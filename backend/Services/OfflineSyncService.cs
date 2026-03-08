using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Services;

/// <summary>
/// Background service that periodically drains the offline JSON queue into the database.
/// Runs every 3 minutes.
/// </summary>
public class OfflineSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OfflineQueueService _queue;
    private readonly ILogger<OfflineSyncService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(3);

    public OfflineSyncService(
        IServiceProvider serviceProvider,
        OfflineQueueService queue,
        ILogger<OfflineSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OfflineSyncService started — syncing every {Interval} minutes", _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);
            await TrySyncAsync(stoppingToken);
        }
    }

    private async Task TrySyncAsync(CancellationToken ct)
    {
        List<OfflineEntry> pending;
        try
        {
            pending = await _queue.GetPendingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OfflineSyncService: failed to read offline queue file");
            return;
        }

        if (pending.Count == 0)
            return;

        _logger.LogInformation("OfflineSyncService: attempting to sync {Count} pending entries", pending.Count);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Quick connectivity check
        try
        {
            if (!await db.Database.CanConnectAsync(ct))
            {
                _logger.LogWarning("OfflineSyncService: database still unreachable, will retry next cycle");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OfflineSyncService: database connectivity check failed");
            return;
        }

        var syncedIds = new List<string>();

        // Process entries in chronological order
        foreach (var entry in pending.OrderBy(e => e.QueuedAt))
        {
            try
            {
                if (entry.Action == "ClockIn")
                {
                    await SyncClockInAsync(db, entry, ct);
                }
                else if (entry.Action == "ClockOut")
                {
                    await SyncClockOutAsync(db, entry, ct);
                }
                else
                {
                    _logger.LogWarning("OfflineSyncService: unknown action '{Action}' for entry {Id}, discarding",
                        entry.Action, entry.Id);
                }

                syncedIds.Add(entry.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OfflineSyncService: failed to sync entry {Id} ({Action} for user {UserId})",
                    entry.Id, entry.Action, entry.UserId);
                // Keep this entry in the queue for the next cycle
            }
        }

        if (syncedIds.Count > 0)
        {
            await _queue.RemoveSyncedAsync(syncedIds);
            _logger.LogInformation("OfflineSyncService: successfully synced {Count}/{Total} entries",
                syncedIds.Count, pending.Count);
        }
    }

    private static async Task SyncClockInAsync(AppDbContext db, OfflineEntry entry, CancellationToken ct)
    {
        // Avoid duplicates: check if user already has an open entry or an entry at this exact timestamp
        var exists = await db.TimeEntries.AnyAsync(
            t => t.UserId == entry.UserId && t.ClockIn == entry.Timestamp, ct);
        if (exists)
            return; // Already synced (idempotent)

        // Auto-close any stale open entry (mirrors AttendanceService logic)
        var openEntry = await db.TimeEntries
            .FirstOrDefaultAsync(t => t.UserId == entry.UserId && t.ClockOut == null, ct);
        if (openEntry != null)
        {
            var openHours = (entry.Timestamp - openEntry.ClockIn).TotalHours;
            if (openHours > 16)
            {
                openEntry.ClockOut = openEntry.ClockIn.AddHours(16);
                openEntry.DurationMinutes = 16 * 60;
                openEntry.Notes = (openEntry.Notes ?? "") + " [Auto-closed: exceeded 16h limit during offline sync]";
                openEntry.IsManuallyEdited = true;
            }
            else
            {
                // There's already an open entry within a valid time range — skip this clock-in
                return;
            }
        }

        db.TimeEntries.Add(new TimeEntry
        {
            UserId = entry.UserId,
            ClockIn = entry.Timestamp,
            Notes = string.IsNullOrEmpty(entry.Notes) ? "[Synced from offline queue]"
                : $"{entry.Notes} [Synced from offline queue]"
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncClockOutAsync(AppDbContext db, OfflineEntry entry, CancellationToken ct)
    {
        var openEntry = await db.TimeEntries
            .FirstOrDefaultAsync(t => t.UserId == entry.UserId && t.ClockOut == null, ct);

        if (openEntry == null)
            return; // Nothing to clock out — entry may have been auto-closed

        var durationMinutes = (entry.Timestamp - openEntry.ClockIn).TotalMinutes;
        if (durationMinutes < 1)
            return; // Too short, skip

        openEntry.ClockOut = entry.Timestamp;
        openEntry.DurationMinutes = durationMinutes;

        var noteTag = "[Synced from offline queue]";
        if (!string.IsNullOrEmpty(entry.Notes))
        {
            openEntry.Notes = string.IsNullOrEmpty(openEntry.Notes)
                ? $"{entry.Notes} {noteTag}"
                : $"{openEntry.Notes} | {entry.Notes} {noteTag}";
        }
        else if (string.IsNullOrEmpty(openEntry.Notes))
        {
            openEntry.Notes = noteTag;
        }
        else
        {
            openEntry.Notes += $" {noteTag}";
        }

        await db.SaveChangesAsync(ct);
    }
}
