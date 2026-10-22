using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Inventory;

public record StockMovementResponse(
    int Id,
    int ProductId,
    string? ProductSku,
    string? ProductName,
    int? InventoryBatchId,
    string? BatchNo,
    string MovementType,
    int QuantityChange,
    int BalanceAfter,
    int? BatchBalanceAfter,
    string Reason,
    int? ReferenceOrderId,
    DateTime CreatedAtUtc)
{
    public static StockMovementResponse FromEntity(StockMovement movement) => new(
        movement.Id,
        movement.ProductId,
        movement.Product?.Sku,
        movement.Product?.Name,
        movement.InventoryBatchId,
        movement.InventoryBatch?.BatchNo,
        movement.MovementType.ToString(),
        movement.QuantityChange,
        movement.BalanceAfter,
        movement.BatchBalanceAfter,
        movement.Reason,
        movement.ReferenceOrderId,
        movement.CreatedAtUtc);
}
