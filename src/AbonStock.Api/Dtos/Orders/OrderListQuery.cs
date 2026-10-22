using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Orders;

public record OrderListQuery
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    public OrderStatus? Status { get; init; }

    public string? CustomerSearch { get; init; }
}
