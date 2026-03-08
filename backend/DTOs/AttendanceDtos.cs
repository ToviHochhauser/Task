namespace backend.DTOs;

// Optional note from employee on clock-in or clock-out
public record ClockRequest(string? Notes);

// Includes status so frontend can update without a separate fetch (8.1)
public record ClockResponse(string Message, DateTime Timestamp, bool IsClockedIn, DateTime? LastClockIn);

public record AttendanceStatusResponse(bool IsClockedIn, DateTime? LastClockIn);

public record TimeEntryDto(
    int Id,
    DateTime ClockIn,
    DateTime? ClockOut,
    double? DurationMinutes,
    string? Notes,
    bool IsManuallyEdited
);

public record EditTimeEntryRequest(DateTime? ClockIn, DateTime? ClockOut, string? Notes);

public record EmployeeDto(int Id, string Username, string FullName, string Role, bool IsActive, DateTime CreatedAt, decimal? HourlyRate);

public record EmployeeReportDto(
    int UserId,
    string FullName,
    List<TimeEntryDto> Entries,
    double TotalHours,
    int CurrentPage,
    int TotalPages,
    int TotalCount,
    bool HasNextPage,
    decimal? HourlyRate,
    decimal? EstimatedPay
);

// Fix #1: Status toggle request
public record UpdateUserStatusRequest(bool IsActive);

// #3: Set hourly rate for an employee
public record UpdateHourlyRateRequest(decimal HourlyRate);

// #10: Audit log entry
public record AuditLogDto(int Id, string ChangedByUserName, DateTime ChangedAt, string FieldName, string? OldValue, string? NewValue);

// Fix #7: Generic paginated response wrapper
public record PaginatedResponse<T>(
    List<T> Items,
    int CurrentPage,
    int TotalPages,
    int TotalCount,
    bool HasNextPage
);
