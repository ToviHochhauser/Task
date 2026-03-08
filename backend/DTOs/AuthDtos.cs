namespace backend.DTOs;

public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string FullName, string Role = "Employee");
public record AuthResponse(string Token, string RefreshToken, string Username, string FullName, string Role);

// #17: Refresh token endpoints
public record RefreshRequest(string RefreshToken);
public record RefreshResponse(string Token, string RefreshToken);
