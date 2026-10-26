using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Inventory;

public record BatchListQuery
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    public int? ProductId { get; init; }

    public BatchStatus? Status { get; init; }

    public string? Search { get; init; }

    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = false;
}
