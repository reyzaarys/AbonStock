/**
 * Batch statuses matching AbonStock.Api.Entities.BatchStatus enum string representation.
 */
export type BatchStatus = 'PendingRelease' | 'Released' | 'Quarantined' | 'Depleted';

/**
 * Valid SortBy column values whitelisted in AbonStock.Api.Services.InventoryService.
 */
export type BatchSortBy = 'expirydate' | 'createdat' | 'batchno' | 'productiondate';

/**
 * Batch DTO matching AbonStock.Api.Dtos.Inventory.BatchResponse.
 */
export interface BatchResponse {
  id: number;
  productId: number;
  productSku: string | null;
  productName: string | null;
  batchNo: string;
  productionDate: string;
  expiryDate: string | null;
  locationId: string;
  initialQuantity: number;
  physicalQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  status: BatchStatus;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

/**
 * Query parameters matching AbonStock.Api.Dtos.Inventory.BatchListQuery.
 */
export interface BatchListQuery {
  page?: number;
  pageSize?: number;
  productId?: number;
  status?: BatchStatus | string;
  search?: string;
  sortBy?: BatchSortBy | string;
  sortDescending?: boolean;
}

/**
 * Payload for POST /api/inventory/receipts matching AbonStock.Api.Dtos.Inventory.StockReceiptRequest.
 */
export interface StockReceiptRequest {
  productId: number;
  batchNo: string;
  productionDate: string;
  expiryDate?: string | null;
  locationId?: string | null;
  quantity: number;
  reason?: string | null;
}

/**
 * Response matching AbonStock.Api.Dtos.Inventory.StockReceiptResponse.
 */
export interface StockReceiptResponse {
  batchId: number;
  productId: number;
  batchNo: string;
  productionDate: string;
  expiryDate: string | null;
  locationId: string;
  initialQuantity: number;
  physicalQuantity: number;
  status: BatchStatus;
  totalProductQuantityOnHand: number;
  batchVersion: number;
  productVersion: number;
  createdAtUtc: string;
}

/**
 * Payload for POST /api/inventory/batches/{id}/release matching AbonStock.Api.Dtos.Inventory.ReleaseBatchRequest.
 */
export interface ReleaseBatchRequest {
  visualCheckPassed: boolean;
  aromaCheckPassed: boolean;
  packagingCheckPassed: boolean;
  notes?: string | null;
  rowVersion: number;
}

/**
 * Payload for POST /api/inventory/batches/{id}/quarantine matching AbonStock.Api.Dtos.Inventory.QuarantineBatchRequest.
 */
export interface QuarantineBatchRequest {
  reason: string;
  notes?: string | null;
  rowVersion: number;
}

/**
 * Payload for POST /api/inventory/batches/{id}/write-off matching AbonStock.Api.Dtos.Inventory.WriteOffBatchRequest.
 */
export interface WriteOffBatchRequest {
  quantity: number;
  reason: string;
  batchRowVersion: number;
  productRowVersion?: number;
  disposalDocumentNumber?: string | null;
}

/**
 * Response matching AbonStock.Api.Dtos.Inventory.WriteOffBatchResponse.
 */
export interface WriteOffBatchResponse {
  batchId: number;
  productId: number;
  batchNo: string;
  writtenOffQuantity: number;
  previousPhysicalQuantity: number;
  newPhysicalQuantity: number;
  previousProductQuantityOnHand: number;
  newProductQuantityOnHand: number;
  status: BatchStatus;
  movementId: number;
  batchVersion: number;
  productVersion: number;
  timestampUtc: string;
}

/**
 * Payload for POST /api/inventory/adjustments matching AbonStock.Api.Dtos.Inventory.StockAdjustmentRequest.
 */
export interface StockAdjustmentRequest {
  batchId: number;
  quantityChange: number;
  reason: string;
  batchRowVersion: number;
  productRowVersion?: number;
}

/**
 * Response matching AbonStock.Api.Dtos.Inventory.StockAdjustmentResponse.
 */
export interface StockAdjustmentResponse {
  batchId: number;
  productId: number;
  previousPhysicalQuantity: number;
  newPhysicalQuantity: number;
  previousProductQuantityOnHand: number;
  newProductQuantityOnHand: number;
  status: BatchStatus;
  batchVersion: number;
  productVersion: number;
  timestampUtc: string;
}

/**
 * Stock movement types matching AbonStock.Api.Entities.StockMovementType string representation.
 */
export type StockMovementType = 'Receipt' | 'Adjustment' | 'OrderDeduction' | 'OrderCancellation' | 'WriteOff';

/**
 * Stock movement DTO matching AbonStock.Api.Dtos.Inventory.StockMovementResponse.
 */
export interface StockMovementResponse {
  id: number;
  productId: number;
  productSku: string | null;
  productName: string | null;
  inventoryBatchId: number | null;
  batchNo: string | null;
  movementType: StockMovementType;
  quantityChange: number;
  balanceAfter: number;
  batchBalanceAfter: number | null;
  reason: string;
  referenceOrderId: number | null;
  createdAtUtc: string;
}

/**
 * Query parameters matching AbonStock.Api.Dtos.Inventory.StockMovementListQuery.
 */
export interface StockMovementListQuery {
  page?: number;
  pageSize?: number;
  productId?: number;
  batchId?: number;
  movementType?: StockMovementType | string;
}

/**
 * Product-level inventory discrepancy matching AbonStock.Api.Dtos.Inventory.ProductDiscrepancyResponse.
 */
export interface ProductDiscrepancyResponse {
  productId: number;
  sku: string;
  productName: string;
  expectedQuantityOnHand: number;
  actualQuantityOnHand: number;
  discrepancy: number;
  batchCount: number;
  isConsistent: boolean;
}

/**
 * Batch-level invariant violation matching AbonStock.Api.Dtos.Inventory.BatchInvariantViolationResponse.
 */
export interface BatchInvariantViolationResponse {
  batchId: number;
  productId: number;
  productSku: string | null;
  batchNo: string;
  status: string;
  physicalQuantity: number;
  reservedQuantity: number;
  violationType: string;
  description: string;
}

/**
 * Ledger audit health status.
 */
export type LedgerAuditStatus = 'Valid' | 'Compromised' | 'Unverifiable';

/**
 * Stock ledger audit response matching AbonStock.Api.Dtos.Inventory.LedgerAuditResponse.
 */
export interface LedgerAuditResponse {
  status: LedgerAuditStatus | string;
  totalMovementsChecked: number;
  issues: string[];
  limitations: string[];
  summary: string;
}

/**
 * Full inventory invariant audit response matching AbonStock.Api.Dtos.Inventory.InventoryAuditResponse.
 */
export interface InventoryAuditResponse {
  auditTimestampUtc: string;
  totalProductsChecked: number;
  consistentProductsCount: number;
  inconsistentProductsCount: number;
  discrepancies: ProductDiscrepancyResponse[];
  invariantViolations: BatchInvariantViolationResponse[];
  ledgerAudit: LedgerAuditResponse;
  isSystemConsistent: boolean;
}
