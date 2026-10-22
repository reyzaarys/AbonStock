using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Orders;

public record CreateOrderItemRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "ProductId must be greater than 0.")]
    public int ProductId { get; init; }

    [Required]
    [Range(1, 100_000, ErrorMessage = "Quantity must be between 1 and 100,000.")]
    public int Quantity { get; init; }
}
