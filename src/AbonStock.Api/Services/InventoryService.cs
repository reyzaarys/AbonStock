using AbonStock.Api.Common;
using AbonStock.Api.Common.Exceptions;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AbonStock.Api.Services;

public class InventoryService(AbonStockDbContext dbContext) : IInventoryService
{
    private readonly AbonStockDbContext _dbContext = dbContext;

    public async Task<StockReceiptResponse> ReceiveStockAsync(StockReceiptRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            throw new ValidationException("Quantity must be greater than 0.");
        }

        var normalizedBatchNo = request.BatchNo?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedBatchNo))
        {
            throw new ValidationException("BatchNo cannot be empty or whitespace.");
        }

        var location = string.IsNullOrWhiteSpace(request.LocationId)
            ? InventoryBatch.DefaultLocationId
            : request.LocationId.Trim();

        if (!string.Equals(location, InventoryBatch.DefaultLocationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Invalid location '{request.LocationId}'. M1 only supports location '{InventoryBatch.DefaultLocationId}'.");
        }

        if (request.ExpiryDate.HasValue && request.ExpiryDate.Value < request.ProductionDate)
        {
            throw new ValidationException("ExpiryDate cannot be earlier than ProductionDate.");
        }

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw new NotFoundException($"Product with ID {request.ProductId} was not found.");

        if (!product.IsActive)
        {
            throw new ValidationException($"Cannot receive stock for inactive product '{product.Sku}'.");
        }

        // Pre-emptive duplicate batch check
        var batchExists = await _dbContext.InventoryBatches
            .AnyAsync(b => b.ProductId == request.ProductId && b.BatchNo == normalizedBatchNo, ct);

        if (batchExists)
        {
            throw new ConflictException(
                $"A batch with number '{normalizedBatchNo}' already exists for product ID {request.ProductId}.");
        }

        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = normalizedBatchNo,
            ProductionDate = request.ProductionDate,
            ExpiryDate = request.ExpiryDate,
            LocationId = InventoryBatch.DefaultLocationId,
            InitialQuantity = request.Quantity,
            PhysicalQuantity = request.Quantity,
            Status = BatchStatus.PendingRelease,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.InventoryBatches.Add(batch);

        product.QuantityOnHand += request.Quantity;
        product.UpdatedAtUtc = DateTime.UtcNow;

        var movement = new StockMovement
        {
            ProductId = product.Id,
            MovementType = StockMovementType.Receipt,
            QuantityChange = request.Quantity,
            BalanceAfter = product.QuantityOnHand,
            InventoryBatch = batch,
            BatchBalanceAfter = batch.PhysicalQuantity,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Initial batch receipt" : request.Reason.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.StockMovements.Add(movement);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_InventoryBatches_ProductId_BatchNo" })
        {
            throw new ConflictException(
                $"A batch with number '{normalizedBatchNo}' already exists for product ID {request.ProductId}.");
        }

        return new StockReceiptResponse(
            batch.Id,
            product.Id,
            batch.BatchNo,
            batch.ProductionDate,
            batch.ExpiryDate,
            batch.LocationId,
            batch.InitialQuantity,
            batch.PhysicalQuantity,
            batch.Status.ToString(),
            product.QuantityOnHand,
            batch.Version,
            product.Version,
            batch.CreatedAtUtc);
    }

    public async Task<BatchResponse> ReleaseBatchAsync(int batchId, ReleaseBatchRequest request, CancellationToken ct = default)
    {
        if (!request.RowVersion.HasValue)
        {
            throw new ValidationException("RowVersion is required for optimistic concurrency verification.");
        }

        if (request.VisualCheckPassed != true || request.AromaCheckPassed != true || request.PackagingCheckPassed != true)
        {
            throw new ValidationException("All quality checks (Visual, Aroma, Packaging) must pass to release the batch.");
        }

        var batch = await _dbContext.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");

        // Optimistic concurrency check
        _dbContext.Entry(batch).Property(b => b.Version).OriginalValue = request.RowVersion.Value;

        if (batch.Status == BatchStatus.Released)
        {
            throw new ConflictException($"Batch '{batch.BatchNo}' is already released.");
        }

        if (batch.Status != BatchStatus.PendingRelease)
        {
            throw new ConflictException(
                $"Batch '{batch.BatchNo}' cannot be released from status '{batch.Status}'. Only PendingRelease batches can be released.");
        }

        batch.Status = BatchStatus.Released;
        batch.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                $"Batch with ID {batchId} has been modified by another operation. Please reload and retry.");
        }

        return BatchResponse.FromEntity(batch);
    }

    public async Task<StockAdjustmentResponse> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct = default)
    {
        if (request.QuantityChange == 0)
        {
            throw new ValidationException("QuantityChange cannot be zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Reason cannot be empty or whitespace.");
        }

        if (!request.BatchRowVersion.HasValue)
        {
            throw new ValidationException("BatchRowVersion is required for optimistic concurrency verification.");
        }

        var batch = await _dbContext.InventoryBatches
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == request.BatchId, ct)
            ?? throw new NotFoundException($"Batch with ID {request.BatchId} was not found.");

        // Optimistic concurrency check on batch
        _dbContext.Entry(batch).Property(b => b.Version).OriginalValue = request.BatchRowVersion.Value;

        // Optional concurrency check on product if provided
        if (request.ProductRowVersion.HasValue)
        {
            _dbContext.Entry(batch.Product).Property(p => p.Version).OriginalValue = request.ProductRowVersion.Value;
        }

        if (batch.Status == BatchStatus.Depleted)
        {
            throw new ConflictException("Cannot adjust a depleted batch. Any new production must be received as a new batch.");
        }

        if (batch.Status != BatchStatus.Released)
        {
            throw new ConflictException(
                $"Cannot adjust batch '{batch.BatchNo}' with status '{batch.Status}'. Only Released batches can be adjusted.");
        }

        var newBatchQty = batch.PhysicalQuantity + request.QuantityChange;
        if (newBatchQty < 0)
        {
            throw new ConflictException(
                $"Adjustment of {request.QuantityChange} would result in negative batch quantity ({newBatchQty}). Current batch quantity is {batch.PhysicalQuantity}.");
        }

        if (newBatchQty < batch.ReservedQuantity)
        {
            throw new ConflictException(
                $"Adjustment of {request.QuantityChange} would reduce physical quantity ({newBatchQty}) below reserved quantity ({batch.ReservedQuantity}). Current batch quantity is {batch.PhysicalQuantity}.");
        }

        var newProductQty = batch.Product.QuantityOnHand + request.QuantityChange;
        if (newProductQty < 0)
        {
            throw new ConflictException(
                $"Adjustment of {request.QuantityChange} would result in negative product stock ({newProductQty}). Current stock is {batch.Product.QuantityOnHand}.");
        }

        int prevBatchQty = batch.PhysicalQuantity;
        int prevProductQty = batch.Product.QuantityOnHand;

        batch.PhysicalQuantity = newBatchQty;
        batch.UpdatedAtUtc = DateTime.UtcNow;

        if (newBatchQty == 0)
        {
            batch.Status = BatchStatus.Depleted;
        }

        batch.Product.QuantityOnHand = newProductQty;
        batch.Product.UpdatedAtUtc = DateTime.UtcNow;

        var movement = new StockMovement
        {
            ProductId = batch.ProductId,
            MovementType = StockMovementType.Adjustment,
            QuantityChange = request.QuantityChange,
            BalanceAfter = newProductQty,
            InventoryBatchId = batch.Id,
            BatchBalanceAfter = newBatchQty,
            Reason = request.Reason.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.StockMovements.Add(movement);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                "Inventory has been modified by another concurrent operation. Please reload and retry.");
        }

        return new StockAdjustmentResponse(
            batch.Id,
            batch.ProductId,
            prevBatchQty,
            newBatchQty,
            prevProductQty,
            newProductQty,
            batch.Status.ToString(),
            batch.Version,
            batch.Product.Version,
            DateTime.UtcNow);
    }

    public async Task<BatchResponse> GetBatchByIdAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await _dbContext.InventoryBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");

        return BatchResponse.FromEntity(batch);
    }

    public async Task<PagedResult<StockMovementResponse>> GetMovementsAsync(StockMovementListQuery query, CancellationToken ct = default)
    {
        var dbQuery = _dbContext.StockMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.InventoryBatch)
            .AsQueryable();

        if (query.ProductId.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.ProductId == query.ProductId.Value);
        }

        if (query.BatchId.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.InventoryBatchId == query.BatchId.Value);
        }

        if (query.MovementType.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.MovementType == query.MovementType.Value);
        }

        var totalCount = await dbQuery.CountAsync(ct);

        var items = await dbQuery
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => StockMovementResponse.FromEntity(m))
            .ToListAsync(ct);

        return new PagedResult<StockMovementResponse>(items, query.Page, query.PageSize, totalCount);
    }
}
