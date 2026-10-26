namespace AbonStock.Api.Services;

public record DemoUser
{
    public string Id { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
