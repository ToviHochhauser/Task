using backend.DTOs;

namespace backend.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<EmployeeDto> CreateEmployeeAsync(RegisterRequest request);
    Task<RefreshResponse> RefreshAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
}
