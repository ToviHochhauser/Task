using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace backend.Middleware;

public class LoginRateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<string, (int count, DateTime windowStart)> _attempts = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly Timer _cleanupTimer;

    public LoginRateLimiter(int maxAttempts = 10, TimeSpan? window = null)
    {
        _maxAttempts = maxAttempts;
        _window = window ?? TimeSpan.FromMinutes(5);
        // Periodic cleanup every 60 seconds instead of on every request
        _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsRateLimited(string key)
    {
        if (_attempts.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow - entry.windowStart > _window)
            {
                _attempts[key] = (1, DateTime.UtcNow);
                return false;
            }
            return entry.count >= _maxAttempts;
        }
        return false;
    }

    public void RecordAttempt(string key)
    {
        _attempts.AddOrUpdate(key,
            _ => (1, DateTime.UtcNow),
            (_, existing) =>
            {
                if (DateTime.UtcNow - existing.windowStart > _window)
                    return (1, DateTime.UtcNow);
                return (existing.count + 1, existing.windowStart);
            });
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _window;
        foreach (var kvp in _attempts)
        {
            if (kvp.Value.windowStart < cutoff)
                _attempts.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

public class LoginRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LoginRateLimiter _limiter;

    public LoginRateLimitMiddleware(RequestDelegate next, LoginRateLimiter limiter)
    {
        _next = next;
        _limiter = limiter;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            && context.Request.Method == "POST")
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Read username from request body to build a composite rate-limit key.
            // EnableBuffering allows the body to be re-read by model binding downstream.
            context.Request.EnableBuffering();
            string? username = null;
            try
            {
                using var doc = await JsonDocument.ParseAsync(context.Request.Body);
                if (doc.RootElement.TryGetProperty("username", out var usernameProp) ||
                    doc.RootElement.TryGetProperty("Username", out usernameProp))
                {
                    username = usernameProp.GetString();
                }
            }
            catch
            {
                // Malformed JSON — fall through; the controller will reject it
            }
            finally
            {
                context.Request.Body.Position = 0;
            }

            var rateLimitKey = string.IsNullOrEmpty(username)
                ? ip
                : $"{ip}:{username}";

            if (_limiter.IsRateLimited(rateLimitKey))
            {
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(new { error = "Too many login attempts. Please try again in a few minutes." }));
                return;
            }

            await _next(context);

            // Only record the attempt if login failed (non-2xx response)
            if (context.Response.StatusCode is < 200 or >= 300)
            {
                _limiter.RecordAttempt(rateLimitKey);
            }

            return;
        }

        await _next(context);
    }
}
