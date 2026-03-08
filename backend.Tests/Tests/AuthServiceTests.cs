using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Services;
using backend.Tests.Helpers;

namespace backend.Tests.Tests;

public class AuthServiceTests
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
        string role = "Employee",
        bool isActive = true)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = "Alice Test",
            Role = role,
            IsActive = isActive
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    // ── LoginAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndUserInfo()
    {
        var db = DbContextFactory.Create();
        SeedUser(db, "alice", "Password1", "Employee");

        var svc = CreateService(db);
        var result = await svc.LoginAsync(new LoginRequest("alice", "Password1"));

        result.Username.Should().Be("alice");
        result.Role.Should().Be("Employee");
        result.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        var db = DbContextFactory.Create();
        SeedUser(db, "bob", "Password1");

        var svc = CreateService(db);

        await svc.Invoking(s => s.LoginAsync(new LoginRequest("bob", "WrongPass99")))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Invalid username or password*");
    }

    [Fact]
    public async Task Login_InactiveUser_ThrowsUnauthorized()
    {
        var db = DbContextFactory.Create();
        SeedUser(db, "charlie", "Password1", isActive: false);

        var svc = CreateService(db);

        await svc.Invoking(s => s.LoginAsync(new LoginRequest("charlie", "Password1")))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidRequest_CreatesUserAndReturnsToken()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        var result = await svc.RegisterAsync(
            new RegisterRequest("newuser", "SecurePass1", "New User", "Employee"));

        result.Username.Should().Be("newuser");
        result.FullName.Should().Be("New User");
        result.Token.Should().NotBeNullOrWhiteSpace();
        db.Users.Should().ContainSingle(u => u.Username == "newuser");
    }

    [Fact]
    public async Task Register_UsernameTooShort_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("ab", "SecurePass1", "A B", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*3 and 50*");
    }

    [Fact]
    public async Task Register_PasswordTooWeak_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        // Missing uppercase
        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("validuser", "nouppercase1", "Valid User", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*uppercase*");
    }

    [Fact]
    public async Task Register_InvalidUsernameCharacters_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("user name!", "SecurePass1", "User", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*letters, digits, and underscores*");
    }

    [Fact]
    public async Task Register_InvalidRole_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("gooduser", "SecurePass1", "Good User", "Superuser")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Employee*Admin*");
    }

    [Fact]
    public async Task Register_DuplicateUsername_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        SeedUser(db, "duplicate");
        var svc = CreateService(db);

        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("duplicate", "SecurePass1", "Dup User", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Register_PasswordMissingDigit_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        // Password has uppercase but no digit
        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("newuser5", "NoDigitsHere", "New User", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*digit*");
    }

    [Fact]
    public async Task Register_EmptyFullName_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("newuser6", "SecurePass1", "   ", "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Full name is required*");
    }

    [Fact]
    public async Task Register_FullNameTooLong_ThrowsInvalidOperation()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        var longName = new string('A', 201);
        await svc.Invoking(s => s.RegisterAsync(
                new RegisterRequest("newuser7", "SecurePass1", longName, "Employee")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Full name is required and must be under 200 characters*");
    }

    [Fact]
    public async Task CreateEmployee_ReturnsEmployeeDto()
    {
        var db = DbContextFactory.Create();
        var svc = CreateService(db);

        var result = await svc.CreateEmployeeAsync(
            new RegisterRequest("newemployee", "SecurePass1", "New Employee", "Employee"));

        result.Username.Should().Be("newemployee");
        result.FullName.Should().Be("New Employee");
        result.Role.Should().Be("Employee");
        result.IsActive.Should().BeTrue();
        db.Users.Should().ContainSingle(u => u.Username == "newemployee");
    }
}
