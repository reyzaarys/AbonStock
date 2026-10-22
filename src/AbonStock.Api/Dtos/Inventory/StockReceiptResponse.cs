namespace AbonStock.Api.Dtos.Inventory;

public record StockReceiptResponse(
    int BatchId,
    int ProductId,
    string BatchNo,
    DateOnly ProductionDate,
    DateOnly? ExpiryDate,
    string LocationId,
    int InitialQuantity,
    int PhysicalQuantity,
    string Status,
    int TotalProductQuantityOnHand,
    uint BatchVersion,
    uint ProductVersion,
    DateTime CreatedAtUtc);
