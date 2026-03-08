namespace backend.Models;

public class TimeEntryAuditLog
{
    public int Id { get; set; }
    public int TimeEntryId { get; set; }
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }

    // Max 50 chars — one of: ClockIn, ClockOut, Notes, DurationMinutes
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public TimeEntry TimeEntry { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}
