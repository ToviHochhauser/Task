using backend.DTOs;

namespace backend.Services;

public interface IAttendanceService
{
    Task<ClockResponse> ClockInAsync(int userId, string? notes = null);
    Task<ClockResponse> ClockOutAsync(int userId, string? notes = null);
    Task<AttendanceStatusResponse> GetStatusAsync(int userId);
    Task<PaginatedResponse<TimeEntryDto>> GetHistoryAsync(int userId, DateTime? from, DateTime? to, int page = 1, int pageSize = 50);
}
