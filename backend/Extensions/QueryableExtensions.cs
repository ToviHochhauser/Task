using backend.Models;

namespace backend.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Filters time entries that overlap the [from, to] date range.
    /// Uses overlap logic so midnight-crossing shifts are included correctly.
    /// Both boundaries are optional — omitting one produces an open-ended filter.
    /// </summary>
    public static IQueryable<TimeEntry> FilterByDateRange(
        this IQueryable<TimeEntry> query,
        DateTime? from,
        DateTime? to)
    {
        if (from.HasValue && to.HasValue)
        {
            var toEnd = to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.AddDays(1) : to.Value;
            return query.Where(t => t.ClockIn < toEnd && (t.ClockOut == null || t.ClockOut > from.Value));
        }

        if (from.HasValue)
            return query.Where(t => t.ClockOut == null || t.ClockOut > from.Value);

        if (to.HasValue)
        {
            var toEnd = to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.AddDays(1) : to.Value;
            return query.Where(t => t.ClockIn < toEnd);
        }

        return query;
    }
}
