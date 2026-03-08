using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using backend.Data;
using backend.DTOs;
using backend.Extensions;
using backend.Models;

namespace backend.Services;

public class AttendanceService : IAttendanceService
{
    private readonly AppDbContext _db;
    private readonly ITimeService _timeService;
    private readonly OfflineQueueService _offlineQueue;
    private readonly ILogger<AttendanceService> _logger;
    private const double MaxShiftHours = 16;

    public AttendanceService(AppDbContext db, ITimeService timeService,
        OfflineQueueService offlineQueue, ILogger<AttendanceService> logger)
    {
        _db = db;
        _timeService = timeService;
        _offlineQueue = offlineQueue;
        _logger = logger;
    }

    public async Task<ClockResponse> ClockInAsync(int userId, string? notes = null)
    {
        var zurichNow = await _timeService.GetZurichTimeAsync();

        try
        {
            return await ClockInCoreAsync(userId, notes, zurichNow);
        }
        catch (Exception ex) when (IsDbConnectionFailure(ex))
        {
            _logger.LogError(ex, "Database unreachable during clock-in for user {UserId} — saving to offline queue", userId);
            await _offlineQueue.EnqueueAsync(new OfflineEntry
            {
                Action = "ClockIn",
                UserId = userId,
                Timestamp = zurichNow,
                Notes = notes?.Trim()
            });
            return new ClockResponse(
                "Clocked in (offline mode — will sync when database is available).",
                zurichNow, true, zurichNow);
        }
    }

    private async Task<ClockResponse> ClockInCoreAsync(int userId, string? notes, DateTime zurichNow)
    {
        var openEntry = await _db.TimeEntries
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ClockOut == null);

        // Auto-close stale entries open longer than MaxShiftHours (6.3, 12.5)
        if (openEntry != null)
        {
            var openHours = (zurichNow - openEntry.ClockIn).TotalHours;
            if (openHours > MaxShiftHours)
            {
                openEntry.ClockOut = openEntry.ClockIn.AddHours(MaxShiftHours);
                openEntry.DurationMinutes = MaxShiftHours * 60;
                openEntry.Notes = (openEntry.Notes ?? "") + " [Auto-closed: exceeded 16h limit]";
                openEntry.IsManuallyEdited = true;
                await _db.SaveChangesAsync();
                _logger.LogWarning("Auto-closed stale entry {EntryId} for user {UserId} (open {Hours:F1}h)",
                    openEntry.Id, userId, openHours);
                openEntry = null;
            }
        }

        if (openEntry != null)
            throw new InvalidOperationException("You are already clocked in. Please clock out first.");

        // Validate employee note
        var trimmedNote = notes?.Trim();
        if (trimmedNote?.Length > 500)
            throw new InvalidOperationException("Note must be 500 characters or fewer.");

        var entry = new TimeEntry
        {
            UserId = userId,
            ClockIn = zurichNow,
            Notes = string.IsNullOrEmpty(trimmedNote) ? null : trimmedNote
        };

        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} clocked in at {Time} (Zurich)", userId, zurichNow);
        return new ClockResponse("Clocked in successfully.", zurichNow, true, zurichNow);
    }

    public async Task<ClockResponse> ClockOutAsync(int userId, string? notes = null)
    {
        var zurichTime = await _timeService.GetZurichTimeAsync();

        try
        {
            return await ClockOutCoreAsync(userId, notes, zurichTime);
        }
        catch (Exception ex) when (IsDbConnectionFailure(ex))
        {
            _logger.LogError(ex, "Database unreachable during clock-out for user {UserId} — saving to offline queue", userId);
            await _offlineQueue.EnqueueAsync(new OfflineEntry
            {
                Action = "ClockOut",
                UserId = userId,
                Timestamp = zurichTime,
                Notes = notes?.Trim()
            });
            return new ClockResponse(
                "Clocked out (offline mode — will sync when database is available).",
                zurichTime, false, null);
        }
    }

    private async Task<ClockResponse> ClockOutCoreAsync(int userId, string? notes, DateTime zurichTime)
    {
        var openEntry = await _db.TimeEntries
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ClockOut == null);

        if (openEntry == null)
            throw new InvalidOperationException("You are not clocked in. Please clock in first.");

        // Reject zero/near-zero duration clock-outs (6.2)
        var durationMinutes = (zurichTime - openEntry.ClockIn).TotalMinutes;
        if (durationMinutes < 1)
            throw new InvalidOperationException("Shift duration must be at least 1 minute. Clock-out rejected.");

        openEntry.ClockOut = zurichTime;
        openEntry.DurationMinutes = durationMinutes;

        // Append clock-out note to any existing clock-in note
        var trimmedNote = notes?.Trim();
        if (!string.IsNullOrEmpty(trimmedNote))
        {
            if (trimmedNote.Length > 500)
                throw new InvalidOperationException("Note must be 500 characters or fewer.");
            openEntry.Notes = string.IsNullOrEmpty(openEntry.Notes)
                ? trimmedNote
                : $"{openEntry.Notes} | {trimmedNote}";
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} clocked out at {Time} (Zurich)", userId, zurichTime);
        return new ClockResponse("Clocked out successfully.", zurichTime, false, null);
    }

    public async Task<AttendanceStatusResponse> GetStatusAsync(int userId)
    {
        var openEntry = await _db.TimeEntries
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ClockOut == null);

        return new AttendanceStatusResponse(openEntry != null, openEntry?.ClockIn);
    }

    public async Task<PaginatedResponse<TimeEntryDto>> GetHistoryAsync(int userId, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new InvalidOperationException("'from' date must be before or equal to 'to' date.");

        var query = _db.TimeEntries
            .Where(t => t.UserId == userId)
            .FilterByDateRange(from, to);

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(t => t.ClockIn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TimeEntryDto(
                t.Id, t.ClockIn, t.ClockOut, t.DurationMinutes, t.Notes, t.IsManuallyEdited))
            .ToListAsync();

        return new PaginatedResponse<TimeEntryDto>(items, page, totalPages, totalCount, page < totalPages);
    }

    /// <summary>
    /// Detects SQL Server / network connectivity failures that warrant offline fallback.
    /// </summary>
    private static bool IsDbConnectionFailure(Exception ex)
    {
        // Walk the exception chain looking for SqlException or known network errors
        var current = ex;
        while (current != null)
        {
            if (current is SqlException sqlEx)
            {
                // Class 20+ = connectivity / fatal errors
                if (sqlEx.Class >= 20) return true;
                // Error 53 = server not found, -2 = timeout, 2 = instance not running
                if (sqlEx.Number is 53 or -2 or 2 or -1 or 40) return true;
            }

            if (current is TimeoutException) return true;
            if (current is InvalidOperationException ioe
                && ioe.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)) return true;

            current = current.InnerException;
        }
        return false;
    }
}
