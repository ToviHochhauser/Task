using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Services;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TimeController : ControllerBase
{
    private readonly ITimeService _timeService;

    public TimeController(ITimeService timeService)
    {
        _timeService = timeService;
    }

    [HttpGet("current")]
    public async Task<ActionResult> GetCurrentTime()
    {
        var zurichTime = await _timeService.GetZurichTimeAsync();
        return Ok(new { datetime = zurichTime, timezone = "Europe/Zurich" });
    }
}
