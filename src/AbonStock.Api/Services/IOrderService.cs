using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Orders;

namespace AbonStock.Api.Services;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<OrderResponse> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, CancellationToken ct = default);
    Task<OrderResponse> ConfirmAsync(int id, ConfirmOrderRequest request, CancellationToken ct = default);
    Task<OrderResponse> CancelAsync(int id, CancelOrderRequest request, CancellationToken ct = default);
    Task<OrderResponse> FulfillAsync(int id, FulfillOrderRequest request, CancellationToken ct = default);
}
