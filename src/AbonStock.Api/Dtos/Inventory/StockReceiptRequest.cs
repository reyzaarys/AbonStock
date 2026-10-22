using System.ComponentModel.DataAnnotations;

namespace AbonStock.Api.Dtos.Inventory;

public record StockReceiptRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "ProductId must be greater than 0.")]
    public int ProductId { get; init; }

    [Required(ErrorMessage = "BatchNo is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "BatchNo must be between 1 and 100 characters.")]
    public string BatchNo { get; init; } = string.Empty;

    [Required(ErrorMessage = "ProductionDate is required.")]
    public DateOnly ProductionDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    [StringLength(50, ErrorMessage = "LocationId cannot exceed 50 characters.")]
    public string? LocationId { get; init; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than 0.")]
    public int Quantity { get; init; }

    [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string? Reason { get; init; }
}
