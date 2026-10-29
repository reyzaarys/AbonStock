using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Inventory;

public record QuarantineBatchRequest
{
    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be between 1 and 500 characters.")]
    public string Reason { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; init; }

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency verification.")]
    public uint? RowVersion { get; init; }
}
