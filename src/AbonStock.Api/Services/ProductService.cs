using AbonStock.Api.Common;
using AbonStock.Api.Common.Exceptions;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AbonStock.Api.Services;

public class ProductService(AbonStockDbContext dbContext) : IProductService
{
    private readonly AbonStockDbContext _dbContext = dbContext;

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        if (request.UnitPrice < 0)
        {
            throw new ValidationException("UnitPrice must be non-negative.");
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSku))
        {
            throw new ValidationException("SKU cannot be empty or whitespace.");
        }

        // Pre-emptive duplicate check
        var skuExists = await _dbContext.Products.AnyAsync(p => p.Sku == normalizedSku, ct);
        if (skuExists)
        {
            throw new ConflictException($"A product with SKU '{normalizedSku}' already exists.");
        }

        var product = new Product
        {
            Sku = normalizedSku,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            UnitPrice = request.UnitPrice,
            QuantityOnHand = 0,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Products.Add(product);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Products_Sku" })
        {
            // Catches race-condition duplicate inserts protected by the database unique index
            throw new ConflictException($"A product with SKU '{normalizedSku}' already exists.");
        }

        return ProductResponse.FromEntity(product);
    }

    public async Task<ProductResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException($"Product with ID {id} was not found.");

        return ProductResponse.FromEntity(product);
    }

    public async Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken ct = default)
    {
        var dbQuery = _dbContext.Products.AsNoTracking().AsQueryable();

        if (!query.IncludeInactive)
        {
            dbQuery = dbQuery.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{query.Search.Trim()}%";
            dbQuery = dbQuery.Where(p =>
                EF.Functions.ILike(p.Name, searchPattern) ||
                EF.Functions.ILike(p.Sku, searchPattern));
        }

        var totalCount = await dbQuery.CountAsync(ct);

        var items = await dbQuery
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => ProductResponse.FromEntity(p))
            .ToListAsync(ct);

        return new PagedResult<ProductResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Name cannot be empty or whitespace.");
        }

        if (request.UnitPrice < 0)
        {
            throw new ValidationException("UnitPrice must be non-negative.");
        }

        if (!request.RowVersion.HasValue)
        {
            throw new ValidationException("RowVersion is required for optimistic concurrency verification.");
        }

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException($"Product with ID {id} was not found.");

        // Set the expected concurrency token from the client's request
        _dbContext.Entry(product).Property(p => p.Version).OriginalValue = request.RowVersion.Value;

        product.Name = request.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.UnitPrice = request.UnitPrice;
        product.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                $"Product with ID {id} has been modified by another operation. Please reload and retry.");
        }

        return ProductResponse.FromEntity(product);
    }

    public async Task<ProductResponse> DeactivateAsync(int id, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException($"Product with ID {id} was not found.");

        if (product.IsActive)
        {
            product.IsActive = false;
            product.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }

        return ProductResponse.FromEntity(product);
    }
}
