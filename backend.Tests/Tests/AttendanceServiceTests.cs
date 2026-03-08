using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

public class AttendanceServiceTests
{
    private static OfflineQueueService CreateOfflineQueue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OfflineQueue:DataDir"] = Path.Combine(Path.GetTempPath(), "timeclock-tests-" + Guid.NewGuid())
            })
            .Build();
        return new OfflineQueueService(config, NullLogger<OfflineQueueService>.Instance);
    }

    private static AttendanceService CreateService(AppDbContext db, DateTime? zurichNow = null)
        => new(db, TimeServiceMockFactory.CreateObject(zurichNow), CreateOfflineQueue(), NullLogger<AttendanceService>.Instance);

    // ── ClockIn ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClockIn_WhenNotClockedIn_ReturnsClockedInResponse()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var zurich = new DateTime(2026, 3, 7, 9, 0, 0);
        var svc = CreateService(db, zurich);

        var result = await svc.ClockInAsync(1);

        result.IsClockedIn.Should().BeTrue();
        result.Timestamp.Should().Be(zurich);
        result.LastClockIn.Should().Be(zurich);
        db.TimeEntries.Should().ContainSingle(e => e.UserId == 1 && e.ClockOut == null);
    }

    [Fact]
    public async Task ClockIn_WhenAlreadyClockedIn_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var zurich = new DateTime(2026, 3, 7, 9, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 1, ClockIn = zurich.AddHours(-2) });
        await db.SaveChangesAsync();

        var svc = CreateService(db, zurich);

        await svc.Invoking(s => s.ClockInAsync(1))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already clocked in*");
    }

    [Fact]
    public async Task ClockIn_WhenStaleEntryExceedsMaxShiftHours_AutoClosesAndClocksInAgain()
    {
        var db = DbContextFactory.Create();
        // Open entry started 20 hours ago (> 16h limit)
        var staleClockIn = new DateTime(2026, 3, 7, 0, 0, 0);
        db.TimeEntries.Add(new TimeEntry { Id = 1, UserId = 1, ClockIn = staleClockIn });
        await db.SaveChangesAsync();

        var zurichNow = staleClockIn.AddHours(20); // 20:00 same day
        var svc = CreateService(db, zurichNow);

        var result = await svc.ClockInAsync(1);

        result.IsClockedIn.Should().BeTrue();

        var stale = db.TimeEntries.Single(e => e.Id == 1);
        stale.ClockOut.Should().Be(staleClockIn.AddHours(16));
        stale.DurationMinutes.Should().Be(16 * 60);
        stale.Notes.Should().Contain("Auto-closed");

        // New open entry was created
        db.TimeEntries.Count(e => e.UserId == 1 && e.ClockOut == null).Should().Be(1);
    }

    [Fact]
    public async Task ClockIn_WhenOpenEntryNotYetStale_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 2, ClockIn = clockIn });
        await db.SaveChangesAsync();

        var zurichNow = clockIn.AddHours(6); // Only 6h open — well within 16h limit
        var svc = CreateService(db, zurichNow);

        await svc.Invoking(s => s.ClockInAsync(2))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already clocked in*");
    }

    // ── ClockOut ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClockOut_WhenClockedIn_SetsDurationAndReturnsResponse()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 3, ClockIn = clockIn });
        await db.SaveChangesAsync();

        var clockOutTime = clockIn.AddHours(8);
        var svc = CreateService(db, clockOutTime);

        var result = await svc.ClockOutAsync(3);

        result.IsClockedIn.Should().BeFalse();
        result.Timestamp.Should().Be(clockOutTime);

        var entry = db.TimeEntries.Single(e => e.UserId == 3);
        entry.ClockOut.Should().Be(clockOutTime);
        entry.DurationMinutes.Should().Be(480);
    }

    [Fact]
    public async Task ClockOut_WhenNotClockedIn_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.ClockOutAsync(99))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not clocked in*");
    }

    [Fact]
    public async Task ClockOut_WhenLessThanOneMinuteSinceClockedIn_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 9, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 4, ClockIn = clockIn });
        await db.SaveChangesAsync();

        // Only 30 seconds later
        var svc = CreateService(db, clockIn.AddSeconds(30));

        await svc.Invoking(s => s.ClockOutAsync(4))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least 1 minute*");
    }

    // ── GetStatus ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_WhenNotClockedIn_ReturnsNotClockedIn()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        var status = await svc.GetStatusAsync(5);

        status.IsClockedIn.Should().BeFalse();
        status.LastClockIn.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_WhenClockedIn_ReturnsOpenEntry()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 9, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 6, ClockIn = clockIn });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var status = await svc.GetStatusAsync(6);

        status.IsClockedIn.Should().BeTrue();
        status.LastClockIn.Should().Be(clockIn);
    }

    // ── GetHistory ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_WithDateRange_ReturnsOnlyOverlappingEntries()
    {
        var db = DbContextFactory.Create();
        var day1 = new DateTime(2026, 3, 1, 8, 0, 0);
        var day2 = new DateTime(2026, 3, 5, 8, 0, 0);
        var day3 = new DateTime(2026, 3, 10, 8, 0, 0);

        db.TimeEntries.AddRange(
            new TimeEntry { UserId = 7, ClockIn = day1, ClockOut = day1.AddHours(8), DurationMinutes = 480 },
            new TimeEntry { UserId = 7, ClockIn = day2, ClockOut = day2.AddHours(8), DurationMinutes = 480 },
            new TimeEntry { UserId = 7, ClockIn = day3, ClockOut = day3.AddHours(8), DurationMinutes = 480 }
        );
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var result = await svc.GetHistoryAsync(
            7,
            from: new DateTime(2026, 3, 3),
            to: new DateTime(2026, 3, 7),
            page: 1, pageSize: 50);

        result.TotalCount.Should().Be(1);
        result.Items.Single().ClockIn.Should().Be(day2);
    }

    [Fact]
    public async Task GetHistory_MidnightCrossingShift_IncludedInNextDayRange()
    {
        var db = DbContextFactory.Create();
        // Shift starts March 7 at 23:00 and ends March 8 at 07:00
        var crossIn = new DateTime(2026, 3, 7, 23, 0, 0);
        var crossOut = new DateTime(2026, 3, 8, 7, 0, 0);
        db.TimeEntries.Add(new TimeEntry
        {
            UserId = 8, ClockIn = crossIn, ClockOut = crossOut,
            DurationMinutes = (crossOut - crossIn).TotalMinutes
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        // Querying just March 8 should include this shift (overlap)
        var result = await svc.GetHistoryAsync(
            8,
            from: new DateTime(2026, 3, 8),
            to: new DateTime(2026, 3, 8),
            page: 1, pageSize: 50);

        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_Pagination_ReturnsCorrectPage()
    {
        var db = DbContextFactory.Create();
        var entries = Enumerable.Range(0, 25).Select(i => new TimeEntry
        {
            UserId = 9,
            ClockIn = new DateTime(2026, 1, 1).AddDays(i),
            ClockOut = new DateTime(2026, 1, 1).AddDays(i).AddHours(8),
            DurationMinutes = 480
        });
        db.TimeEntries.AddRange(entries);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        var page1 = await svc.GetHistoryAsync(9, null, null, page: 1, pageSize: 10);
        var page2 = await svc.GetHistoryAsync(9, null, null, page: 2, pageSize: 10);
        var page3 = await svc.GetHistoryAsync(9, null, null, page: 3, pageSize: 10);

        page1.Items.Should().HaveCount(10);
        page2.Items.Should().HaveCount(10);
        page3.Items.Should().HaveCount(5);
        page1.TotalCount.Should().Be(25);
        page1.TotalPages.Should().Be(3);
        page1.HasNextPage.Should().BeTrue();
        page3.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistory_FromAfterTo_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.GetHistoryAsync(
                1,
                from: new DateTime(2026, 3, 10),
                to: new DateTime(2026, 3, 1),
                page: 1, pageSize: 50))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'from' date must be before or equal to 'to' date*");
    }

    [Fact]
    public async Task GetHistory_SameDateFromAndTo_ReturnsEntriesOnThatDay()
    {
        var db = DbContextFactory.Create();
        var targetDay = new DateTime(2026, 3, 5, 8, 0, 0);
        db.TimeEntries.AddRange(
            new TimeEntry { UserId = 10, ClockIn = targetDay, ClockOut = targetDay.AddHours(8), DurationMinutes = 480 },
            new TimeEntry { UserId = 10, ClockIn = new DateTime(2026, 3, 6, 8, 0, 0), ClockOut = new DateTime(2026, 3, 6, 16, 0, 0), DurationMinutes = 480 }
        );
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var result = await svc.GetHistoryAsync(10,
            from: new DateTime(2026, 3, 5),
            to: new DateTime(2026, 3, 5),
            page: 1, pageSize: 50);

        result.TotalCount.Should().Be(1);
        result.Items.Single().ClockIn.Should().Be(targetDay);
    }
}
