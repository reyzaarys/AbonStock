using AbonStock.Api.Entities;

namespace AbonStock.Api.Dtos.Products;

public record ProductResponse(
    int Id,
    string Sku,
    string Name,
    string? Description,
    decimal UnitPrice,
    int QuantityOnHand,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    uint Version)
{
    public static ProductResponse FromEntity(Product product) => new(
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.UnitPrice,
        product.QuantityOnHand,
        product.IsActive,
        product.CreatedAtUtc,
        product.UpdatedAtUtc,
        product.Version);
}
