using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using backend.Data;
using backend.DTOs;
using backend.Extensions;

namespace backend.Services;

public class AdminService : IAdminService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminService> _logger;
    private readonly ITimeService _timeService;
    private readonly IMemoryCache _cache;
    public AdminService(AppDbContext db, ILogger<AdminService> logger, ITimeService timeService, IMemoryCache cache)
    {
        _db = db;
        _logger = logger;
        _timeService = timeService;
        _cache = cache;
    }

    public async Task<List<EmployeeDto>> GetEmployeesAsync()
    {
        return await _db.Users
            .Select(u => new EmployeeDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.HourlyRate))
            .ToListAsync();
    }

    public async Task<EmployeeReportDto> GetEmployeeReportAsync(int userId, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new InvalidOperationException("'from' date must be before or equal to 'to' date.");

        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("Employee not found.");

        var query = _db.TimeEntries
            .Where(t => t.UserId == userId)
            .FilterByDateRange(from, to);

        // Total hours across all matching entries (not just current page)
        // Clamp negative durations to 0 (9.1)
        var totalHours = await query
            .Where(t => t.DurationMinutes.HasValue && t.DurationMinutes.Value > 0)
            .SumAsync(t => t.DurationMinutes!.Value) / 60.0;

        // #3: Estimated pay calculation
        decimal? estimatedPay = user.HourlyRate.HasValue
            ? (decimal)totalHours * user.HourlyRate.Value
            : null;

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var entries = await query
            .OrderByDescending(t => t.ClockIn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TimeEntryDto(
                t.Id, t.ClockIn, t.ClockOut, t.DurationMinutes, t.Notes, t.IsManuallyEdited))
            .ToListAsync();

        return new EmployeeReportDto(userId, user.FullName, entries, totalHours,
            page, totalPages, totalCount, page < totalPages, user.HourlyRate, estimatedPay);
    }

    // #3: Export all entries as CSV (no pagination — full dataset)
    public async Task<(byte[] Bytes, string FileName)> GetEmployeeReportCsvAsync(int userId, DateTime? from, DateTime? to)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("Employee not found.");

        var query = _db.TimeEntries
            .Where(t => t.UserId == userId)
            .FilterByDateRange(from, to);

        var entries = await query
            .OrderBy(t => t.ClockIn)
            .ToListAsync();

        var totalHours = entries
            .Where(t => t.DurationMinutes.HasValue && t.DurationMinutes.Value > 0)
            .Sum(t => t.DurationMinutes!.Value) / 60.0;

        decimal? estimatedPay = user.HourlyRate.HasValue
            ? (decimal)totalHours * user.HourlyRate.Value
            : null;

        var sb = new StringBuilder();
        // UTF-8 BOM so Excel opens it correctly without import wizard
        sb.Append('\uFEFF');
        sb.AppendLine("Date,Clock In,Clock Out,Duration (h),Notes,Edited");

        foreach (var e in entries)
        {
            var date = e.ClockIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var clockIn = e.ClockIn.ToString("HH:mm", CultureInfo.InvariantCulture);
            var clockOut = e.ClockOut?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            var duration = e.DurationMinutes.HasValue
                ? (e.DurationMinutes.Value / 60.0).ToString("F2", CultureInfo.InvariantCulture)
                : "";
            var notes = CsvEscape(e.Notes ?? "");
            var edited = e.IsManuallyEdited ? "Yes" : "No";
            sb.AppendLine($"{date},{clockIn},{clockOut},{duration},{notes},{edited}");
        }

        // Summary rows
        sb.AppendLine();
        sb.AppendLine($"Total Hours,{totalHours:F2}");
        if (user.HourlyRate.HasValue)
            sb.AppendLine($"Hourly Rate (CHF),{user.HourlyRate.Value:F2}");
        if (estimatedPay.HasValue)
            sb.AppendLine($"Estimated Pay (CHF),{estimatedPay.Value:F2}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var safeName = user.FullName.Replace(" ", "_").Replace("/", "-");
        var dateSuffix = from.HasValue || to.HasValue
            ? $"_{from?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start"}-{to?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "now"}"
            : $"_{DateTime.UtcNow:yyyyMMdd}";
        var fileName = $"TimeClock_{safeName}{dateSuffix}.csv";
        return (bytes, fileName);
    }

    // #3: Update hourly rate
    public async Task UpdateHourlyRateAsync(int userId, decimal hourlyRate)
    {
        if (hourlyRate < 0)
            throw new InvalidOperationException("Hourly rate cannot be negative.");

        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("Employee not found.");

        user.HourlyRate = hourlyRate;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Hourly rate for user {UserId} set to {Rate}", userId, hourlyRate);
    }

    public async Task<TimeEntryDto> EditTimeEntryAsync(int entryId, EditTimeEntryRequest request)
    {
        // Wrap in transaction for atomic multi-field edits (7.3)
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var entry = await _db.TimeEntries.FindAsync(entryId)
            ?? throw new KeyNotFoundException("Time entry not found.");

        // Fix #2: Reject future timestamps — inputs are Zurich-local, compare against Zurich now
        var zurichNow = await _timeService.GetZurichTimeAsync();
        if (request.ClockIn.HasValue && request.ClockIn.Value > zurichNow)
            throw new InvalidOperationException("Clock-in time cannot be in the future.");
        if (request.ClockOut.HasValue && request.ClockOut.Value > zurichNow)
            throw new InvalidOperationException("Clock-out time cannot be in the future.");

        // Store as Zurich local time (system operates in Europe/Zurich timezone)
        if (request.ClockIn.HasValue) entry.ClockIn = request.ClockIn.Value;
        if (request.ClockOut.HasValue) entry.ClockOut = request.ClockOut.Value;

        if (request.Notes != null)
        {
            if (request.Notes.Length > 500)
                throw new InvalidOperationException("Notes must be 500 characters or fewer.");
            entry.Notes = request.Notes;
        }

        if (entry.ClockOut.HasValue)
        {
            if (entry.ClockOut.Value <= entry.ClockIn)
                throw new InvalidOperationException("Clock out time must be after clock in time.");

            // Fix #4: Reject shifts longer than 24 hours
            var durationHours = (entry.ClockOut.Value - entry.ClockIn).TotalHours;
            if (durationHours > 24)
                throw new InvalidOperationException("Shift duration cannot exceed 24 hours.");

            entry.DurationMinutes = (entry.ClockOut.Value - entry.ClockIn).TotalMinutes;
        }

        entry.IsManuallyEdited = true;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        _logger.LogInformation("Admin edited time entry {EntryId}", entryId);

        return new TimeEntryDto(
            entry.Id, entry.ClockIn, entry.ClockOut, entry.DurationMinutes, entry.Notes, entry.IsManuallyEdited);
    }

    // Fix #1: Activate/Deactivate a user with self-deactivation guard
    public async Task UpdateUserStatusAsync(int userId, bool isActive, int callingAdminId)
    {
        if (userId == callingAdminId)
            throw new InvalidOperationException("You cannot deactivate your own account.");

        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.IsActive = isActive;
        await _db.SaveChangesAsync();

        // Invalidate IsActiveMiddleware cache so the change takes effect immediately (Fix #8)
        _cache.Remove($"IsActive:{userId}");

        _logger.LogInformation("Admin {AdminId} set user {UserId} IsActive={IsActive}",
            callingAdminId, userId, isActive);
    }

    // Fix #3: Reopen a closed time entry (clears ClockOut so the employee can clock out again)
    public async Task<TimeEntryDto> ReopenTimeEntryAsync(int entryId)
    {
        var entry = await _db.TimeEntries.FindAsync(entryId)
            ?? throw new KeyNotFoundException("Time entry not found.");

        if (!entry.ClockOut.HasValue)
            throw new InvalidOperationException("Entry is already open.");

        // Unique index (IX_TimeEntries_OpenEntry) prevents two open entries per user
        var hasOpenEntry = await _db.TimeEntries
            .AnyAsync(t => t.UserId == entry.UserId && t.ClockOut == null);
        if (hasOpenEntry)
            throw new InvalidOperationException("Cannot reopen: user already has an open entry.");

        entry.ClockOut = null;
        entry.DurationMinutes = null;
        entry.IsManuallyEdited = true;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin reopened time entry {EntryId} for user {UserId}", entryId, entry.UserId);

        return new TimeEntryDto(
            entry.Id, entry.ClockIn, entry.ClockOut, entry.DurationMinutes, entry.Notes, entry.IsManuallyEdited);
    }

    // #10: Return audit log for a time entry
    public async Task<List<AuditLogDto>> GetAuditLogsAsync(int entryId)
    {
        var entryExists = await _db.TimeEntries.AnyAsync(t => t.Id == entryId);
        if (!entryExists)
            throw new KeyNotFoundException("Time entry not found.");

        return await _db.TimeEntryAuditLogs
            .Where(a => a.TimeEntryId == entryId)
            .OrderBy(a => a.ChangedAt)
            .Select(a => new AuditLogDto(
                a.Id,
                a.ChangedByUser.FullName,
                a.ChangedAt,
                a.FieldName,
                a.OldValue,
                a.NewValue))
            .ToListAsync();
    }

    // RFC 4180: wrap fields containing commas, newlines, or quotes in double quotes; escape inner quotes as ""
    // Also prefix formula-injection characters so Excel doesn't interpret them
    private static string CsvEscape(string value)
    {
        if (value.Length > 0 && "=+-@|!".Contains(value[0]))
            value = "\t" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
