using AbonStock.Api.Common;
using AbonStock.Api.Common.Exceptions;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Orders;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AbonStock.Api.Services;

public class OrderService(AbonStockDbContext dbContext) : IOrderService
{
    private readonly AbonStockDbContext _dbContext = dbContext;

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            throw new ValidationException("CustomerName cannot be empty or whitespace.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationException("Order must contain at least one item.");
        }

        // Validate quantities > 0
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            throw new ValidationException("Quantity must be greater than zero.");
        }

        // Validate no duplicate products in a single order
        var duplicateProductIds = request.Items
            .GroupBy(x => x.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateProductIds.Count != 0)
        {
            throw new ValidationException(
                $"Duplicate product ID {duplicateProductIds[0]} in order items. Each product may only appear once per order.");
        }

        // Fetch products from database to ensure existence, active status, and server-authoritative pricing
        var productIds = request.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await _dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);

        var missingProductIds = productIds.Except(products.Select(p => p.Id)).ToList();
        if (missingProductIds.Count != 0)
        {
            throw new NotFoundException($"Product with ID {missingProductIds[0]} was not found.");
        }

        var inactiveProduct = products.FirstOrDefault(p => !p.IsActive);
        if (inactiveProduct != null)
        {
            throw new ValidationException($"Cannot create order with inactive product '{inactiveProduct.Sku}'.");
        }

        var productMap = products.ToDictionary(p => p.Id);

        // Generate a human-readable unique order number (e.g. ORD-20261015-A1B2C3D4)
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail.Trim(),
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        foreach (var reqItem in request.Items)
        {
            var product = productMap[reqItem.ProductId];
            var lineTotal = reqItem.Quantity * product.UnitPrice;

            order.Items.Add(new OrderItem
            {
                Order = order,
                ProductId = product.Id,
                ProductSku = product.Sku,
                ProductName = product.Name,
                Quantity = reqItem.Quantity,
                UnitPrice = product.UnitPrice,
                LineTotal = lineTotal
            });
        }

        // Server-side calculation of order total amount
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        return OrderResponse.FromEntity(order);
    }

    public async Task<OrderResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException($"Order with ID {id} was not found.");

        return OrderResponse.FromEntity(order);
    }

    public async Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, CancellationToken ct = default)
    {
        var dbQuery = _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .AsQueryable();

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CustomerSearch))
        {
            var pattern = $"%{query.CustomerSearch.Trim()}%";
            dbQuery = dbQuery.Where(o =>
                EF.Functions.ILike(o.CustomerName, pattern) ||
                (o.CustomerEmail != null && EF.Functions.ILike(o.CustomerEmail, pattern)) ||
                EF.Functions.ILike(o.OrderNumber, pattern));
        }

        var totalCount = await dbQuery.CountAsync(ct);

        var items = await dbQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => OrderResponse.FromEntity(o))
            .ToListAsync(ct);

        return new PagedResult<OrderResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<OrderResponse> ConfirmAsync(int id, ConfirmOrderRequest request, CancellationToken ct = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Acquire row-level lock on the order row first to serialize concurrent confirmation/cancellation requests.
            // Lock Hierarchy / Order:
            // 1. Orders table: SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE
            // 2. InventoryBatches table: candidate batches locked via FOR UPDATE in canonical primary key order (Id ASC)
            // ConfirmAsync, CancelAsync, and FulfillAsync strictly follow this Order -> Batch hierarchy, preventing cross-resource deadlocks.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE", ct);

            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new NotFoundException($"Order with ID {id} was not found.");

            if (order.Status != OrderStatus.Pending)
            {
                throw new ConflictException(
                    $"Order '{order.OrderNumber}' cannot be confirmed because it is in status '{order.Status}'.");
            }

            if (request?.RowVersion.HasValue == true)
            {
                _dbContext.Entry(order).Property(o => o.Version).OriginalValue = request.RowVersion.Value;
            }

            if (order.Items == null || order.Items.Count == 0)
            {
                throw new ValidationException("Order must contain at least one item.");
            }

            if (order.Items.Any(i => i.Quantity <= 0))
            {
                throw new ValidationException("Order items must have positive quantities.");
            }

            var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _dbContext.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            if (products.Count != productIds.Count)
            {
                var missingId = productIds.Except(products.Select(p => p.Id)).First();
                throw new NotFoundException($"Product with ID {missingId} was not found.");
            }

            var inactiveProduct = products.FirstOrDefault(p => !p.IsActive);
            if (inactiveProduct != null)
            {
                throw new ValidationException($"Cannot confirm order containing inactive product '{inactiveProduct.Sku}'.");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            // Sort items deterministically by ProductId to acquire row locks in consistent order and prevent deadlocks
            var sortedItems = order.Items.OrderBy(i => i.ProductId).ToList();

            foreach (var item in sortedItems)
            {
                // Step 1: Acquire row-level locks on candidate batches in canonical primary key order (Id ASC)
                // Candidate filter strictly enforces: Released status, positive available quantity,
                // non-null ExpiryDate, and ExpiryDate >= today (FEFO sales policy).
                var eligibleBatches = await _dbContext.InventoryBatches
                    .FromSqlInterpolated($@"
                        SELECT *, ""xmin"" FROM ""InventoryBatches""
                        WHERE ""ProductId"" = {item.ProductId}
                          AND ""Status"" = {(int)BatchStatus.Released}
                          AND ""PhysicalQuantity"" > ""ReservedQuantity""
                          AND ""ExpiryDate"" IS NOT NULL
                          AND ""ExpiryDate"" >= {today}
                        ORDER BY ""Id"" ASC
                        FOR UPDATE")
                    .ToListAsync(ct);

                int totalAvailable = eligibleBatches.Sum(b => b.PhysicalQuantity - b.ReservedQuantity);
                if (totalAvailable < item.Quantity)
                {
                    throw new ConflictException(
                        $"Insufficient available stock for product '{item.ProductSku}'. Requested: {item.Quantity}, Available: {totalAvailable}.");
                }

                // Step 2: FEFO Logical Allocation
                // Sort batches by ExpiryDate ASC, then ProductionDate ASC, then Id ASC
                var fefoBatches = FefoAllocationHelper.SelectAndOrderEligibleBatches(eligibleBatches, today);

                int remaining = item.Quantity;
                foreach (var batch in fefoBatches)
                {
                    if (remaining <= 0) break;

                    int availableInBatch = batch.PhysicalQuantity - batch.ReservedQuantity;
                    if (availableInBatch <= 0) continue;

                    int allocateQty = Math.Min(remaining, availableInBatch);

                    batch.ReservedQuantity += allocateQty;
                    batch.UpdatedAtUtc = DateTime.UtcNow;

                    var reservation = new OrderReservation
                    {
                        OrderId = order.Id,
                        OrderItemId = item.Id,
                        InventoryBatchId = batch.Id,
                        Quantity = allocateQty,
                        Status = ReservationStatus.Active,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _dbContext.OrderReservations.Add(reservation);
                    remaining -= allocateQty;
                }
            }

            order.Status = OrderStatus.Confirmed;
            order.ConfirmedAtUtc = DateTime.UtcNow;
            order.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return OrderResponse.FromEntity(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                "Order or inventory was modified by another concurrent operation. Please reload and retry.");
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Available stock was depleted by another concurrent transaction.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<OrderResponse> CancelAsync(int id, CancelOrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CancellationReason))
        {
            throw new ValidationException("CancellationReason cannot be empty or whitespace.");
        }

        var normalizedReason = request.CancellationReason.Trim();
        if (normalizedReason.Length > 500)
        {
            throw new ValidationException("CancellationReason cannot exceed 500 characters.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Acquire row-level lock on the order row first to serialize concurrent cancellation/confirmation requests.
            // Lock Hierarchy / Order:
            // 1. Orders table: SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE
            // 2. InventoryBatches table: candidate batches locked via FOR UPDATE in canonical primary key order (Id ASC)
            // ConfirmAsync, CancelAsync, and FulfillAsync strictly follow this canonical Order -> Batch hierarchy, preventing cross-resource deadlocks.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE", ct);

            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new NotFoundException($"Order with ID {id} was not found.");

            // Cancellation is only allowed for Pending or Confirmed orders
            if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Completed)
            {
                throw new ConflictException(
                    $"Order '{order.OrderNumber}' cannot be cancelled because it is in status '{order.Status}'.");
            }

            if (request.RowVersion.HasValue)
            {
                _dbContext.Entry(order).Property(o => o.Version).OriginalValue = request.RowVersion.Value;
            }

            var now = DateTime.UtcNow;

            // If the order was Confirmed, atomically release all active reservations
            if (order.Status == OrderStatus.Confirmed)
            {
                var activeReservations = await _dbContext.OrderReservations
                    .Where(r => r.OrderId == order.Id && r.Status == ReservationStatus.Active)
                    .ToListAsync(ct);

                if (activeReservations.Count > 0)
                {
                    // Order batch rows deterministically by Id ASC matching canonical row-lock order
                    var batchIds = activeReservations
                        .Select(r => r.InventoryBatchId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();

                    var batches = await _dbContext.InventoryBatches
                        .FromSqlInterpolated($@"
                            SELECT *, ""xmin"" FROM ""InventoryBatches""
                            WHERE ""Id"" = ANY({batchIds})
                            ORDER BY ""Id"" ASC
                            FOR UPDATE")
                        .ToListAsync(ct);

                    var batchMap = batches.ToDictionary(b => b.Id);

                    foreach (var reservation in activeReservations)
                    {
                        if (!batchMap.TryGetValue(reservation.InventoryBatchId, out var batch))
                        {
                            throw new NotFoundException(
                                $"Inventory batch with ID {reservation.InventoryBatchId} was not found.");
                        }

                        if (batch.ReservedQuantity < reservation.Quantity)
                        {
                            throw new ConflictException(
                                $"Cannot release reservation of {reservation.Quantity} because batch '{batch.BatchNo}' has only {batch.ReservedQuantity} reserved.");
                        }

                        batch.ReservedQuantity -= reservation.Quantity;
                        batch.UpdatedAtUtc = now;

                        reservation.Status = ReservationStatus.Released;
                        reservation.ReleasedAtUtc = now;
                    }
                }
            }

            order.Status = OrderStatus.Cancelled;
            order.CancelledAtUtc = now;
            order.CancellationReason = normalizedReason;
            order.UpdatedAtUtc = now;

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return OrderResponse.FromEntity(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                "Order or inventory was modified by another concurrent operation. Please reload and retry.");
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Inventory reservation constraint violation occurred during cancellation.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<OrderResponse> FulfillAsync(int id, FulfillOrderRequest request, CancellationToken ct = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            // Acquire row-level lock on the order row first to serialize concurrent fulfillment/cancellation requests.
            // Lock Hierarchy / Order:
            // 1. Orders table: SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE
            // 2. InventoryBatches table: candidate batches locked via FOR UPDATE in canonical primary key order (Id ASC)
            // 3. Products table: associated products locked via FOR UPDATE with deterministic ordering (Id ASC)
            // ConfirmAsync, CancelAsync, and FulfillAsync strictly follow this canonical Order -> Batch -> Product hierarchy, preventing cross-resource deadlocks.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE", ct);

            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new NotFoundException($"Order with ID {id} was not found.");

            if (order.Status != OrderStatus.Confirmed)
            {
                throw new ConflictException(
                    $"Order '{order.OrderNumber}' cannot be fulfilled because it is in status '{order.Status}'.");
            }

            if (request?.RowVersion.HasValue == true)
            {
                _dbContext.Entry(order).Property(o => o.Version).OriginalValue = request.RowVersion.Value;
            }

            if (order.Items == null || order.Items.Count == 0)
            {
                throw new ValidationException("Order must contain at least one item.");
            }

            var orderItemMap = order.Items.ToDictionary(i => i.Id);

            var activeReservations = await _dbContext.OrderReservations
                .Where(r => r.OrderId == order.Id && r.Status == ReservationStatus.Active)
                .ToListAsync(ct);

            if (activeReservations.Count == 0)
            {
                throw new ConflictException(
                    $"Order '{order.OrderNumber}' has no active reservations to fulfill.");
            }

            if (activeReservations.Any(r => r.Quantity <= 0))
            {
                throw new ConflictException("Order reservations must have positive quantities.");
            }

            var invalidReservation = activeReservations.FirstOrDefault(r => !orderItemMap.ContainsKey(r.OrderItemId));
            if (invalidReservation != null)
            {
                throw new ConflictException(
                    $"Reservation {invalidReservation.Id} references an order item ({invalidReservation.OrderItemId}) that does not belong to order '{order.OrderNumber}'.");
            }

            var reservationsByItem = activeReservations
                .GroupBy(r => r.OrderItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var item in order.Items)
            {
                if (!reservationsByItem.TryGetValue(item.Id, out var itemReservations))
                {
                    throw new ConflictException(
                        $"Order item '{item.ProductSku}' (ID: {item.Id}) has no active reservations.");
                }

                int totalReservedForItem = itemReservations.Sum(r => r.Quantity);
                if (totalReservedForItem != item.Quantity)
                {
                    throw new ConflictException(
                        $"Reservation quantity mismatch for order item '{item.ProductSku}'. Ordered: {item.Quantity}, Reserved: {totalReservedForItem}.");
                }
            }

            // 5. Lock batches deterministically by Id ASC (Lock Hierarchy step 2, matching canonical row-lock order)
            var batchIds = activeReservations
                .Select(r => r.InventoryBatchId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            var batches = await _dbContext.InventoryBatches
                .FromSqlInterpolated($@"
                    SELECT *, ""xmin"" FROM ""InventoryBatches""
                    WHERE ""Id"" = ANY({batchIds})
                    ORDER BY ""Id"" ASC
                    FOR UPDATE")
                .ToListAsync(ct);

            var batchMap = batches.ToDictionary(b => b.Id);

            var missingBatchId = batchIds.FirstOrDefault(bId => !batchMap.ContainsKey(bId));
            if (missingBatchId != 0 && !batchMap.ContainsKey(missingBatchId))
            {
                throw new NotFoundException($"Inventory batch with ID {missingBatchId} was not found.");
            }

            foreach (var res in activeReservations)
            {
                var batch = batchMap[res.InventoryBatchId];
                var item = orderItemMap[res.OrderItemId];
                if (batch.ProductId != item.ProductId)
                {
                    throw new ConflictException(
                        $"Batch '{batch.BatchNo}' belongs to product ID {batch.ProductId}, but order item requires product ID {item.ProductId}.");
                }
            }

            var nonReleasedBatch = batches.FirstOrDefault(b => b.Status != BatchStatus.Released);
            if (nonReleasedBatch != null)
            {
                throw new ConflictException(
                    $"Batch '{nonReleasedBatch.BatchNo}' is in status '{nonReleasedBatch.Status}'. Only Released batches can be fulfilled.");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var expiredBatch = batches.FirstOrDefault(b => b.ExpiryDate.HasValue && b.ExpiryDate.Value < today);
            if (expiredBatch != null && expiredBatch.ExpiryDate.HasValue)
            {
                throw new ConflictException(
                    $"Batch '{expiredBatch.BatchNo}' has expired on {expiredBatch.ExpiryDate.Value:yyyy-MM-dd} and cannot be fulfilled.");
            }

            var requiredPerBatch = activeReservations
                .GroupBy(r => r.InventoryBatchId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

            foreach (var batch in batches)
            {
                int requiredQty = requiredPerBatch[batch.Id];
                if (batch.PhysicalQuantity < requiredQty)
                {
                    throw new ConflictException(
                        $"Batch '{batch.BatchNo}' has insufficient physical quantity ({batch.PhysicalQuantity}) to fulfill {requiredQty}.");
                }

                if (batch.ReservedQuantity < requiredQty)
                {
                    throw new ConflictException(
                        $"Batch '{batch.BatchNo}' has insufficient reserved quantity ({batch.ReservedQuantity}) to fulfill {requiredQty}.");
                }
            }

            var deductionsByProduct = activeReservations
                .GroupBy(r => orderItemMap[r.OrderItemId].ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

            var productIds = deductionsByProduct.Keys.OrderBy(pId => pId).ToArray();

            var products = await _dbContext.Products
                .FromSqlInterpolated($@"
                    SELECT *, ""xmin"" FROM ""Products""
                    WHERE ""Id"" = ANY({productIds})
                    ORDER BY ""Id"" ASC
                    FOR UPDATE")
                .ToListAsync(ct);

            var productMap = products.ToDictionary(p => p.Id);

            var missingProductId = productIds.FirstOrDefault(pId => !productMap.ContainsKey(pId));
            if (missingProductId != 0 && !productMap.ContainsKey(missingProductId))
            {
                throw new NotFoundException($"Product with ID {missingProductId} was not found.");
            }

            foreach (var product in products)
            {
                int totalDeduct = deductionsByProduct[product.Id];
                if (product.QuantityOnHand < totalDeduct)
                {
                    throw new ConflictException(
                        $"Product '{product.Sku}' has insufficient quantity on hand ({product.QuantityOnHand}) for deduction of {totalDeduct}.");
                }
            }

            var now = DateTime.UtcNow;
            var runningProductBalances = products.ToDictionary(p => p.Id, p => p.QuantityOnHand);

            // Deduct from batches and record StockMovement per batch (in FEFO order)
            foreach (var batch in batches.OrderBy(b => b.ExpiryDate).ThenBy(b => b.ProductionDate).ThenBy(b => b.Id))
            {
                int batchDeductQty = requiredPerBatch[batch.Id];

                batch.PhysicalQuantity -= batchDeductQty;
                batch.ReservedQuantity -= batchDeductQty;
                if (batch.PhysicalQuantity == 0)
                {
                    batch.Status = BatchStatus.Depleted;
                }
                batch.UpdatedAtUtc = now;

                runningProductBalances[batch.ProductId] -= batchDeductQty;

                var movement = new StockMovement
                {
                    ProductId = batch.ProductId,
                    MovementType = StockMovementType.OrderDeduction,
                    QuantityChange = -batchDeductQty,
                    BalanceAfter = runningProductBalances[batch.ProductId],
                    InventoryBatchId = batch.Id,
                    BatchBalanceAfter = batch.PhysicalQuantity,
                    ReferenceOrderId = order.Id,
                    Reason = $"Order {order.OrderNumber} fulfillment",
                    CreatedAtUtc = now
                };

                _dbContext.StockMovements.Add(movement);
            }

            // Deduct from product QuantityOnHand
            foreach (var product in products)
            {
                product.QuantityOnHand = runningProductBalances[product.Id];
                product.UpdatedAtUtc = now;
            }

            // Mark active reservations as Fulfilled
            foreach (var reservation in activeReservations)
            {
                reservation.Status = ReservationStatus.Fulfilled;
            }

            order.Status = OrderStatus.Completed;
            order.CompletedAtUtc = now;
            order.UpdatedAtUtc = now;

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return OrderResponse.FromEntity(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw new ConcurrencyConflictException(
                "Order or inventory was modified by another concurrent operation. Please reload and retry.");
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Inventory stock constraint violation occurred during fulfillment.");
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new ConflictException(
                "Order fulfillment stock movement constraint violation occurred. Order may have already been fulfilled.");
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
