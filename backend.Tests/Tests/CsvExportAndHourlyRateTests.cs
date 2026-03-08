using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

/// <summary>
/// Feature #3 — CSV Export + Hourly Rate
/// Tests CSV generation (RFC 4180, BOM, injection protection), hourly rate CRUD, estimated pay.
/// </summary>
public class CsvExportAndHourlyRateTests
{
    private static AdminService CreateService(AppDbContext db, DateTime? zurichNow = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new AdminService(
            db,
            NullLogger<AdminService>.Instance,
            TimeServiceMockFactory.CreateObject(zurichNow),
            cache);
    }

    private static async Task<(AppDbContext db, User user)> ArrangeUserWithEntries(
        decimal? hourlyRate = null,
        params (DateTime clockIn, DateTime clockOut, string? notes)[] entries)
    {
        var db = DbContextFactory.Create();
        var user = new User
        {
            Username = "csvuser",
            PasswordHash = "x",
            FullName = "CSV Test User",
            HourlyRate = hourlyRate
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        foreach (var (clockIn, clockOut, notes) in entries)
        {
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = user.Id,
                ClockIn = clockIn,
                ClockOut = clockOut,
                DurationMinutes = (clockOut - clockIn).TotalMinutes,
                Notes = notes
            });
        }
        await db.SaveChangesAsync();

        return (db, user);
    }

    // ── CSV Export ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployeeReportCsv_ReturnsValidCsvWithHeaders()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null));

        var svc = CreateService(db);
        var (bytes, fileName) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);

        var csv = Encoding.UTF8.GetString(bytes);

        // Remove BOM for easier assertion
        csv = csv.TrimStart('\uFEFF');

        // Header row
        csv.Should().StartWith("Date,Clock In,Clock Out,Duration (h),Notes,Edited");

        // Data row
        csv.Should().Contain("2026-03-07,08:00,16:00,8.00,,No");

        // File name includes user's name
        fileName.Should().Contain("CSV_Test_User");
        fileName.Should().EndWith(".csv");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_StartsWithUtf8Bom()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);

        // UTF-8 BOM: 0xEF, 0xBB, 0xBF
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
    }

    [Fact]
    public async Task GetEmployeeReportCsv_EscapesFormulaInjection()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), "=SUM(A1:A10)"));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        // Formula prefix chars (=, +, -, @) are prefixed with a tab
        csv.Should().Contain("\t=SUM(A1:A10)");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_EscapesCommasAndQuotes()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), "Note with, comma"));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        // RFC 4180: fields with commas are wrapped in double quotes
        csv.Should().Contain("\"Note with, comma\"");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_IncludesSummaryRows()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: 50.00m,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null),
            (new DateTime(2026, 3, 8, 9, 0, 0), new DateTime(2026, 3, 8, 17, 0, 0), null));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain("Total Hours,16.00");
        csv.Should().Contain("Hourly Rate (CHF),50.00");
        csv.Should().Contain("Estimated Pay (CHF),800.00");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_WithoutHourlyRate_OmitsRateAndPay()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain("Total Hours");
        csv.Should().NotContain("Hourly Rate");
        csv.Should().NotContain("Estimated Pay");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_DateRange_FiltersEntries()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 1, 8, 0, 0), new DateTime(2026, 3, 1, 16, 0, 0), null),
            (new DateTime(2026, 3, 5, 8, 0, 0), new DateTime(2026, 3, 5, 16, 0, 0), null),
            (new DateTime(2026, 3, 10, 8, 0, 0), new DateTime(2026, 3, 10, 16, 0, 0), null));

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(
            user.Id,
            from: new DateTime(2026, 3, 4),
            to: new DateTime(2026, 3, 6));
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain("2026-03-05");
        csv.Should().NotContain("2026-03-01");
        csv.Should().NotContain("2026-03-10");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_UserNotFound_ThrowsKeyNotFound()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.GetEmployeeReportCsvAsync(999, null, null))
            .Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetEmployeeReportCsv_ManuallyEditedEntry_ShowsYes()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.TimeEntries.Add(new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480,
            IsManuallyEdited = true
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var (bytes, _) = await svc.GetEmployeeReportCsvAsync(user.Id, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain(",Yes");
    }

    // ── Hourly Rate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHourlyRate_ValidRate_SetsOnUser()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.UpdateHourlyRateAsync(user.Id, 45.50m);

        db.Users.Single(u => u.Id == user.Id).HourlyRate.Should().Be(45.50m);
    }

    [Fact]
    public async Task UpdateHourlyRate_ZeroRate_Succeeds()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.UpdateHourlyRateAsync(user.Id, 0m);

        db.Users.Single(u => u.Id == user.Id).HourlyRate.Should().Be(0m);
    }

    [Fact]
    public async Task UpdateHourlyRate_NegativeRate_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await svc.Invoking(s => s.UpdateHourlyRateAsync(user.Id, -5m))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*negative*");
    }

    [Fact]
    public async Task UpdateHourlyRate_UserNotFound_ThrowsKeyNotFound()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.UpdateHourlyRateAsync(999, 50m))
            .Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    // ── Estimated Pay in Report ────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployeeReport_WithHourlyRate_IncludesEstimatedPay()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: 30.00m,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null)); // 8h

        var svc = CreateService(db);
        var report = await svc.GetEmployeeReportAsync(user.Id, null, null);

        report.HourlyRate.Should().Be(30.00m);
        report.EstimatedPay.Should().Be(240.00m); // 8h × 30
    }

    [Fact]
    public async Task GetEmployeeReport_WithoutHourlyRate_EstimatedPayIsNull()
    {
        var (db, user) = await ArrangeUserWithEntries(
            hourlyRate: null,
            (new DateTime(2026, 3, 7, 8, 0, 0), new DateTime(2026, 3, 7, 16, 0, 0), null));

        var svc = CreateService(db);
        var report = await svc.GetEmployeeReportAsync(user.Id, null, null);

        report.HourlyRate.Should().BeNull();
        report.EstimatedPay.Should().BeNull();
    }

    [Fact]
    public async Task GetEmployees_IncludesHourlyRate()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User
        {
            Username = "emp", PasswordHash = "x", FullName = "Emp",
            HourlyRate = 75.25m
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var employees = await svc.GetEmployeesAsync();

        employees.Should().ContainSingle(e => e.HourlyRate == 75.25m);
    }
}
