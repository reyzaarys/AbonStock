namespace AbonStock.Api.Dtos.Inventory;

public record ProductDiscrepancyResponse(
    int ProductId,
    string Sku,
    string ProductName,
    int ExpectedQuantityOnHand,
    int ActualQuantityOnHand,
    int Discrepancy,
    int BatchCount,
    bool IsConsistent);

public record BatchInvariantViolationResponse(
    int BatchId,
    int ProductId,
    string? ProductSku,
    string BatchNo,
    string Status,
    int PhysicalQuantity,
    int ReservedQuantity,
    string ViolationType,
    string Description);

public record LedgerAuditResponse(
    string Status,
    int TotalMovementsChecked,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Limitations,
    string Summary);

public record InventoryAuditResponse(
    DateTime AuditTimestampUtc,
    int TotalProductsChecked,
    int ConsistentProductsCount,
    int InconsistentProductsCount,
    IReadOnlyList<ProductDiscrepancyResponse> Discrepancies,
    IReadOnlyList<BatchInvariantViolationResponse> InvariantViolations,
    LedgerAuditResponse LedgerAudit,
    bool IsSystemConsistent);
