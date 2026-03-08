using System.ComponentModel.DataAnnotations;

namespace backend.Models;

/// <summary>
/// ClockIn/ClockOut are stored as Zurich-local (Europe/Zurich) DateTimes without timezone offset.
/// This is the system's canonical time convention for attendance timestamps.
/// All other DateTimes (e.g. User.CreatedAt, RefreshToken.ExpiresAt) use UTC.
/// </summary>
public class TimeEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public double? DurationMinutes { get; set; }
    public string? Notes { get; set; }
    public bool IsManuallyEdited { get; set; } = false;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public User User { get; set; } = null!;
}
