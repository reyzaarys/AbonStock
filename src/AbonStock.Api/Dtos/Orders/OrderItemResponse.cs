using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Orders;

public record OrderItemResponse(
    int Id,
    int ProductId,
    string ProductSku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal)
{
    public static OrderItemResponse FromEntity(OrderItem item) => new(
        item.Id,
        item.ProductId,
        item.ProductSku,
        item.ProductName,
        item.Quantity,
        item.UnitPrice,
        item.LineTotal);
}
