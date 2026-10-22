using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Orders;

public record CancelOrderRequest
{
    [Required(ErrorMessage = "CancellationReason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "CancellationReason must be between 1 and 500 characters.")]
    public string CancellationReason { get; init; } = string.Empty;

    public uint? RowVersion { get; init; }
}
