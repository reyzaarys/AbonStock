/**
 * Order status matching AbonStock.Api.Entities.OrderStatus string representation.
 */
export type OrderStatus = 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled';

/**
 * Order item DTO matching AbonStock.Api.Dtos.Orders.OrderItemResponse.
 */
export interface OrderItemResponse {
  id: number;
  productId: number;
  productSku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

/**
 * Order DTO matching AbonStock.Api.Dtos.Orders.OrderResponse.
 */
export interface OrderResponse {
  id: number;
  orderNumber: string;
  customerName: string;
  customerEmail: string | null;
  status: OrderStatus;
  totalAmount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  confirmedAtUtc: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  completedAtUtc: string | null;
  version: number;
  items: OrderItemResponse[];
}

/**
 * Query parameters matching AbonStock.Api.Dtos.Orders.OrderListQuery.
 */
export interface OrderListQuery {
  page?: number;
  pageSize?: number;
  status?: OrderStatus | string;
  customerSearch?: string;
}

/**
 * Line item in CreateOrderRequest matching AbonStock.Api.Dtos.Orders.CreateOrderItemRequest.
 */
export interface CreateOrderItemRequest {
  productId: number;
  quantity: number;
}

/**
 * Payload for POST /api/orders matching AbonStock.Api.Dtos.Orders.CreateOrderRequest.
 */
export interface CreateOrderRequest {
  customerName: string;
  customerEmail?: string | null;
  items: CreateOrderItemRequest[];
}

/**
 * Payload for POST /api/orders/{id}/confirm matching AbonStock.Api.Dtos.Orders.ConfirmOrderRequest.
 */
export interface ConfirmOrderRequest {
  rowVersion?: number;
}

/**
 * Payload for POST /api/orders/{id}/cancel matching AbonStock.Api.Dtos.Orders.CancelOrderRequest.
 */
export interface CancelOrderRequest {
  cancellationReason: string;
  rowVersion?: number;
}

/**
 * Payload for POST /api/orders/{id}/fulfill matching AbonStock.Api.Dtos.Orders.FulfillOrderRequest.
 */
export interface FulfillOrderRequest {
  rowVersion?: number;
}
