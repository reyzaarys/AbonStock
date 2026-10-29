import { apiClient } from '../lib/apiClient.ts';
import type {
  CancelOrderRequest,
  ConfirmOrderRequest,
  CreateOrderRequest,
  FulfillOrderRequest,
  OrderListQuery,
  OrderResponse,
  PagedResult,
} from '../types';

export const ordersService = {
  /**
   * Retrieves paginated orders.
   * GET /api/orders
   */
  list: (query?: OrderListQuery, signal?: AbortSignal): Promise<PagedResult<OrderResponse>> =>
    apiClient.get<PagedResult<OrderResponse>>('/api/orders', {
      params: query as Record<string, unknown>,
      signal,
    }),

  /**
   * Retrieves an order by its ID.
   * GET /api/orders/{id}
   */
  getById: (id: number, signal?: AbortSignal): Promise<OrderResponse> =>
    apiClient.get<OrderResponse>(`/api/orders/${id}`, { signal }),

  /**
   * Creates a new customer order in Pending status without deducting or reserving physical stock.
   * Stock reservation is deferred to order confirmation (FEFO allocation).
   * POST /api/orders
   */
  create: (data: CreateOrderRequest, signal?: AbortSignal): Promise<OrderResponse> =>
    apiClient.post<OrderResponse>('/api/orders', data, { signal }),

  /**
   * Confirms a pending order.
   * POST /api/orders/{id}/confirm
   */
  confirm: (
    id: number,
    data?: ConfirmOrderRequest,
    signal?: AbortSignal
  ): Promise<OrderResponse> =>
    apiClient.post<OrderResponse>(`/api/orders/${id}/confirm`, data ?? {}, { signal }),

  /**
   * Cancels an order and releases any active stock reservations.
   * POST /api/orders/{id}/cancel
   */
  cancel: (
    id: number,
    data: CancelOrderRequest,
    signal?: AbortSignal
  ): Promise<OrderResponse> =>
    apiClient.post<OrderResponse>(`/api/orders/${id}/cancel`, data, { signal }),

  /**
   * Fulfills a confirmed order, converting reservations into physical deductions.
   * POST /api/orders/{id}/fulfill
   */
  fulfill: (
    id: number,
    data?: FulfillOrderRequest,
    signal?: AbortSignal
  ): Promise<OrderResponse> =>
    apiClient.post<OrderResponse>(`/api/orders/${id}/fulfill`, data ?? {}, { signal }),
};
