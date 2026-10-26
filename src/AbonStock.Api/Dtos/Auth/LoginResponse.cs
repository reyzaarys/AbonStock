namespace AbonStock.Api.Dtos.Auth;

public record LoginResponse(
    string Token,
    int ExpiresIn,
    UserProfileResponse User
);
