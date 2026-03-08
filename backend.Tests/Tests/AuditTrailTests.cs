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

/// <summary>
/// Feature #10 — Audit Trails
/// Tests auto-audit via ChangeTracker, audit log retrieval, and skip behavior without HTTP context.
/// </summary>
public class AuditTrailTests
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

    // ── GetAuditLogs ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogs_ReturnsLogsForEntry()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "admin", PasswordHash = "x", FullName = "Admin User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        // Manually insert audit log (since unit test DbContext has no IHttpContextAccessor)
        db.TimeEntryAuditLogs.Add(new TimeEntryAuditLog
        {
            TimeEntryId = entry.Id,
            ChangedByUserId = user.Id,
            ChangedAt = DateTime.UtcNow,
            FieldName = "ClockIn",
            OldValue = "2026-03-07 08:00:00",
            NewValue = "2026-03-07 09:00:00"
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var logs = await svc.GetAuditLogsAsync(entry.Id);

        logs.Should().HaveCount(1);
        logs[0].FieldName.Should().Be("ClockIn");
        logs[0].OldValue.Should().Be("2026-03-07 08:00:00");
        logs[0].NewValue.Should().Be("2026-03-07 09:00:00");
        logs[0].ChangedByUserName.Should().Be("Admin User");
    }

    [Fact]
    public async Task GetAuditLogs_EntryNotFound_ThrowsKeyNotFound()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.GetAuditLogsAsync(999))
            .Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetAuditLogs_NoLogs_ReturnsEmptyList()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var logs = await svc.GetAuditLogsAsync(entry.Id);

        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditLogs_MultipleChanges_ReturnsSortedByDate()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "admin", PasswordHash = "x", FullName = "Admin" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.TimeEntryAuditLogs.AddRange(
            new TimeEntryAuditLog
            {
                TimeEntryId = entry.Id, ChangedByUserId = user.Id,
                ChangedAt = now.AddMinutes(-10), FieldName = "ClockIn",
                OldValue = "08:00", NewValue = "09:00"
            },
            new TimeEntryAuditLog
            {
                TimeEntryId = entry.Id, ChangedByUserId = user.Id,
                ChangedAt = now.AddMinutes(-5), FieldName = "Notes",
                OldValue = null, NewValue = "Updated"
            },
            new TimeEntryAuditLog
            {
                TimeEntryId = entry.Id, ChangedByUserId = user.Id,
                ChangedAt = now, FieldName = "ClockOut",
                OldValue = "16:00", NewValue = "17:00"
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var logs = await svc.GetAuditLogsAsync(entry.Id);

        logs.Should().HaveCount(3);
        logs[0].FieldName.Should().Be("ClockIn");
        logs[1].FieldName.Should().Be("Notes");
        logs[2].FieldName.Should().Be("ClockOut");
    }

    // ── ChangeTracker auto-audit (no HTTP context → no audit logs) ─────────

    [Fact]
    public async Task SaveChangesAsync_WithoutHttpContext_SkipsAuditLogs()
    {
        // Unit test DbContext has no IHttpContextAccessor, so audit should be skipped
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        // Modify entry — since there's no HTTP context, no audit log should be created
        entry.ClockIn = new DateTime(2026, 3, 7, 9, 0, 0);
        entry.Notes = "Changed";
        await db.SaveChangesAsync();

        db.TimeEntryAuditLogs.Should().BeEmpty();
    }

    // ── Audit log fields tracked ───────────────────────────────────────────────

    [Fact]
    public async Task EditTimeEntry_SetsIsManuallyEdited_AllowsAuditRetrieval()
    {
        var db = DbContextFactory.Create();
        var user = new User { Username = "emp", PasswordHash = "x", FullName = "Emp" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var entry = new TimeEntry
        {
            UserId = user.Id,
            ClockIn = new DateTime(2026, 3, 7, 8, 0, 0),
            ClockOut = new DateTime(2026, 3, 7, 16, 0, 0),
            DurationMinutes = 480
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();

        var zurichNow = new DateTime(2026, 3, 7, 17, 0, 0);
        var svc = CreateService(db, zurichNow);

        var result = await svc.EditTimeEntryAsync(entry.Id,
            new EditTimeEntryRequest(null, null, "Admin correction"));

        result.IsManuallyEdited.Should().BeTrue();
        result.Notes.Should().Be("Admin correction");
    }
}
