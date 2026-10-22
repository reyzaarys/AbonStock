namespace AbonStock.Api.Entities;

public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public StockMovementType MovementType { get; set; }
    public int QuantityChange { get; set; }
    public int BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int? ReferenceOrderId { get; set; }
    public Order? ReferenceOrder { get; set; }

    public int? InventoryBatchId { get; set; }
    public InventoryBatch? InventoryBatch { get; set; }

    public int? BatchBalanceAfter { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
