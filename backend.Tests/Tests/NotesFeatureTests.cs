using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

/// <summary>
/// Feature #12 — Notes at Clock-In/Out
/// Tests ClockInAsync/ClockOutAsync note handling: storage, appending with | separator, validation.
/// </summary>
public class NotesFeatureTests
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

    // ── ClockIn Notes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClockIn_WithNote_SavesNoteOnEntry()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var zurich = new DateTime(2026, 3, 7, 9, 0, 0);
        var svc = CreateService(db, zurich);

        await svc.ClockInAsync(1, "Morning shift");

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().Be("Morning shift");
    }

    [Fact]
    public async Task ClockIn_WithNullNote_SavesNullNotes()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new DateTime(2026, 3, 7, 9, 0, 0));
        await svc.ClockInAsync(1, null);

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().BeNull();
    }

    [Fact]
    public async Task ClockIn_WithEmptyNote_SavesNullNotes()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new DateTime(2026, 3, 7, 9, 0, 0));
        await svc.ClockInAsync(1, "   ");

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().BeNull();
    }

    [Fact]
    public async Task ClockIn_NoteExceeds500Chars_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new DateTime(2026, 3, 7, 9, 0, 0));

        await svc.Invoking(s => s.ClockInAsync(1, new string('x', 501)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task ClockIn_NoteExactly500Chars_Succeeds()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new DateTime(2026, 3, 7, 9, 0, 0));
        await svc.ClockInAsync(1, new string('x', 500));

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().HaveLength(500);
    }

    [Fact]
    public async Task ClockIn_NoteIsTrimmed()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 1, Username = "emp", PasswordHash = "x", FullName = "Employee" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new DateTime(2026, 3, 7, 9, 0, 0));
        await svc.ClockInAsync(1, "  hello world  ");

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().Be("hello world");
    }

    // ── ClockOut Notes ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ClockOut_WithNote_WhenNoClockInNote_SetsNoteDirectly()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 1, ClockIn = clockIn, Notes = null });
        await db.SaveChangesAsync();

        var svc = CreateService(db, clockIn.AddHours(8));
        await svc.ClockOutAsync(1, "Done for the day");

        var entry = db.TimeEntries.Single(e => e.UserId == 1);
        entry.Notes.Should().Be("Done for the day");
    }

    [Fact]
    public async Task ClockOut_WithNote_WhenClockInNoteExists_AppendsSeparatedByPipe()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 2, ClockIn = clockIn, Notes = "Morning shift" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, clockIn.AddHours(8));
        await svc.ClockOutAsync(2, "Finished early");

        var entry = db.TimeEntries.Single(e => e.UserId == 2);
        entry.Notes.Should().Be("Morning shift | Finished early");
    }

    [Fact]
    public async Task ClockOut_WithNullNote_PreservesClockInNote()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 3, ClockIn = clockIn, Notes = "Started shift" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, clockIn.AddHours(8));
        await svc.ClockOutAsync(3, null);

        var entry = db.TimeEntries.Single(e => e.UserId == 3);
        entry.Notes.Should().Be("Started shift");
    }

    [Fact]
    public async Task ClockOut_NoteExceeds500Chars_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 4, ClockIn = clockIn });
        await db.SaveChangesAsync();

        var svc = CreateService(db, clockIn.AddHours(8));

        await svc.Invoking(s => s.ClockOutAsync(4, new string('x', 501)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task ClockOut_WithEmptyNote_DoesNotModifyExistingNotes()
    {
        var db = DbContextFactory.Create();
        var clockIn = new DateTime(2026, 3, 7, 8, 0, 0);
        db.TimeEntries.Add(new TimeEntry { UserId = 5, ClockIn = clockIn, Notes = "Original note" });
        await db.SaveChangesAsync();

        var svc = CreateService(db, clockIn.AddHours(8));
        await svc.ClockOutAsync(5, "   ");

        var entry = db.TimeEntries.Single(e => e.UserId == 5);
        entry.Notes.Should().Be("Original note");
    }
}
