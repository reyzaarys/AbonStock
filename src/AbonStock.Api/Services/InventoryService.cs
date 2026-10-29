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

    public async Task<BatchResponse> QuarantineBatchAsync(int batchId, QuarantineBatchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Reason cannot be empty or whitespace.");
        }

        if (request.Reason.Length > 500)
        {
            throw new ValidationException("Reason cannot exceed 500 characters.");
        }

        if (!request.RowVersion.HasValue)
        {
            throw new ValidationException("RowVersion is required for optimistic concurrency verification.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            InventoryBatch batch;
            if (_dbContext.Database.IsRelational())
            {
                batch = await _dbContext.InventoryBatches
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""InventoryBatches""
                        WHERE ""Id"" = {batchId}
                        FOR UPDATE")
                    .Include(b => b.Product)
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");
            }
            else
            {
                batch = await _dbContext.InventoryBatches
                    .Include(b => b.Product)
                    .FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");
            }

            // Optimistic concurrency check against client-supplied RowVersion
            _dbContext.Entry(batch).Property(b => b.Version).OriginalValue = request.RowVersion.Value;

            if (batch.Status == BatchStatus.Quarantined)
            {
                throw new ConflictException($"Batch '{batch.BatchNo}' is already quarantined.");
            }

            if (batch.Status != BatchStatus.PendingRelease && batch.Status != BatchStatus.Released)
            {
                throw new ConflictException(
                    $"Batch '{batch.BatchNo}' cannot be quarantined from status '{batch.Status}'. Only PendingRelease or Released batches can be quarantined.");
            }

            if (batch.ReservedQuantity > 0)
            {
                throw new ConflictException(
                    $"Cannot quarantine batch '{batch.BatchNo}' because it has {batch.ReservedQuantity} active reserved units. Existing order reservations must be resolved first.");
            }

            // Quarantine state transition preserves PhysicalQuantity and Product.QuantityOnHand
            batch.Status = BatchStatus.Quarantined;
            batch.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return BatchResponse.FromEntity(batch);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                $"Batch with ID {batchId} has been modified by another operation. Please reload and retry.");
        }
    }

    public async Task<WriteOffBatchResponse> WriteOffBatchAsync(
        int batchId,
        WriteOffBatchRequest request,
        string? actor = null,
        CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            throw new ValidationException("Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("Reason cannot be empty or whitespace.");
        }

        if (request.Reason.Length > 500)
        {
            throw new ValidationException("Reason cannot exceed 500 characters.");
        }

        if (!request.BatchRowVersion.HasValue)
        {
            throw new ValidationException("BatchRowVersion is required for optimistic concurrency verification.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Lock Hierarchy step 1: Exclusive row-level lock on InventoryBatches via FOR UPDATE
            InventoryBatch batch;
            if (_dbContext.Database.IsRelational())
            {
                batch = await _dbContext.InventoryBatches
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""InventoryBatches""
                        WHERE ""Id"" = {batchId}
                        FOR UPDATE")
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");
            }
            else
            {
                batch = await _dbContext.InventoryBatches
                    .FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");
            }

            // Lock Hierarchy step 2: Exclusive row-level lock on associated Products row via FOR UPDATE (matching OrderService.FulfillAsync hierarchy)
            Product product;
            if (_dbContext.Database.IsRelational())
            {
                product = await _dbContext.Products
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""Products""
                        WHERE ""Id"" = {batch.ProductId}
                        FOR UPDATE")
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"Product with ID {batch.ProductId} was not found.");
            }
            else
            {
                product = await _dbContext.Products
                    .FirstOrDefaultAsync(p => p.Id == batch.ProductId, ct)
                    ?? throw new NotFoundException($"Product with ID {batch.ProductId} was not found.");
            }

            // Optimistic concurrency verification on batch
            _dbContext.Entry(batch).Property(b => b.Version).OriginalValue = request.BatchRowVersion.Value;

            // Optional optimistic concurrency verification on product
            if (request.ProductRowVersion.HasValue)
            {
                _dbContext.Entry(product).Property(p => p.Version).OriginalValue = request.ProductRowVersion.Value;
            }

            // 1. Status rule: Only Quarantined or Released batches can be written off
            if (batch.Status != BatchStatus.Quarantined && batch.Status != BatchStatus.Released)
            {
                throw new ConflictException(
                    $"Cannot write off batch '{batch.BatchNo}' with status '{batch.Status}'. Only Quarantined or Released batches can be written off.");
            }

            // 2. Quantity rule: 1 <= Quantity <= PhysicalQuantity - ReservedQuantity
            int availableUnreserved = batch.PhysicalQuantity - batch.ReservedQuantity;
            if (request.Quantity > availableUnreserved)
            {
                throw new ConflictException(
                    $"Write-off quantity ({request.Quantity}) exceeds available unreserved stock ({availableUnreserved}). Current physical: {batch.PhysicalQuantity}, Reserved: {batch.ReservedQuantity}.");
            }

            if (request.Quantity > product.QuantityOnHand)
            {
                throw new ConflictException(
                    $"Write-off quantity ({request.Quantity}) exceeds product quantity on hand ({product.QuantityOnHand}).");
            }

            var previousBatchQty = batch.PhysicalQuantity;
            var previousProductQty = product.QuantityOnHand;
            var now = DateTime.UtcNow;

            // 3. Deduct physical quantity and product quantity on hand
            batch.PhysicalQuantity -= request.Quantity;
            if (batch.PhysicalQuantity == 0)
            {
                batch.Status = BatchStatus.Depleted;
            }
            batch.UpdatedAtUtc = now;

            product.QuantityOnHand -= request.Quantity;
            product.UpdatedAtUtc = now;

            // 4. Record StockMovement ledger entry
            var actorName = !string.IsNullOrWhiteSpace(actor) ? actor.Trim() : "admin";
            var reasonText = !string.IsNullOrWhiteSpace(request.DisposalDocumentNumber)
                ? $"Write-off: {request.Reason.Trim()} [Doc: {request.DisposalDocumentNumber.Trim()}] [By: {actorName}]"
                : $"Write-off: {request.Reason.Trim()} [By: {actorName}]";

            if (reasonText.Length > 500)
            {
                reasonText = reasonText[..500];
            }

            var movement = new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.WriteOff,
                QuantityChange = -request.Quantity,
                BalanceAfter = product.QuantityOnHand,
                InventoryBatchId = batch.Id,
                BatchBalanceAfter = batch.PhysicalQuantity,
                Reason = reasonText,
                CreatedAtUtc = now
            };

            _dbContext.StockMovements.Add(movement);

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new WriteOffBatchResponse(
                batch.Id,
                product.Id,
                batch.BatchNo,
                request.Quantity,
                previousBatchQty,
                batch.PhysicalQuantity,
                previousProductQty,
                product.QuantityOnHand,
                batch.Status.ToString(),
                movement.Id,
                batch.Version,
                product.Version,
                now);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                $"Batch with ID {batchId} or associated product has been modified by another operation. Please reload and retry.");
        }
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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Lock Hierarchy step 1: Exclusive row-level lock on candidate InventoryBatch row via FOR UPDATE
            InventoryBatch batch;
            if (_dbContext.Database.IsRelational())
            {
                batch = await _dbContext.InventoryBatches
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""InventoryBatches""
                        WHERE ""Id"" = {request.BatchId}
                        FOR UPDATE")
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"Batch with ID {request.BatchId} was not found.");
            }
            else
            {
                batch = await _dbContext.InventoryBatches
                    .FirstOrDefaultAsync(b => b.Id == request.BatchId, ct)
                    ?? throw new NotFoundException($"Batch with ID {request.BatchId} was not found.");
            }

            // Lock Hierarchy step 2: Exclusive row-level lock on associated Products row via FOR UPDATE (matching OrderService.FulfillAsync and WriteOffBatchAsync hierarchy)
            Product product;
            if (_dbContext.Database.IsRelational())
            {
                product = await _dbContext.Products
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""Products""
                        WHERE ""Id"" = {batch.ProductId}
                        FOR UPDATE")
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException($"Product with ID {batch.ProductId} was not found.");
            }
            else
            {
                product = await _dbContext.Products
                    .FirstOrDefaultAsync(p => p.Id == batch.ProductId, ct)
                    ?? throw new NotFoundException($"Product with ID {batch.ProductId} was not found.");
            }

            // Optimistic concurrency verification on batch
            _dbContext.Entry(batch).Property(b => b.Version).OriginalValue = request.BatchRowVersion.Value;

            // Optional optimistic concurrency verification on product
            if (request.ProductRowVersion.HasValue)
            {
                _dbContext.Entry(product).Property(p => p.Version).OriginalValue = request.ProductRowVersion.Value;
            }

            // Post-lock re-validations of batch status
            if (batch.Status == BatchStatus.Depleted)
            {
                throw new ConflictException("Cannot adjust a depleted batch. Any new production must be received as a new batch.");
            }

            if (batch.Status != BatchStatus.Released)
            {
                throw new ConflictException(
                    $"Cannot adjust batch '{batch.BatchNo}' with status '{batch.Status}'. Only Released batches can be adjusted.");
            }

            // Post-lock re-validations of batch quantities
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

            // Post-lock re-validations of product quantity on hand
            var newProductQty = product.QuantityOnHand + request.QuantityChange;
            if (newProductQty < 0)
            {
                throw new ConflictException(
                    $"Adjustment of {request.QuantityChange} would result in negative product stock ({newProductQty}). Current stock is {product.QuantityOnHand}.");
            }

            int prevBatchQty = batch.PhysicalQuantity;
            int prevProductQty = product.QuantityOnHand;
            var now = DateTime.UtcNow;

            batch.PhysicalQuantity = newBatchQty;
            batch.UpdatedAtUtc = now;

            if (newBatchQty == 0)
            {
                batch.Status = BatchStatus.Depleted;
            }

            product.QuantityOnHand = newProductQty;
            product.UpdatedAtUtc = now;

            var movement = new StockMovement
            {
                ProductId = batch.ProductId,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = request.QuantityChange,
                BalanceAfter = newProductQty,
                InventoryBatchId = batch.Id,
                BatchBalanceAfter = newBatchQty,
                Reason = request.Reason.Trim(),
                CreatedAtUtc = now
            };

            _dbContext.StockMovements.Add(movement);

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new StockAdjustmentResponse(
                batch.Id,
                batch.ProductId,
                prevBatchQty,
                newBatchQty,
                prevProductQty,
                newProductQty,
                batch.Status.ToString(),
                batch.Version,
                product.Version,
                now);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                "Inventory has been modified by another concurrent operation. Please reload and retry.");
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Inventory constraint violation occurred during adjustment.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<BatchResponse> GetBatchByIdAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await _dbContext.InventoryBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new NotFoundException($"Batch with ID {batchId} was not found.");

        return BatchResponse.FromEntity(batch);
    }

    public async Task<PagedResult<BatchResponse>> GetBatchesAsync(BatchListQuery query, CancellationToken ct = default)
    {
        var dbQuery = _dbContext.InventoryBatches
            .AsNoTracking()
            .Include(b => b.Product)
            .AsQueryable();

        if (query.ProductId.HasValue)
        {
            dbQuery = dbQuery.Where(b => b.ProductId == query.ProductId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(b => b.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            dbQuery = dbQuery.Where(b =>
                EF.Functions.ILike(b.BatchNo, pattern) ||
                (b.Product != null && (EF.Functions.ILike(b.Product.Name, pattern) || EF.Functions.ILike(b.Product.Sku, pattern))));
        }

        dbQuery = (query.SortBy?.Trim().ToLowerInvariant(), query.SortDescending) switch
        {
            ("expirydate", true) => dbQuery.OrderByDescending(b => b.ExpiryDate).ThenByDescending(b => b.Id),
            ("expirydate", false) => dbQuery.OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id),
            ("createdat", true) => dbQuery.OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id),
            ("createdat", false) => dbQuery.OrderBy(b => b.CreatedAtUtc).ThenBy(b => b.Id),
            ("batchno", true) => dbQuery.OrderByDescending(b => b.BatchNo).ThenByDescending(b => b.Id),
            ("batchno", false) => dbQuery.OrderBy(b => b.BatchNo).ThenBy(b => b.Id),
            ("productiondate", true) => dbQuery.OrderByDescending(b => b.ProductionDate).ThenByDescending(b => b.Id),
            _ => dbQuery.OrderBy(b => b.ProductionDate).ThenBy(b => b.Id)
        };

        var totalCount = await dbQuery.CountAsync(ct);

        var items = await dbQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => BatchResponse.FromEntity(b))
            .ToListAsync(ct);

        return new PagedResult<BatchResponse>(items, query.Page, query.PageSize, totalCount);
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

    public async Task<InventoryAuditResponse> AuditInventoryAsync(int? productId = null, CancellationToken ct = default)
    {
        // 1. Fetch data read-only with AsNoTracking to guarantee zero mutations
        var productQuery = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Batches)
            .AsQueryable();

        if (productId.HasValue)
        {
            var exists = await _dbContext.Products.AnyAsync(p => p.Id == productId.Value, ct);
            if (!exists)
            {
                throw new NotFoundException($"Product with ID {productId.Value} was not found.");
            }
            productQuery = productQuery.Where(p => p.Id == productId.Value);
        }

        var products = await productQuery
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        var activeReservationsQuery = _dbContext.OrderReservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active);

        if (productId.HasValue)
        {
            var batchIds = products.SelectMany(p => p.Batches).Select(b => b.Id).ToList();
            activeReservationsQuery = activeReservationsQuery.Where(r => batchIds.Contains(r.InventoryBatchId));
        }

        var activeReservations = await activeReservationsQuery.ToListAsync(ct);

        var stockMovementsQuery = _dbContext.StockMovements
            .AsNoTracking()
            .OrderBy(sm => sm.CreatedAtUtc)
            .ThenBy(sm => sm.Id)
            .AsQueryable();

        if (productId.HasValue)
        {
            stockMovementsQuery = stockMovementsQuery.Where(sm => sm.ProductId == productId.Value);
        }

        var stockMovements = await stockMovementsQuery.ToListAsync(ct);

        // 2. Inventory Invariant Check: Product.QuantityOnHand vs SUM(Batch.PhysicalQuantity)
        var discrepancies = new List<ProductDiscrepancyResponse>();
        int consistentCount = 0;
        int inconsistentCount = 0;

        foreach (var product in products)
        {
            // Empty aggregation returns 0 correctly when product has no batches
            int expectedQty = product.Batches.Sum(b => b.PhysicalQuantity);
            int actualQty = product.QuantityOnHand;
            int discrepancy = actualQty - expectedQty;
            bool isConsistent = (discrepancy == 0) && (actualQty >= 0);

            if (isConsistent)
            {
                consistentCount++;
            }
            else
            {
                inconsistentCount++;
                discrepancies.Add(new ProductDiscrepancyResponse(
                    product.Id,
                    product.Sku,
                    product.Name,
                    expectedQty,
                    actualQty,
                    discrepancy,
                    product.Batches.Count,
                    isConsistent));
            }
        }

        // 3. Batch Invariant Checks
        var batchViolations = new List<BatchInvariantViolationResponse>();
        var activeResByBatch = activeReservations
            .GroupBy(r => r.InventoryBatchId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

        foreach (var product in products)
        {
            foreach (var batch in product.Batches)
            {
                // Invariant: PhysicalQuantity cannot be negative
                if (batch.PhysicalQuantity < 0)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "NegativePhysicalQuantity",
                        $"Batch '{batch.BatchNo}' has negative physical quantity ({batch.PhysicalQuantity})."));
                }

                // Invariant: ReservedQuantity cannot be negative
                if (batch.ReservedQuantity < 0)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "NegativeReservedQuantity",
                        $"Batch '{batch.BatchNo}' has negative reserved quantity ({batch.ReservedQuantity})."));
                }

                // Invariant: ReservedQuantity cannot exceed PhysicalQuantity
                if (batch.ReservedQuantity > batch.PhysicalQuantity)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "ReservedExceedsPhysical",
                        $"Batch '{batch.BatchNo}' reserved quantity ({batch.ReservedQuantity}) exceeds physical quantity ({batch.PhysicalQuantity})."));
                }

                // Invariant: If PhysicalQuantity == 0, Status must be Depleted
                if (batch.PhysicalQuantity == 0 && batch.Status != BatchStatus.Depleted)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "ZeroPhysicalNotDepleted",
                        $"Batch '{batch.BatchNo}' has 0 physical quantity but status is '{batch.Status}' instead of 'Depleted'."));
                }

                // Invariant: If Status == Depleted, PhysicalQuantity and ReservedQuantity must be 0
                if (batch.Status == BatchStatus.Depleted && (batch.PhysicalQuantity > 0 || batch.ReservedQuantity > 0))
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "DepletedWithRemainingStock",
                        $"Batch '{batch.BatchNo}' is marked 'Depleted' but has {batch.PhysicalQuantity} physical quantity or {batch.ReservedQuantity} reserved quantity."));
                }

                // Invariant: Quarantined batches cannot hold active reservations
                if (batch.Status == BatchStatus.Quarantined && batch.ReservedQuantity > 0)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "QuarantinedWithActiveReservations",
                        $"Batch '{batch.BatchNo}' is Quarantined but has {batch.ReservedQuantity} active reserved units."));
                }

                // Invariant: PendingRelease batches cannot hold active reservations
                if (batch.Status == BatchStatus.PendingRelease && batch.ReservedQuantity > 0)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "PendingReleaseWithActiveReservations",
                        $"Batch '{batch.BatchNo}' is PendingRelease but has {batch.ReservedQuantity} active reserved units."));
                }

                // Invariant: ReservedQuantity must equal sum of active OrderReservations
                int activeSum = activeResByBatch.GetValueOrDefault(batch.Id, 0);
                if (activeSum != batch.ReservedQuantity)
                {
                    batchViolations.Add(new BatchInvariantViolationResponse(
                        batch.Id, product.Id, product.Sku, batch.BatchNo, batch.Status.ToString(),
                        batch.PhysicalQuantity, batch.ReservedQuantity,
                        "ReservationSumMismatch",
                        $"Batch '{batch.BatchNo}' has ReservedQuantity={batch.ReservedQuantity}, but sum of active reservations in database is {activeSum}."));
                }
            }
        }

        // 4. Stock Ledger Historical Audit
        var ledgerIssues = new List<string>();
        var ledgerLimitations = new List<string>
        {
            "Status transitions (PendingRelease -> Released, Released -> Quarantined) alter batch sellability without writing to StockMovements, as physical quantity is unchanged.",
            "Order reservations alter AvailableQuantity and ReservedQuantity upon order confirmation, but are recorded in OrderReservations rather than StockMovements until fulfillment.",
            "Disposal and write-off metadata (e.g. disposal document numbers, actor usernames) are stored in the freeform Reason text column rather than a dedicated disposal entity or foreign key.",
            "Stock movements rely on application-generated timestamps (CreatedAtUtc) rather than database transaction commit order or monotonically increasing log sequence numbers (LSN)."
        };

        bool hasCompromised = false;
        bool hasUnverifiable = false;

        var movementsByProduct = stockMovements.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var movementsByBatch = stockMovements.Where(m => m.InventoryBatchId.HasValue).GroupBy(m => m.InventoryBatchId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        // A. Product-level Ledger Chains
        foreach (var product in products)
        {
            if (!movementsByProduct.TryGetValue(product.Id, out var prodMovements) || prodMovements.Count == 0)
            {
                if (product.QuantityOnHand > 0)
                {
                    hasUnverifiable = true;
                    ledgerIssues.Add($"Product '{product.Sku}' (ID: {product.Id}) has QuantityOnHand={product.QuantityOnHand} but zero stock movements exist in the ledger.");
                }
                continue;
            }

            int runningProductBalance = 0;
            foreach (var m in prodMovements)
            {
                int expectedBalanceAfter = runningProductBalance + m.QuantityChange;
                if (m.BalanceAfter != expectedBalanceAfter)
                {
                    hasCompromised = true;
                    ledgerIssues.Add($"Product '{product.Sku}' movement ID {m.Id} ({m.MovementType}): BalanceAfter ({m.BalanceAfter}) does not match expected ({runningProductBalance} + {m.QuantityChange} = {expectedBalanceAfter}).");
                }
                runningProductBalance = m.BalanceAfter;
            }

            if (runningProductBalance != product.QuantityOnHand)
            {
                hasCompromised = true;
                ledgerIssues.Add($"Product '{product.Sku}' final ledger balance ({runningProductBalance}) does not match current QuantityOnHand ({product.QuantityOnHand}).");
            }
        }

        // B. Batch-level Ledger Chains
        foreach (var product in products)
        {
            foreach (var batch in product.Batches)
            {
                if (!movementsByBatch.TryGetValue(batch.Id, out var batchMovements) || batchMovements.Count == 0)
                {
                    if (batch.PhysicalQuantity > 0 || batch.InitialQuantity > 0)
                    {
                        hasUnverifiable = true;
                        ledgerIssues.Add($"Batch '{batch.BatchNo}' (ID: {batch.Id}) has InitialQuantity={batch.InitialQuantity}, PhysicalQuantity={batch.PhysicalQuantity} but zero movements exist in the ledger.");
                    }
                    continue;
                }

                int runningBatchBalance = 0;
                foreach (var bm in batchMovements)
                {
                    if (!bm.BatchBalanceAfter.HasValue)
                    {
                        hasUnverifiable = true;
                        ledgerIssues.Add($"Batch '{batch.BatchNo}' movement ID {bm.Id} has NULL BatchBalanceAfter.");
                        runningBatchBalance += bm.QuantityChange;
                        continue;
                    }

                    int expectedBatchBalanceAfter = runningBatchBalance + bm.QuantityChange;
                    if (bm.BatchBalanceAfter.Value != expectedBatchBalanceAfter)
                    {
                        hasCompromised = true;
                        ledgerIssues.Add($"Batch '{batch.BatchNo}' movement ID {bm.Id}: BatchBalanceAfter ({bm.BatchBalanceAfter.Value}) does not match expected ({runningBatchBalance} + {bm.QuantityChange} = {expectedBatchBalanceAfter}).");
                    }
                    runningBatchBalance = bm.BatchBalanceAfter.Value;
                }

                if (runningBatchBalance != batch.PhysicalQuantity)
                {
                    hasCompromised = true;
                    ledgerIssues.Add($"Batch '{batch.BatchNo}' final ledger balance ({runningBatchBalance}) does not match current PhysicalQuantity ({batch.PhysicalQuantity}).");
                }
            }
        }

        string ledgerStatus;
        string ledgerSummary;
        if (hasCompromised)
        {
            ledgerStatus = "Compromised";
            ledgerSummary = $"Ledger is compromised: {ledgerIssues.Count} mathematical or sequence discrepancies detected.";
        }
        else if (hasUnverifiable)
        {
            ledgerStatus = "Unverifiable";
            ledgerSummary = $"Ledger is unverifiable: {ledgerIssues.Count} data completeness limitations detected (e.g. unrecorded movements or null balances).";
        }
        else
        {
            ledgerStatus = "Valid";
            ledgerSummary = "Ledger chain is valid and mathematically reconciled across all products and batches.";
        }

        bool isSystemConsistent = (inconsistentCount == 0) && (batchViolations.Count == 0) && (ledgerStatus == "Valid");

        return new InventoryAuditResponse(
            DateTime.UtcNow,
            products.Count,
            consistentCount,
            inconsistentCount,
            discrepancies,
            batchViolations,
            new LedgerAuditResponse(ledgerStatus, stockMovements.Count, ledgerIssues, ledgerLimitations, ledgerSummary),
            isSystemConsistent);
    }
}
