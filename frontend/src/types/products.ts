/**
 * Product DTO matching AbonStock.Api.Dtos.Products.ProductResponse.
 */
export interface ProductResponse {
  id: number;
  sku: string;
  name: string;
  description: string | null;
  unitPrice: number;
  quantityOnHand: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
}

/**
 * Query parameters matching AbonStock.Api.Dtos.Products.ProductListQuery.
 */
export interface ProductListQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  includeInactive?: boolean;
}

/**
 * Payload for POST /api/products matching AbonStock.Api.Dtos.Products.CreateProductRequest.
 */
export interface CreateProductRequest {
  sku: string;
  name: string;
  description?: string | null;
  unitPrice: number;
}

/**
 * Payload for PUT /api/products/{id} matching AbonStock.Api.Dtos.Products.UpdateProductRequest.
 */
export interface UpdateProductRequest {
  name: string;
  description?: string | null;
  unitPrice: number;
  rowVersion: number;
}
