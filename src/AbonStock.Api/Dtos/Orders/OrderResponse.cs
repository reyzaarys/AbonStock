using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Orders;

public record OrderResponse(
    int Id,
    string OrderNumber,
    string CustomerName,
    string? CustomerEmail,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    DateTime? CompletedAtUtc,
    uint Version,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse FromEntity(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.CustomerName,
        order.CustomerEmail,
        order.Status.ToString(),
        order.TotalAmount,
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        order.ConfirmedAtUtc,
        order.CancelledAtUtc,
        order.CancellationReason,
        order.CompletedAtUtc,
        order.Version,
        order.Items.Select(OrderItemResponse.FromEntity).ToList());
}
