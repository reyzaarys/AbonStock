namespace AbonStock.Api.Dtos.Auth;

public record UserProfileResponse(
    string Id,
    string Username,
    string DisplayName,
    string Role
);
