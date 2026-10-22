using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Orders;

public record CreateOrderRequest
{
    [Required(ErrorMessage = "CustomerName is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "CustomerName must be between 1 and 200 characters.")]
    public string CustomerName { get; init; } = string.Empty;

    [EmailAddress(ErrorMessage = "CustomerEmail must be a valid email address.")]
    [StringLength(200, ErrorMessage = "CustomerEmail cannot exceed 200 characters.")]
    public string? CustomerEmail { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    [MinLength(1, ErrorMessage = "Order must contain at least one item.")]
    public List<CreateOrderItemRequest> Items { get; init; } = new();
}
