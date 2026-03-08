using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.DTOs;
using backend.Models;

namespace backend.Services;

public partial class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private static readonly string[] ValidRoles = ["Employee", "Admin"];

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$")]
    private static partial Regex UsernameRegex();

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid username or password.");

        var token = GenerateAccessToken(user);
        var refreshToken = await CreateRefreshTokenAsync(user.Id);
        return new AuthResponse(token, refreshToken, user.Username, user.FullName, user.Role);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var user = await CreateUserAsync(request);
        var token = GenerateAccessToken(user);
        var refreshToken = await CreateRefreshTokenAsync(user.Id);
        return new AuthResponse(token, refreshToken, user.Username, user.FullName, user.Role);
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(RegisterRequest request)
    {
        var user = await CreateUserAsync(request);
        return new EmployeeDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, user.HourlyRate);
    }

    private async Task<User> CreateUserAsync(RegisterRequest request)
    {
        var username = request.Username?.Trim() ?? "";
        if (username.Length < 3 || username.Length > 50)
            throw new InvalidOperationException("Username must be between 3 and 50 characters.");
        if (!UsernameRegex().IsMatch(username))
            throw new InvalidOperationException("Username can only contain letters, digits, and underscores.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException("Password must be at least 8 characters.");
        if (!request.Password.Any(char.IsUpper) || !request.Password.Any(char.IsDigit))
            throw new InvalidOperationException("Password must contain at least one uppercase letter and one digit.");
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 200)
            throw new InvalidOperationException("Full name is required and must be under 200 characters.");
        if (!ValidRoles.Contains(request.Role))
            throw new InvalidOperationException("Role must be 'Employee' or 'Admin'.");

        if (await _db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Username already exists.");

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            Role = request.Role
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // #17: Exchange a valid refresh token for a new access+refresh token pair (rotation)
    public async Task<RefreshResponse> RefreshAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (storedToken == null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        // Token theft detection: if a revoked token is reused, revoke ALL tokens for this user
        if (storedToken.IsRevoked)
        {
            await RevokeAllUserTokensAsync(storedToken.UserId);
            throw new UnauthorizedAccessException("Refresh token has been revoked. All sessions invalidated.");
        }

        if (storedToken.IsExpired)
            throw new UnauthorizedAccessException("Refresh token has expired. Please log in again.");

        if (!storedToken.User.IsActive)
            throw new UnauthorizedAccessException("Account is deactivated.");

        // Rotate: revoke old, issue new
        var newRefreshTokenValue = GenerateRefreshTokenValue();
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.ReplacedByToken = newRefreshTokenValue;

        var newRefreshToken = new RefreshToken
        {
            UserId = storedToken.UserId,
            Token = newRefreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        _db.RefreshTokens.Add(newRefreshToken);
        await _db.SaveChangesAsync();

        var newAccessToken = GenerateAccessToken(storedToken.User);
        return new RefreshResponse(newAccessToken, newRefreshTokenValue);
    }

    // #17: Revoke a specific refresh token (logout)
    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (storedToken != null && storedToken.RevokedAt == null)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    private async Task<string> CreateRefreshTokenAsync(int userId)
    {
        // Clean up expired tokens for this user on each new login (keep DB tidy)
        var expiredTokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
        if (expiredTokens.Count > 0)
            _db.RefreshTokens.RemoveRange(expiredTokens);

        var tokenValue = GenerateRefreshTokenValue();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            Token = tokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();
        return tokenValue;
    }

    private async Task RevokeAllUserTokensAsync(int userId)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync();
        foreach (var t in activeTokens)
            t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static string GenerateRefreshTokenValue()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
