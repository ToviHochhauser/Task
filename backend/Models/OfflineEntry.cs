namespace backend.Models;

public class OfflineEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Action { get; set; } = string.Empty; // "ClockIn" or "ClockOut"
    public int UserId { get; set; }
    public DateTime Timestamp { get; set; }   // Zurich time when the action occurred
    public string? Notes { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
}
