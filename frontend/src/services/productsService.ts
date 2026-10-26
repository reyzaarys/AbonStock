import { apiClient } from '../lib/apiClient.ts';
import type {
  CreateProductRequest,
  PagedResult,
  ProductListQuery,
  ProductResponse,
  UpdateProductRequest,
} from '../types';

export const productsService = {
  /**
   * Retrieves a paginated list of products.
   * GET /api/products
   */
  list: (query?: ProductListQuery, signal?: AbortSignal): Promise<PagedResult<ProductResponse>> =>
    apiClient.get<PagedResult<ProductResponse>>('/api/products', {
      params: query as Record<string, unknown>,
      signal,
    }),

  /**
   * Retrieves a product by its primary identifier.
   * GET /api/products/{id}
   */
  getById: (id: number, signal?: AbortSignal): Promise<ProductResponse> =>
    apiClient.get<ProductResponse>(`/api/products/${id}`, { signal }),

  /**
   * Creates a new product.
   * POST /api/products
   */
  create: (data: CreateProductRequest, signal?: AbortSignal): Promise<ProductResponse> =>
    apiClient.post<ProductResponse>('/api/products', data, { signal }),

  /**
   * Updates an existing product with optimistic concurrency check.
   * PUT /api/products/{id}
   */
  update: (
    id: number,
    data: UpdateProductRequest,
    signal?: AbortSignal
  ): Promise<ProductResponse> =>
    apiClient.put<ProductResponse>(`/api/products/${id}`, data, { signal }),

  /**
   * Deactivates a product.
   * PATCH /api/products/{id}/deactivate
   */
  deactivate: (id: number, signal?: AbortSignal): Promise<ProductResponse> =>
    apiClient.patch<ProductResponse>(`/api/products/${id}/deactivate`, undefined, { signal }),
};
