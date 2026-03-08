using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using backend.Data;

namespace backend.Middleware;

public class IsActiveMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public IsActiveMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // Fix #8: IMemoryCache injected per-invocation (middleware uses method injection)
    public async Task InvokeAsync(HttpContext context, AppDbContext db, IMemoryCache cache)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdClaim, out var userId))
            {
                var cacheKey = $"IsActive:{userId}";
                if (!cache.TryGetValue(cacheKey, out bool isActive))
                {
                    isActive = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive);
                    cache.Set(cacheKey, isActive, CacheTtl);
                }

                if (!isActive)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(new { error = "Account is deactivated." }));
                    return;
                }
            }
        }

        await _next(context);
    }
}
