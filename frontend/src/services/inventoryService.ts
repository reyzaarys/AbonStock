import { apiClient } from '../lib/apiClient.ts';
import type {
  BatchListQuery,
  BatchResponse,
  InventoryAuditResponse,
  PagedResult,
  QuarantineBatchRequest,
  ReleaseBatchRequest,
  StockAdjustmentRequest,
  StockAdjustmentResponse,
  StockMovementListQuery,
  StockMovementResponse,
  StockReceiptRequest,
  StockReceiptResponse,
  WriteOffBatchRequest,
  WriteOffBatchResponse,
} from '../types';

export const inventoryService = {
  /**
   * Retrieves paginated inventory batches.
   * GET /api/inventory/batches
   */
  getBatches: (
    query?: BatchListQuery,
    signal?: AbortSignal
  ): Promise<PagedResult<BatchResponse>> =>
    apiClient.get<PagedResult<BatchResponse>>('/api/inventory/batches', {
      params: query as Record<string, unknown>,
      signal,
    }),

  /**
   * Retrieves an inventory batch by its ID.
   * GET /api/inventory/batches/{id}
   */
  getBatchById: (id: number, signal?: AbortSignal): Promise<BatchResponse> =>
    apiClient.get<BatchResponse>(`/api/inventory/batches/${id}`, { signal }),

  /**
   * Receives incoming stock and initializes a new batch.
   * POST /api/inventory/receipts
   */
  receiveStock: (
    data: StockReceiptRequest,
    signal?: AbortSignal
  ): Promise<StockReceiptResponse> =>
    apiClient.post<StockReceiptResponse>('/api/inventory/receipts', data, { signal }),

  /**
   * Releases a pending-release batch after quality inspection.
   * POST /api/inventory/batches/{id}/release
   */
  releaseBatch: (
    id: number,
    data: ReleaseBatchRequest,
    signal?: AbortSignal
  ): Promise<BatchResponse> =>
    apiClient.post<BatchResponse>(`/api/inventory/batches/${id}/release`, data, { signal }),

  /**
   * Quarantines a batch failing quality checks or suffering damage/contamination.
   * POST /api/inventory/batches/{id}/quarantine
   */
  quarantineBatch: (
    id: number,
    data: QuarantineBatchRequest,
    signal?: AbortSignal
  ): Promise<BatchResponse> =>
    apiClient.post<BatchResponse>(`/api/inventory/batches/${id}/quarantine`, data, { signal }),

  /**
   * Writes off physical stock from an active batch (quarantined or damaged stock disposal).
   * POST /api/inventory/batches/{id}/write-off
   */
  writeOffBatch: (
    id: number,
    data: WriteOffBatchRequest,
    signal?: AbortSignal
  ): Promise<WriteOffBatchResponse> =>
    apiClient.post<WriteOffBatchResponse>(`/api/inventory/batches/${id}/write-off`, data, { signal }),

  /**
   * Adjusts stock quantity on an active batch (spoilage, correction, count discrepancy).
   * POST /api/inventory/adjustments
   */
  adjustStock: (
    data: StockAdjustmentRequest,
    signal?: AbortSignal
  ): Promise<StockAdjustmentResponse> =>
    apiClient.post<StockAdjustmentResponse>('/api/inventory/adjustments', data, { signal }),

  /**
   * Retrieves paginated stock movements audit trail.
   * GET /api/inventory/movements
   */
  getMovements: (
    query?: StockMovementListQuery,
    signal?: AbortSignal
  ): Promise<PagedResult<StockMovementResponse>> =>
    apiClient.get<PagedResult<StockMovementResponse>>('/api/inventory/movements', {
      params: query as Record<string, unknown>,
      signal,
    }),

  /**
   * Retrieves diagnostic invariant and ledger consistency audit.
   * GET /api/inventory/audit
   */
  auditInventory: (
    productId?: number,
    signal?: AbortSignal
  ): Promise<InventoryAuditResponse> =>
    apiClient.get<InventoryAuditResponse>('/api/inventory/audit', {
      params: productId !== undefined ? { productId } : undefined,
      signal,
    }),
};
