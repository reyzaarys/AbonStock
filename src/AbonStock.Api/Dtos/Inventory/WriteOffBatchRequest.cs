using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Inventory;

public record WriteOffBatchRequest
{
    [Required(ErrorMessage = "Quantity is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    public int Quantity { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be between 1 and 500 characters.")]
    public string Reason { get; init; } = string.Empty;

    [Required(ErrorMessage = "BatchRowVersion is required for optimistic concurrency verification.")]
    public uint? BatchRowVersion { get; init; }

    public uint? ProductRowVersion { get; init; }

    [StringLength(100, ErrorMessage = "DisposalDocumentNumber cannot exceed 100 characters.")]
    public string? DisposalDocumentNumber { get; init; }
}
