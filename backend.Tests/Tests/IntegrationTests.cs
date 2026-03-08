using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using backend.DTOs;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

/// <summary>
/// Integration tests covering HTTP endpoints for features #12, #3, #10, #17.
/// Uses TimeClockWebAppFactory for full middleware pipeline testing.
/// All authenticated requests use real login to get valid JWTs (no JwtTokenFactory shortcuts).
/// </summary>
public class IntegrationTests : IClassFixture<TimeClockWebAppFactory>
{
    private readonly TimeClockWebAppFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IntegrationTests(TimeClockWebAppFactory factory)
    {
        _factory = factory;
        _factory.SetZurichTime(new DateTime(2026, 3, 7, 10, 0, 0));
    }

    /// <summary>Login as the seeded test admin and return an authenticated HttpClient.</summary>
    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "testadmin", password = "Admin123!" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "admin login must succeed");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.Token);
        return client;
    }

    /// <summary>Register an employee via admin and return an authenticated HttpClient.</summary>
    private async Task<(HttpClient client, int userId)> LoginAsEmployeeAsync(string username)
    {
        var adminClient = await LoginAsAdminAsync();

        // Register employee (ignore 400 if already exists)
        await adminClient.PostAsJsonAsync("/api/admin/employees",
            new { username, password = "Employee123!", fullName = $"Test {username}", role = "Employee" });

        // Get employee ID
        var employees = await adminClient.GetFromJsonAsync<JsonElement>("/api/admin/employees");
        var emp = employees.EnumerateArray().First(e => e.GetProperty("username").GetString() == username);
        var empId = emp.GetProperty("id").GetInt32();

        // Login as employee
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = "Employee123!" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"employee {username} login must succeed");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        return (client, empId);
    }

    // ── #12: Notes at Clock-In/Out ─────────────────────────────────────────────

    [Fact]
    public async Task ClockIn_WithNotes_ReturnsSuccess()
    {
        _factory.SetZurichTime(new DateTime(2026, 3, 7, 10, 0, 0));
        var (client, _) = await LoginAsEmployeeAsync("notes_emp1");

        var response = await client.PostAsJsonAsync("/api/attendance/clock-in",
            new { notes = "Starting morning shift" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClockResponse>(JsonOptions);
        body!.IsClockedIn.Should().BeTrue();

        // Clean up: clock out
        _factory.SetZurichTime(new DateTime(2026, 3, 7, 18, 0, 0));
        await client.PostAsJsonAsync("/api/attendance/clock-out", new { notes = (string?)null });
    }

    [Fact]
    public async Task ClockOut_WithNotes_AppendsToClockInNote()
    {
        var (client, _) = await LoginAsEmployeeAsync("notes_emp2");

        _factory.SetZurichTime(new DateTime(2026, 3, 7, 8, 0, 0));
        await client.PostAsJsonAsync("/api/attendance/clock-in",
            new { notes = "Morning" });

        _factory.SetZurichTime(new DateTime(2026, 3, 7, 16, 0, 0));
        var response = await client.PostAsJsonAsync("/api/attendance/clock-out",
            new { notes = "Leaving" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check history — notes should be "Morning | Leaving"
        var history = await client.GetFromJsonAsync<JsonElement>("/api/attendance/history");
        var firstEntry = history.GetProperty("items")[0];
        firstEntry.GetProperty("notes").GetString().Should().Be("Morning | Leaving");
    }

    // ── #3: CSV Export ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_CsvFormat_ReturnsCsvContentType()
    {
        var adminClient = await LoginAsAdminAsync();
        var (_, empId) = await LoginAsEmployeeAsync("csv_emp");

        // Seed a completed entry
        await _factory.SeedTimeEntryAsync(empId,
            new DateTime(2026, 3, 7, 8, 0, 0),
            new DateTime(2026, 3, 7, 16, 0, 0));

        var response = await adminClient.GetAsync($"/api/admin/reports/{empId}?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Date,Clock In,Clock Out");
        csv.Should().Contain("Total Hours");
    }

    // ── #3: Hourly Rate ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHourlyRate_ReturnsOkAndPersists()
    {
        var adminClient = await LoginAsAdminAsync();
        var (_, empId) = await LoginAsEmployeeAsync("rate_emp");

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{empId}/hourly-rate",
            new { hourlyRate = 55.00 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify via employees list
        var employees = await adminClient.GetFromJsonAsync<JsonElement>("/api/admin/employees");
        var emp = employees.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == empId);
        emp.GetProperty("hourlyRate").GetDecimal().Should().Be(55.00m);
    }

    // ── #10: Audit Logs ────────────────────────────────────────────────────────

    [Fact]
    public async Task EditEntry_ThenGetAuditLogs_ReturnsChangeHistory()
    {
        var adminClient = await LoginAsAdminAsync();
        var (_, empId) = await LoginAsEmployeeAsync("audit_emp");
        var entryId = await _factory.SeedTimeEntryAsync(empId,
            new DateTime(2026, 3, 7, 8, 0, 0),
            new DateTime(2026, 3, 7, 16, 0, 0));

        _factory.SetZurichTime(new DateTime(2026, 3, 7, 17, 0, 0));

        // Edit the entry (changes Notes) — this should generate audit logs
        var editResponse = await adminClient.PutAsJsonAsync($"/api/admin/attendance/{entryId}",
            new { notes = "Admin correction" });
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fetch audit logs
        var response = await adminClient.GetAsync($"/api/admin/attendance/{entryId}/audit");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        logs.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAuditLogs_NonexistentEntry_Returns404()
    {
        var adminClient = await LoginAsAdminAsync();

        var response = await adminClient.GetAsync("/api/admin/attendance/99999/audit");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── #17: Refresh Token ─────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ThenRefresh_ReturnsNewTokenPair()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "testadmin", password = "Admin123!" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        loginBody!.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = loginBody.RefreshToken });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>(JsonOptions);
        refreshBody!.Token.Should().NotBeNullOrWhiteSpace();
        refreshBody.RefreshToken.Should().NotBe(loginBody.RefreshToken);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "testadmin", password = "Admin123!" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = loginBody!.RefreshToken });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Try to use the revoked token
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = loginBody.RefreshToken });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "invalid-token-value" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authorization checks ───────────────────────────────────────────────────

    [Fact]
    public async Task AuditLogs_RequiresAdminRole()
    {
        var (empClient, _) = await LoginAsEmployeeAsync("audit_nonadmin");

        var response = await empClient.GetAsync("/api/admin/attendance/1/audit");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HourlyRate_RequiresAdminRole()
    {
        var (empClient, _) = await LoginAsEmployeeAsync("rate_nonadmin");

        var response = await empClient.PutAsJsonAsync("/api/admin/users/1/hourly-rate",
            new { hourlyRate = 50.00 });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CsvExport_RequiresAdminRole()
    {
        var (empClient, _) = await LoginAsEmployeeAsync("csv_nonadmin");

        var response = await empClient.GetAsync("/api/admin/reports/1?format=csv");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
