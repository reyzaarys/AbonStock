using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Products;

public record ProductListQuery
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    public string? Search { get; init; }

    public bool IncludeInactive { get; init; } = false;
}
