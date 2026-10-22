using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Inventory;

public record ReleaseBatchRequest
{
    [Required(ErrorMessage = "VisualCheckPassed is required.")]
    public bool? VisualCheckPassed { get; init; }

    [Required(ErrorMessage = "AromaCheckPassed is required.")]
    public bool? AromaCheckPassed { get; init; }

    [Required(ErrorMessage = "PackagingCheckPassed is required.")]
    public bool? PackagingCheckPassed { get; init; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; init; }

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency verification.")]
    public uint? RowVersion { get; init; }
}
