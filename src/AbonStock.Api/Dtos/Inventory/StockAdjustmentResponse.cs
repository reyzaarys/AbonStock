namespace AbonStock.Api.Dtos.Inventory;

public record StockAdjustmentResponse(
    int BatchId,
    int ProductId,
    int PreviousPhysicalQuantity,
    int NewPhysicalQuantity,
    int PreviousProductQuantityOnHand,
    int NewProductQuantityOnHand,
    string Status,
    uint BatchVersion,
    uint ProductVersion,
    DateTime TimestampUtc);
