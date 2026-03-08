using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

public class AdminServiceTests
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

    private static async Task<(AppDbContext db, int userId, int entryId)> ArrangeUserWithClosedEntry(
        DateTime clockIn, DateTime clockOut)
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = clockIn,
            ClockOut = clockOut,
            DurationMinutes = (clockOut - clockIn).TotalMinutes
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        return (db, user.Id, entry.Id);
    }

    // ── EditTimeEntry ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EditTimeEntry_UpdatesClockIn_ReturnsUpdatedDto()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 7, 17, 0, 0);
        var svc = CreateService(db, zurichNow);

        var newClockIn = new DateTime(2026, 3, 7, 9, 0, 0);
        var result = await svc.EditTimeEntryAsync(entryId,
            new EditTimeEntryRequest(newClockIn, null, null));

        result.ClockIn.Should().Be(newClockIn);
        result.IsManuallyEdited.Should().BeTrue();
        // DurationMinutes recalculated: 16:00 - 09:00 = 420 min
        result.DurationMinutes.Should().Be(420);
    }

    [Fact]
    public async Task EditTimeEntry_FutureClockIn_ThrowsInvalidOperation()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 7, 17, 0, 0);
        var svc = CreateService(db, zurichNow);

        await svc.Invoking(s => s.EditTimeEntryAsync(entryId,
                new EditTimeEntryRequest(zurichNow.AddHours(1), null, null)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*future*");
    }

    [Fact]
    public async Task EditTimeEntry_FutureClockOut_ThrowsInvalidOperation()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 7, 17, 0, 0);
        var svc = CreateService(db, zurichNow);

        await svc.Invoking(s => s.EditTimeEntryAsync(entryId,
                new EditTimeEntryRequest(null, zurichNow.AddHours(2), null)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*future*");
    }

    [Fact]
    public async Task EditTimeEntry_ClockOutBeforeClockIn_ThrowsInvalidOperation()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 8, 12, 0, 0);
        var svc = CreateService(db, zurichNow);

        // Set clockOut to before clockIn
        await svc.Invoking(s => s.EditTimeEntryAsync(entryId,
                new EditTimeEntryRequest(
                    new DateTime(2026, 3, 7, 10, 0, 0),
                    new DateTime(2026, 3, 7, 9, 0, 0),
                    null)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*after clock in*");
    }

    [Fact]
    public async Task EditTimeEntry_ShiftExceeds24Hours_ThrowsInvalidOperation()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 10, 12, 0, 0);
        var svc = CreateService(db, zurichNow);

        await svc.Invoking(s => s.EditTimeEntryAsync(entryId,
                new EditTimeEntryRequest(
                    new DateTime(2026, 3, 7, 8, 0, 0),
                    new DateTime(2026, 3, 8, 9, 0, 0), // 25h duration
                    null)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*24 hours*");
    }

    [Fact]
    public async Task EditTimeEntry_NotesTooLong_ThrowsInvalidOperation()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var zurichNow = new DateTime(2026, 3, 7, 17, 0, 0);
        var svc = CreateService(db, zurichNow);

        await svc.Invoking(s => s.EditTimeEntryAsync(entryId,
                new EditTimeEntryRequest(null, null, new string('x', 501))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task EditTimeEntry_EntryNotFound_ThrowsKeyNotFound()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db, new DateTime(2026, 3, 7, 12, 0, 0));

        await svc.Invoking(s => s.EditTimeEntryAsync(999,
                new EditTimeEntryRequest(null, null, "note")))
            .Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    // ── ReopenTimeEntry ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReopenTimeEntry_Success_ClearsClockOutAndDuration()
    {
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        var clockOut = new DateTime(2026, 3, 7, 16, 0, 0);
        var (db, _, entryId) = await ArrangeUserWithClosedEntry(clockIn, clockOut);

        var svc = CreateService(db);
        var result = await svc.ReopenTimeEntryAsync(entryId);

        result.ClockOut.Should().BeNull();
        result.DurationMinutes.Should().BeNull();
        result.IsManuallyEdited.Should().BeTrue();
    }

    [Fact]
    public async Task ReopenTimeEntry_AlreadyOpen_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "u", PasswordHash = "x", FullName = "U" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry { UserId = user.Id, ClockIn = new DateTime(2026, 3, 7, 8, 0, 0) };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await svc.Invoking(s => s.ReopenTimeEntryAsync(entry.Id))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already open*");
    }

    [Fact]
    public async Task ReopenTimeEntry_UserAlreadyHasOpenEntry_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "u2", PasswordHash = "x", FullName = "U2" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Closed entry we want to reopen
        var closedEntry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 6, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 6, 16, 0, 0),
            DurationMinutes = 480
        };
        // Already-open entry
        var openEntry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0)
        };
        db.TimeEntries.AddRange(closedEntry, openEntry);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await svc.Invoking(s => s.ReopenTimeEntryAsync(closedEntry.Id))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has an open entry*");
    }

    // ── UpdateUserStatus ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserStatus_Deactivate_SetsIsActiveFalse()
    {
        var db = DbContextFactory.Create();
        var user = new User { Id = 0, Username = "target", PasswordHash = "x", FullName = "Target", IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.UpdateUserStatusAsync(user.Id, isActive: false, callingAdminId: 999);

        db.Users.Single(u => u.Id == user.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserStatus_SelfDeactivation_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var admin = new User { Username = "admin", PasswordHash = "x", FullName = "Admin", Role = "Admin" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await svc.Invoking(s => s.UpdateUserStatusAsync(admin.Id, false, callingAdminId: admin.Id))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*own account*");
    }

    // ── GetEmployeesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployeesAsync_ReturnsAllUsers()
    {
        var db = DbContextFactory.Create();
        db.Users.AddRange(
            new User { Username = "emp1", PasswordHash = "x", FullName = "Alice", Role = "Employee" },
            new User { Username = "emp2", PasswordHash = "x", FullName = "Bob", Role = "Admin" }
        );
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var result = await svc.GetEmployeesAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(e => e.Username == "emp1" && e.FullName == "Alice");
        result.Should().Contain(e => e.Username == "emp2" && e.Role == "Admin");
    }

    [Fact]
    public async Task GetEmployeesAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        var result = await svc.GetEmployeesAsync();

        result.Should().BeEmpty();
    }

    // ── GetEmployeeReport ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployeeReport_FromAfterTo_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "rpt", PasswordHash = "x", FullName = "Rpt" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await svc.Invoking(s => s.GetEmployeeReportAsync(
                user.Id,
                from: new DateTime(2026, 3, 10),
                to: new DateTime(2026, 3, 1)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*before or equal*");
    }

    [Fact]
    public async Task GetEmployeeReport_UserNotFound_ThrowsKeyNotFoundException()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.GetEmployeeReportAsync(99999, null, null))
            .Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Employee not found*");
    }
}
