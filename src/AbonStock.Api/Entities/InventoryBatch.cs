namespace AbonStock.Api.Entities;

public class InventoryBatch
{
    public const string DefaultLocationId = "LOC_KDR_PROD";

    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNo { get; set; } = string.Empty;
    public DateOnly ProductionDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string LocationId { get; set; } = DefaultLocationId;

    public int InitialQuantity { get; set; }
    public int PhysicalQuantity { get; set; }
    public int ReservedQuantity { get; set; } = 0;
    public BatchStatus Status { get; set; } = BatchStatus.PendingRelease;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// PostgreSQL xmin system column mapped via EF Core IsRowVersion() for optimistic concurrency.
    /// </summary>
    public uint Version { get; set; }

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    public ICollection<OrderReservation> OrderReservations { get; set; } = new List<OrderReservation>();
}
