namespace AbonStock.Api.Dtos.Inventory;

public record WriteOffBatchResponse(
    int BatchId,
    int ProductId,
    string BatchNo,
    int WrittenOffQuantity,
    int PreviousPhysicalQuantity,
    int NewPhysicalQuantity,
    int PreviousProductQuantityOnHand,
    int NewProductQuantityOnHand,
    string Status,
    int MovementId,
    uint BatchVersion,
    uint ProductVersion,
    DateTime TimestampUtc);
