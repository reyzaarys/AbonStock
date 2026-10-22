using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Products;

namespace AbonStock.Api.Services;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductResponse> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken ct = default);
    Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default);
    Task<ProductResponse> DeactivateAsync(int id, CancellationToken ct = default);
}
