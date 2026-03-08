using backend.DTOs;

namespace backend.Services;

public interface IAdminService
{
    Task<List<EmployeeDto>> GetEmployeesAsync();
    Task<EmployeeReportDto> GetEmployeeReportAsync(int userId, DateTime? from, DateTime? to, int page = 1, int pageSize = 50);
    Task<(byte[] Bytes, string FileName)> GetEmployeeReportCsvAsync(int userId, DateTime? from, DateTime? to);
    Task UpdateHourlyRateAsync(int userId, decimal hourlyRate);
    Task<TimeEntryDto> EditTimeEntryAsync(int entryId, EditTimeEntryRequest request);
    Task UpdateUserStatusAsync(int userId, bool isActive, int callingAdminId);
    Task<TimeEntryDto> ReopenTimeEntryAsync(int entryId);
    Task<List<AuditLogDto>> GetAuditLogsAsync(int entryId);
}
