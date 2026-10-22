using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Products;

public record UpdateProductRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters.")]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; init; }

    [Range(0, 100_000_000, ErrorMessage = "UnitPrice must be non-negative.")]
    public decimal UnitPrice { get; init; }

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency verification.")]
    public uint? RowVersion { get; init; }
}
