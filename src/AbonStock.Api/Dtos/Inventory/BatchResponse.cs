using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Inventory;

public record BatchResponse(
    int Id,
    int ProductId,
    string BatchNo,
    DateOnly ProductionDate,
    DateOnly? ExpiryDate,
    string LocationId,
    int InitialQuantity,
    int PhysicalQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    uint Version)
{
    public static BatchResponse FromEntity(InventoryBatch batch) => new(
        batch.Id,
        batch.ProductId,
        batch.BatchNo,
        batch.ProductionDate,
        batch.ExpiryDate,
        batch.LocationId,
        batch.InitialQuantity,
        batch.PhysicalQuantity,
        batch.Status.ToString(),
        batch.CreatedAtUtc,
        batch.UpdatedAtUtc,
        batch.Version);
}
