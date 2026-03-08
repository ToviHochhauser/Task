using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace backend.Tests.Helpers;

/// <summary>
/// Mints real, signed JWTs for use in integration tests.
/// The key, issuer, and audience must match appsettings.Test.json.
/// </summary>
public static class JwtTokenFactory
{
    // Must stay in sync with appsettings.Test.json → Jwt:Key
    internal const string TestKey = "TestSecretKeyThatIsAtLeast32CharsLongForTests!!";
    private const string Issuer = "TimeClock";
    private const string Audience = "TimeClockUsers";

    /// <summary>
    /// Generates a signed JWT for the given user identity.
    /// </summary>
    public static string GenerateToken(int userId, string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Generates an Admin-role JWT. Defaults to the seeded test admin (ID 1).</summary>
    public static string GenerateAdminToken(int userId = 1, string username = "testadmin") =>
        GenerateToken(userId, username, "Admin");

    /// <summary>Generates an Employee-role JWT.</summary>
    public static string GenerateEmployeeToken(int userId = 2, string username = "testemployee") =>
        GenerateToken(userId, username, "Employee");
}
