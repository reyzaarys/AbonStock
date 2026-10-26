using System.Security.Claims;
using AbonStock.Api.Dtos.Auth;

namespace AbonStock.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    UserProfileResponse? GetCurrentUserProfile(ClaimsPrincipal principal);
}
