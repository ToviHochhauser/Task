using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using backend.Data;
using backend.Middleware;
using backend.Models;
using backend.Services;

namespace backend.Tests.Helpers;

/// <summary>
/// A shared WebApplicationFactory for integration tests.
///
/// What it does:
///   1. Injects appsettings.Test.json so JWT key/issuer/audience are set at startup.
///   2. Replaces the SQL Server DbContext with an EF Core InMemory database.
///   3. Replaces ITimeService with a controllable Moq mock so tests can pin Zurich time.
///
/// Usage (xUnit IClassFixture):
/// <code>
///   public class MyIntegrationTests : IClassFixture&lt;TimeClockWebAppFactory&gt;
///   {
///       private readonly TimeClockWebAppFactory _factory;
///       public MyIntegrationTests(TimeClockWebAppFactory factory) => _factory = factory;
///   }
/// </code>
/// </summary>
public class TimeClockWebAppFactory : WebApplicationFactory<Program>
{
    // Each factory instance owns its own isolated InMemory database.
    private readonly string _databaseName = $"TestDb_{Guid.NewGuid()}";

    // ── Controllable time mock ───────────────────────────────────────────────
    // Exposed so individual integration tests can override the returned time:
    //   _factory.SetZurichTime(new DateTime(2026, 3, 7, 23, 30, 0));
    public Mock<ITimeService> MockTimeService { get; } =
        TimeServiceMockFactory.Create();

    /// <summary>
    /// Convenience setter — changes what <c>GetZurichTimeAsync</c> returns.
    /// Call before making the HTTP request you want to time-control.
    /// </summary>
    public void SetZurichTime(DateTime zurichTime) =>
        MockTimeService
            .Setup(s => s.GetZurichTimeAsync())
            .ReturnsAsync(zurichTime);

    // ── Pre-built authenticated clients ─────────────────────────────────────

    /// <summary>
    /// Returns an HttpClient whose Authorization header carries a valid Admin JWT.
    /// The userId must match a user that exists in the InMemory DB (the seeded
    /// testadmin gets ID 1 via Program.cs startup seed).
    /// </summary>
    public HttpClient CreateAdminClient(int userId = 1, string username = "testadmin")
    {
        var client = CreateClient();
        AttachBearer(client, JwtTokenFactory.GenerateAdminToken(userId, username));
        return client;
    }

    /// <summary>
    /// Seeds a test employee into the InMemory DB (if not already present) and
    /// returns an authenticated HttpClient for that user.
    /// Call <c>await</c> before making requests — seeding is async.
    /// </summary>
    public async Task<HttpClient> CreateEmployeeClientAsync(
        string username = "testemployee",
        string fullName = "Test Employee")
    {
        var userId = await SeedUserAsync(username, "Employee123!", fullName, "Employee");
        var client = CreateClient();
        AttachBearer(client, JwtTokenFactory.GenerateEmployeeToken(userId, username));
        return client;
    }

    // ── Database seeding helpers ─────────────────────────────────────────────

    /// <summary>
    /// Seeds a user and returns their auto-generated ID.
    /// If a user with the same username already exists, returns the existing ID.
    /// </summary>
    public async Task<int> SeedUserAsync(
        string username,
        string password,
        string fullName,
        string role = "Employee")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (existing is not null)
            return existing.Id;

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            Role = role,
            IsActive = true,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Seeds a TimeEntry for the given user and returns its ID.
    /// </summary>
    public async Task<int> SeedTimeEntryAsync(
        int userId,
        DateTime clockIn,
        DateTime? clockOut = null,
        string? notes = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = new TimeEntry
        {
            UserId = userId,
            ClockIn = clockIn,
            ClockOut = clockOut,
            DurationMinutes = clockOut.HasValue
                ? (clockOut.Value - clockIn).TotalMinutes
                : null,
            Notes = notes,
        };

        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    // ── WebApplicationFactory overrides ─────────────────────────────────────

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 1. Inject test config BEFORE the app reads it (JWT key validation happens at startup).
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Resolve relative to the test assembly's output directory (where CopyToOutputDirectory places it)
            var testDir = Path.GetDirectoryName(typeof(TimeClockWebAppFactory).Assembly.Location)!;
            config.AddJsonFile(Path.Combine(testDir, "appsettings.Test.json"), optional: false, reloadOnChange: false);
        });

        builder.ConfigureTestServices(services =>
        {
            // 2. Replace SQL Server DbContext with InMemory ──────────────────
            // Remove ALL EF Core / DbContext registrations to avoid dual-provider conflict
            var efDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                          || d.ServiceType == typeof(AppDbContext)
                          || (d.ServiceType.IsGenericType &&
                              d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
                          || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();

            foreach (var d in efDescriptors)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(w =>
                    w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });

            // 3. Replace ITimeService with a controllable mock ────────────────
            // AddHttpClient<ITimeService, WorldTimeApiService>() registers ITimeService
            // as a transient; remove all such registrations before injecting the mock.
            var timeServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(ITimeService))
                .ToList();

            foreach (var d in timeServiceDescriptors)
                services.Remove(d);

            // Singleton so every request within a test sees the same mock instance.
            services.AddSingleton(MockTimeService.Object);

            // 4. Disable login rate limiting for integration tests ─────────
            var rateLimiterDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(LoginRateLimiter));
            if (rateLimiterDescriptor is not null)
                services.Remove(rateLimiterDescriptor);
            // Use a very high limit so integration tests (which make many logins) are never throttled.
            services.AddSingleton(new LoginRateLimiter(maxAttempts: int.MaxValue));

            // 5. Override JWT bearer validation to use the known test key ───
            // Program.cs reads Jwt:Key at startup before the test config file is
            // injected, so the validation key may be the development key while
            // AuthService (resolved from DI at runtime) uses the test key.
            // We override the bearer options here to guarantee they match.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var testSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(JwtTokenFactory.TestKey));
                options.TokenValidationParameters.IssuerSigningKey = testSigningKey;
                options.TokenValidationParameters.ValidIssuer = "TimeClock";
                options.TokenValidationParameters.ValidAudience = "TimeClockUsers";
            });
        });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void AttachBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
}
