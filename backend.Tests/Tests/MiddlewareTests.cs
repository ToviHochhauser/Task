using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using backend.Data;
using backend.Middleware;
using backend.Models;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

/// <summary>
/// Unit tests for LoginRateLimiter, LoginRateLimitMiddleware, ExceptionMiddleware,
/// and IsActiveMiddleware.
/// </summary>
public class MiddlewareTests
{
    // ── LoginRateLimiter ───────────────────────────────────────────────────────

    [Fact]
    public void LoginRateLimiter_NewKey_IsNotRateLimited()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 5);
        limiter.IsRateLimited("192.168.1.1:alice").Should().BeFalse();
    }

    [Fact]
    public void LoginRateLimiter_BelowMaxAttempts_IsNotRateLimited()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 5, window: TimeSpan.FromMinutes(5));

        for (var i = 0; i < 4; i++)
            limiter.RecordAttempt("192.168.1.1:alice");

        limiter.IsRateLimited("192.168.1.1:alice").Should().BeFalse();
    }

    [Fact]
    public void LoginRateLimiter_AtMaxAttempts_IsRateLimited()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 5, window: TimeSpan.FromMinutes(5));

        for (var i = 0; i < 5; i++)
            limiter.RecordAttempt("192.168.1.1:bob");

        limiter.IsRateLimited("192.168.1.1:bob").Should().BeTrue();
    }

    [Fact]
    public void LoginRateLimiter_DifferentKeys_AreIndependent()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 3, window: TimeSpan.FromMinutes(5));

        for (var i = 0; i < 3; i++)
            limiter.RecordAttempt("192.168.1.1:user1");

        // Different username
        limiter.IsRateLimited("192.168.1.1:user2").Should().BeFalse();
        // Different IP
        limiter.IsRateLimited("10.0.0.1:user1").Should().BeFalse();
    }

    [Fact]
    public void LoginRateLimiter_AfterWindowExpires_AllowsAgain()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 2, window: TimeSpan.FromMilliseconds(10));

        limiter.RecordAttempt("1.2.3.4:charlie");
        limiter.RecordAttempt("1.2.3.4:charlie");
        limiter.IsRateLimited("1.2.3.4:charlie").Should().BeTrue();

        // Wait for window to expire
        Thread.Sleep(50);

        // RecordAttempt resets the window when it detects expiry
        limiter.RecordAttempt("1.2.3.4:charlie");
        limiter.IsRateLimited("1.2.3.4:charlie").Should().BeFalse();
    }

    // ── LoginRateLimitMiddleware ───────────────────────────────────────────────

    private static DefaultHttpContext BuildLoginContext(string username = "testuser")
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/auth/login";
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var body = JsonSerializer.Serialize(new { username });
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentType = "application/json";
        ctx.Request.ContentLength = bytes.Length;

        return ctx;
    }

    [Fact]
    public async Task LoginRateLimitMiddleware_BelowLimit_ForwardsRequest()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 5, window: TimeSpan.FromMinutes(5));
        var nextCalled = false;
        var mw = new LoginRateLimitMiddleware(
            ctx => { ctx.Response.StatusCode = 200; nextCalled = true; return Task.CompletedTask; },
            limiter);

        var ctx = BuildLoginContext();
        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task LoginRateLimitMiddleware_AtLimit_Returns429WithMessage()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 3, window: TimeSpan.FromMinutes(5));

        // Pre-fill to hit the limit
        for (var i = 0; i < 3; i++)
            limiter.RecordAttempt("10.0.0.1:testuser");

        var nextCalled = false;
        var mw = new LoginRateLimitMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            limiter);

        var ctx = BuildLoginContext();
        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeFalse("middleware must short-circuit when rate limited");
        ctx.Response.StatusCode.Should().Be(429);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("Too many login attempts");
    }

    [Fact]
    public async Task LoginRateLimitMiddleware_FailedLogin_RecordsAttempt()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 1, window: TimeSpan.FromMinutes(5));
        var mw = new LoginRateLimitMiddleware(
            ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; },
            limiter);

        var ctx = BuildLoginContext();
        await mw.InvokeAsync(ctx);

        // One failed attempt was recorded — should now be rate limited (1 >= 1)
        limiter.IsRateLimited("10.0.0.1:testuser").Should().BeTrue();
    }

    [Fact]
    public async Task LoginRateLimitMiddleware_SuccessfulLogin_DoesNotCountAttempt()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 1, window: TimeSpan.FromMinutes(5));
        var mw = new LoginRateLimitMiddleware(
            ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            limiter);

        var ctx = BuildLoginContext();
        await mw.InvokeAsync(ctx);

        // Successful login (200) should NOT be counted
        limiter.IsRateLimited("10.0.0.1:testuser").Should().BeFalse();
    }

    [Fact]
    public async Task LoginRateLimitMiddleware_NonLoginPath_IsNotRateLimited()
    {
        using var limiter = new LoginRateLimiter(maxAttempts: 0, window: TimeSpan.FromMinutes(5));
        var nextCalled = false;
        var mw = new LoginRateLimitMiddleware(
            ctx => { nextCalled = true; return Task.CompletedTask; },
            limiter);

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/auth/refresh"; // Different path
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue("non-login paths bypass the rate limiter");
    }

    // ── ExceptionMiddleware ────────────────────────────────────────────────────

    private static async Task<(int StatusCode, string Body)> InvokeExceptionMiddleware(Exception ex)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var mw = new ExceptionMiddleware(
            _ => throw ex,
            NullLogger<ExceptionMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return (ctx.Response.StatusCode, body);
    }

    [Fact]
    public async Task ExceptionMiddleware_UnauthorizedAccessException_Returns401()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new UnauthorizedAccessException("Invalid token."));

        status.Should().Be(401);
        body.Should().Contain("Invalid token.");
    }

    [Fact]
    public async Task ExceptionMiddleware_InvalidOperationException_Returns400()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new InvalidOperationException("Validation failed."));

        status.Should().Be(400);
        body.Should().Contain("Validation failed.");
    }

    [Fact]
    public async Task ExceptionMiddleware_KeyNotFoundException_Returns404()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new KeyNotFoundException("Entity not found."));

        status.Should().Be(404);
        body.Should().Contain("Entity not found.");
    }

    [Fact]
    public async Task ExceptionMiddleware_DbUpdateConcurrencyException_Returns409()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Concurrency conflict."));

        status.Should().Be(409);
        body.Should().Contain("modified by another user");
    }

    [Fact]
    public async Task ExceptionMiddleware_DbUpdateException_Returns409()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new Microsoft.EntityFrameworkCore.DbUpdateException("DB constraint.", new Exception()));

        status.Should().Be(409);
        body.Should().Contain("database constraint");
    }

    [Fact]
    public async Task ExceptionMiddleware_UnhandledException_Returns500()
    {
        var (status, body) = await InvokeExceptionMiddleware(
            new ApplicationException("Unexpected."));

        status.Should().Be(500);
        body.Should().Contain("unexpected error");
    }

    [Fact]
    public async Task ExceptionMiddleware_NoException_PassesThrough()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Response.StatusCode = 200;

        var nextCalled = false;
        var mw = new ExceptionMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<ExceptionMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    // ── IsActiveMiddleware ─────────────────────────────────────────────────────

    private static HttpContext CreateAuthenticatedContext(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return ctx;
    }

    [Fact]
    public async Task IsActiveMiddleware_ActiveUser_PassesThrough()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 100, Username = "active_user", PasswordHash = "x", FullName = "Active", IsActive = true });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var ctx = CreateAuthenticatedContext(100);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new IsActiveMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, db, cache);

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task IsActiveMiddleware_InactiveUser_Returns401AndBlocksNext()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 200, Username = "inactive_user", PasswordHash = "x", FullName = "Inactive", IsActive = false });
        await db.SaveChangesAsync();

        var nextCalled = false;
        var ctx = CreateAuthenticatedContext(200);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new IsActiveMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, db, cache);

        nextCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("deactivated");
    }

    [Fact]
    public async Task IsActiveMiddleware_UnauthenticatedRequest_PassesThrough()
    {
        var db = DbContextFactory.Create();
        var nextCalled = false;
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        // No authentication — User.Identity.IsAuthenticated == false
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new IsActiveMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, db, cache);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task IsActiveMiddleware_CachesActiveStatus_AvoidsDuplicateDbHits()
    {
        var db = DbContextFactory.Create();
        db.Users.Add(new User { Id = 300, Username = "cached_user", PasswordHash = "x", FullName = "Cached", IsActive = true });
        await db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var mw = new IsActiveMiddleware(_ => Task.CompletedTask);

        // First call — populates cache from DB
        await mw.InvokeAsync(CreateAuthenticatedContext(300), db, cache);

        // Deactivate user directly in DB without clearing cache
        var user = await db.Users.FindAsync(300);
        user!.IsActive = false;
        await db.SaveChangesAsync();

        // Second call — should still pass because cache still says active
        var nextCalled = false;
        var mw2 = new IsActiveMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await mw2.InvokeAsync(CreateAuthenticatedContext(300), db, cache);

        nextCalled.Should().BeTrue("cached value (active=true) should be used");
    }
}
