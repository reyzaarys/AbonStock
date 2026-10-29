using AbonStock.Api.Common.Exceptions;
using AbonStock.Api.Entities;

namespace AbonStock.Api.Services;

/// <summary>
/// Helper for First Expired, First Out (FEFO) inventory batch selection and stock allocation.
/// Encapsulates the core FEFO business invariants:
/// 1. Only Released batches are eligible.
/// 2. Batches without a valid ExpiryDate are strictly excluded from automated allocation.
/// 3. Batches where ExpiryDate is strictly before today are excluded.
/// 4. Batches where PhysicalQuantity &lt;= ReservedQuantity have zero available stock and are skipped.
/// 5. Ordering hierarchy:
///    a. ExpiryDate ASC (earliest expiration date allocated first)
///    b. ProductionDate ASC (earliest production date as primary tie-breaker)
///    c. Id ASC (deterministic secondary tie-breaker)
/// </summary>
public static class FefoAllocationHelper
{
    /// <summary>
    /// Filters and orders candidate batches according to FEFO business invariants.
    /// </summary>
    public static List<InventoryBatch> SelectAndOrderEligibleBatches(
        IEnumerable<InventoryBatch> batches,
        DateOnly today)
    {
        return batches
            .Where(b => b.Status == BatchStatus.Released
                        && b.ExpiryDate.HasValue
                        && b.ExpiryDate.Value >= today
                        && b.PhysicalQuantity > b.ReservedQuantity)
            .OrderBy(b => b.ExpiryDate!.Value)
            .ThenBy(b => b.ProductionDate)
            .ThenBy(b => b.Id)
            .ToList();
    }

    /// <summary>
    /// Allocates the requested quantity across candidate batches using FEFO ordering.
    /// Returns a list of tuples containing the batch and the allocated quantity.
    /// Throws ConflictException if total available stock across eligible batches is insufficient.
    /// </summary>
    public static List<(InventoryBatch Batch, int AllocatedQuantity)> AllocateFefo(
        IEnumerable<InventoryBatch> batches,
        int requestedQuantity,
        DateOnly today)
    {
        if (requestedQuantity <= 0)
        {
            throw new ValidationException("Requested allocation quantity must be greater than zero.");
        }

        var eligible = SelectAndOrderEligibleBatches(batches, today);
        int totalAvailable = eligible.Sum(b => b.PhysicalQuantity - b.ReservedQuantity);

        if (totalAvailable < requestedQuantity)
        {
            throw new ConflictException(
                $"Insufficient available stock. Requested: {requestedQuantity}, Available: {totalAvailable}.");
        }

        var allocations = new List<(InventoryBatch Batch, int AllocatedQuantity)>();
        int remaining = requestedQuantity;

        foreach (var batch in eligible)
        {
            if (remaining <= 0) break;

            int available = batch.PhysicalQuantity - batch.ReservedQuantity;
            if (available <= 0) continue;

            int allocateQty = Math.Min(remaining, available);
            allocations.Add((batch, allocateQty));
            remaining -= allocateQty;
        }

        return allocations;
    }
}
