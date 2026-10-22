using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Inventory;

public record StockAdjustmentRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "BatchId must be greater than 0.")]
    public int BatchId { get; init; }

    [Required]
    public int QuantityChange { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be between 1 and 500 characters.")]
    public string Reason { get; init; } = string.Empty;

    [Required(ErrorMessage = "BatchRowVersion is required for optimistic concurrency verification.")]
    public uint? BatchRowVersion { get; init; }

    public uint? ProductRowVersion { get; init; }
}
