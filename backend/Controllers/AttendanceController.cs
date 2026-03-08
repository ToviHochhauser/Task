using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.DTOs;
using backend.Services;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;
    private readonly OfflineQueueService _offlineQueue;

    public AttendanceController(IAttendanceService attendanceService, OfflineQueueService offlineQueue)
    {
        _attendanceService = attendanceService;
        _offlineQueue = offlineQueue;
    }

    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Invalid user token.");
        return userId;
    }

    [HttpPost("clock-in")]
    public async Task<ActionResult> ClockIn([FromBody] ClockRequest? request)
    {
        var result = await _attendanceService.ClockInAsync(GetUserId(), request?.Notes);
        return Ok(result);
    }

    [HttpPost("clock-out")]
    public async Task<ActionResult> ClockOut([FromBody] ClockRequest? request)
    {
        var result = await _attendanceService.ClockOutAsync(GetUserId(), request?.Notes);
        return Ok(result);
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetStatus()
    {
        var result = await _attendanceService.GetStatusAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<ActionResult> GetHistory(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _attendanceService.GetHistoryAsync(GetUserId(), from, to, page, pageSize);
        return Ok(result);
    }

    [HttpGet("offline-queue-status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetOfflineQueueStatus()
    {
        var count = await _offlineQueue.GetPendingCountAsync();
        var pending = await _offlineQueue.GetPendingAsync();
        return Ok(new { pendingCount = count, entries = pending });
    }
}
