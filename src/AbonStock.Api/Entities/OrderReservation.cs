namespace AbonStock.Api.Entities;

public class OrderReservation
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int InventoryBatchId { get; set; }
    public InventoryBatch InventoryBatch { get; set; } = null!;

    public int Quantity { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAtUtc { get; set; }
}
