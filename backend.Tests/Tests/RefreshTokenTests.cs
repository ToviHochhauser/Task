using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

/// <summary>
/// Feature #17 — Refresh Token Auth
/// Tests token rotation, theft detection, refresh/logout, and cleanup.
/// </summary>
public class RefreshTokenTests
{
    private static AuthService CreateService(AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtTokenFactory.TestKey,
                ["Jwt:Issuer"] = "TimeClock",
                ["Jwt:Audience"] = "TimeClockUsers"
            })
            .Build();

        return new AuthService(db, config);
    }

    private static User SeedUser(
        AppDbContext db,
        string username = "alice",
        string password = "Password1",
        bool isActive = true)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = "Alice Test",
            Role = "Employee",
            IsActive = isActive
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    // ── Login returns refresh token ────────────────────────────────────────────

    [Fact]
    public async Task Login_ReturnsAccessTokenAndRefreshToken()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var result = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        result.Token.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_CreatesRefreshTokenInDatabase()
    {
        var db = DbContextFactory.Create();
        var user = SeedUser(db);
        var svc = CreateService(db);

        var result = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        db.RefreshTokens.Should().ContainSingle(r =>
            r.UserId == user.Id && r.Token == result.RefreshToken);
    }

    // ── RefreshAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokenPair()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));
        var refreshResult = await svc.RefreshAsync(loginResult.RefreshToken);

        refreshResult.Token.Should().NotBeNullOrWhiteSpace();
        refreshResult.RefreshToken.Should().NotBeNullOrWhiteSpace();
        // New token should be different from old
        refreshResult.RefreshToken.Should().NotBe(loginResult.RefreshToken);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldTokenRevoked()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));
        await svc.RefreshAsync(loginResult.RefreshToken);

        // Old token should be revoked
        var oldToken = db.RefreshTokens.Single(r => r.Token == loginResult.RefreshToken);
        oldToken.RevokedAt.Should().NotBeNull();
        oldToken.ReplacedByToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_RotatesToken_NewTokenCreatedInDb()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));
        var refreshResult = await svc.RefreshAsync(loginResult.RefreshToken);

        var newToken = db.RefreshTokens.Single(r => r.Token == refreshResult.RefreshToken);
        newToken.RevokedAt.Should().BeNull();
        newToken.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Refresh_RevokedToken_TheftDetection_RevokesAllUserTokens()
    {
        var db = DbContextFactory.Create();
        var user = SeedUser(db);
        var svc = CreateService(db);

        // Login → get token A
        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));
        var tokenA = loginResult.RefreshToken;

        // Rotate token A → token B (A is now revoked)
        var refreshResult = await svc.RefreshAsync(tokenA);
        var tokenB = refreshResult.RefreshToken;

        // Attacker reuses revoked token A → theft detection triggers
        await svc.Invoking(s => s.RefreshAsync(tokenA))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*revoked*");

        // ALL tokens for this user should now be revoked (including token B)
        var activeTokens = db.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null)
            .ToList();
        activeTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ThrowsUnauthorized()
    {
        var db = DbContextFactory.Create();
        var user = SeedUser(db);
        var svc = CreateService(db);

        // Manually create an expired token
        var expiredToken = new RefreshToken
        {
            UserId = user.Id,
            Token = "expired-token-value",
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // Already expired
        };
        db.RefreshTokens.Add(expiredToken);
        await db.SaveChangesAsync();

        await svc.Invoking(s => s.RefreshAsync("expired-token-value"))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task Refresh_InactiveUser_ThrowsUnauthorized()
    {
        var db = DbContextFactory.Create();
        var user = SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        // Deactivate user
        user.IsActive = false;
        await db.SaveChangesAsync();

        await svc.Invoking(s => s.RefreshAsync(loginResult.RefreshToken))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*deactivated*");
    }

    [Fact]
    public async Task Refresh_InvalidToken_ThrowsUnauthorized()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RefreshAsync("nonexistent-token"))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid*");
    }

    // ── RevokeRefreshTokenAsync (logout) ───────────────────────────────────────

    [Fact]
    public async Task Revoke_ValidToken_SetsRevokedAt()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));
        await svc.RevokeRefreshTokenAsync(loginResult.RefreshToken);

        var token = db.RefreshTokens.Single(r => r.Token == loginResult.RefreshToken);
        token.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_AlreadyRevokedToken_DoesNotThrow()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        // Revoke twice — should not throw
        await svc.RevokeRefreshTokenAsync(loginResult.RefreshToken);
        await svc.Invoking(s => s.RevokeRefreshTokenAsync(loginResult.RefreshToken))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Revoke_NonexistentToken_DoesNotThrow()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RevokeRefreshTokenAsync("does-not-exist"))
            .Should().NotThrowAsync();
    }

    // ── Expired token cleanup ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_CleansUpExpiredTokens()
    {
        var db = DbContextFactory.Create();
        var user = SeedUser(db);
        var svc = CreateService(db);

        // Manually add expired tokens
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = user.Id, Token = "expired-1", ExpiresAt = DateTime.UtcNow.AddDays(-10) },
            new RefreshToken { UserId = user.Id, Token = "expired-2", ExpiresAt = DateTime.UtcNow.AddDays(-5) });
        await db.SaveChangesAsync();

        // Login triggers cleanup
        await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        var expiredTokens = db.RefreshTokens
            .Where(r => r.UserId == user.Id && r.ExpiresAt < DateTime.UtcNow)
            .ToList();
        expiredTokens.Should().BeEmpty();
    }

    // ── Refresh token chain ────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ChainedRotation_EachNewTokenWorks()
    {
        var db = DbContextFactory.Create();
        SeedUser(db);
        var svc = CreateService(db);

        var loginResult = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        // Rotate 3 times in sequence
        var r1 = await svc.RefreshAsync(loginResult.RefreshToken);
        var r2 = await svc.RefreshAsync(r1.RefreshToken);
        var r3 = await svc.RefreshAsync(r2.RefreshToken);

        r3.Token.Should().NotBeNullOrWhiteSpace();
        r3.RefreshToken.Should().NotBe(r2.RefreshToken);

        // All previous tokens should be revoked
        var activeTokens = db.RefreshTokens.Where(r => r.RevokedAt == null).ToList();
        activeTokens.Should().ContainSingle(); // Only the latest
    }
}
