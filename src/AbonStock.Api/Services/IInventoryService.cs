using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Inventory;

namespace AbonStock.Api.Services;

public interface IInventoryService
{
    Task<StockReceiptResponse> ReceiveStockAsync(StockReceiptRequest request, CancellationToken ct = default);
    Task<BatchResponse> ReleaseBatchAsync(int batchId, ReleaseBatchRequest request, CancellationToken ct = default);
    Task<BatchResponse> QuarantineBatchAsync(int batchId, QuarantineBatchRequest request, CancellationToken ct = default);
    Task<WriteOffBatchResponse> WriteOffBatchAsync(int batchId, WriteOffBatchRequest request, string? actor = null, CancellationToken ct = default);
    Task<StockAdjustmentResponse> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct = default);
    Task<BatchResponse> GetBatchByIdAsync(int batchId, CancellationToken ct = default);
    Task<PagedResult<BatchResponse>> GetBatchesAsync(BatchListQuery query, CancellationToken ct = default);
    Task<PagedResult<StockMovementResponse>> GetMovementsAsync(StockMovementListQuery query, CancellationToken ct = default);
    Task<InventoryAuditResponse> AuditInventoryAsync(int? productId = null, CancellationToken ct = default);
}
