using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.DTOs;
using backend.Services;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IAuthService _authService;

    public AdminController(IAdminService adminService, IAuthService authService)
    {
        _adminService = adminService;
        _authService = authService;
    }

    private int GetCallingAdminId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Invalid user token.");
        return id;
    }

    [HttpGet("employees")]
    public async Task<ActionResult> GetEmployees()
    {
        var result = await _adminService.GetEmployeesAsync();
        return Ok(result);
    }

    [HttpPost("employees")]
    public async Task<ActionResult> CreateEmployee(RegisterRequest request)
    {
        var result = await _authService.CreateEmployeeAsync(request);
        return Ok(result);
    }

    [HttpGet("reports/{userId}")]
    public async Task<ActionResult> GetReport(
        int userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? format = null)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var (bytes, fileName) = await _adminService.GetEmployeeReportCsvAsync(userId, from, to);
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        var result = await _adminService.GetEmployeeReportAsync(userId, from, to, page, pageSize);
        return Ok(result);
    }

    // #3: Set hourly rate for an employee
    [HttpPut("users/{userId}/hourly-rate")]
    public async Task<ActionResult> UpdateHourlyRate(int userId, UpdateHourlyRateRequest request)
    {
        await _adminService.UpdateHourlyRateAsync(userId, request.HourlyRate);
        return Ok(new { message = "Hourly rate updated." });
    }

    [HttpPut("attendance/{entryId}")]
    public async Task<ActionResult> EditTimeEntry(int entryId, EditTimeEntryRequest request)
    {
        var result = await _adminService.EditTimeEntryAsync(entryId, request);
        return Ok(result);
    }

    // Fix #1: Toggle user active/inactive status
    [HttpPut("users/{userId}/status")]
    public async Task<ActionResult> UpdateUserStatus(int userId, UpdateUserStatusRequest request)
    {
        await _adminService.UpdateUserStatusAsync(userId, request.IsActive, GetCallingAdminId());
        return Ok(new { message = request.IsActive ? "User activated." : "User deactivated." });
    }

    // Fix #3: Reopen a closed time entry
    [HttpPost("attendance/{entryId}/reopen")]
    public async Task<ActionResult> ReopenTimeEntry(int entryId)
    {
        var result = await _adminService.ReopenTimeEntryAsync(entryId);
        return Ok(result);
    }

    // #10: Get full audit history for a time entry
    [HttpGet("attendance/{entryId}/audit")]
    public async Task<ActionResult> GetAuditLogs(int entryId)
    {
        var result = await _adminService.GetAuditLogsAsync(entryId);
        return Ok(result);
    }
}
