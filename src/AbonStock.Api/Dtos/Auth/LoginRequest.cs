using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Auth;

public record LoginRequest
{
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = string.Empty;
}
