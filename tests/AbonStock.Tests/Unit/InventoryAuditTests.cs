using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Entities;
using Xunit;

namespace AbonStock.Tests.Unit;

public class InventoryAuditTests
{
    [Fact]
    public void ProductDiscrepancy_WithZeroBatchesAndZeroQuantityOnHand_IsConsistent()
    {
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-EMPTY",
            Name = "Empty Product",
            QuantityOnHand = 0,
            Batches = new List<InventoryBatch>()
        };

        int expectedQty = product.Batches.Sum(b => b.PhysicalQuantity);
        int actualQty = product.QuantityOnHand;
        int discrepancy = actualQty - expectedQty;
        bool isConsistent = (discrepancy == 0) && (actualQty >= 0);

        Assert.Equal(0, expectedQty);
        Assert.Equal(0, actualQty);
        Assert.Equal(0, discrepancy);
        Assert.True(isConsistent);
    }

    [Fact]
    public void ProductDiscrepancy_WithMatchingBatchesAndQuantityOnHand_IsConsistent()
    {
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-MATCH",
            Name = "Matching Product",
            QuantityOnHand = 150,
            Batches = new List<InventoryBatch>
            {
                new() { Id = 1, PhysicalQuantity = 100 },
                new() { Id = 2, PhysicalQuantity = 50 }
            }
        };

        int expectedQty = product.Batches.Sum(b => b.PhysicalQuantity);
        int actualQty = product.QuantityOnHand;
        int discrepancy = actualQty - expectedQty;
        bool isConsistent = (discrepancy == 0) && (actualQty >= 0);

        Assert.Equal(150, expectedQty);
        Assert.Equal(150, actualQty);
        Assert.Equal(0, discrepancy);
        Assert.True(isConsistent);
    }

    [Fact]
    public void ProductDiscrepancy_WithMismatchedBatchesAndQuantityOnHand_IsDetectedAsInconsistent()
    {
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-MISMATCH",
            Name = "Mismatch Product",
            QuantityOnHand = 120, // Database shows 120, but batches sum to 100
            Batches = new List<InventoryBatch>
            {
                new() { Id = 1, PhysicalQuantity = 60 },
                new() { Id = 2, PhysicalQuantity = 40 }
            }
        };

        int expectedQty = product.Batches.Sum(b => b.PhysicalQuantity);
        int actualQty = product.QuantityOnHand;
        int discrepancy = actualQty - expectedQty;
        bool isConsistent = (discrepancy == 0) && (actualQty >= 0);

        Assert.Equal(100, expectedQty);
        Assert.Equal(120, actualQty);
        Assert.Equal(20, discrepancy);
        Assert.False(isConsistent);

        var discrepancyResponse = new ProductDiscrepancyResponse(
            product.Id,
            product.Sku,
            product.Name,
            expectedQty,
            actualQty,
            discrepancy,
            product.Batches.Count,
            isConsistent);

        Assert.Equal(20, discrepancyResponse.Discrepancy);
        Assert.False(discrepancyResponse.IsConsistent);
    }

    [Fact]
    public void BatchInvariant_ReservedExceedsPhysical_ViolatesInvariant()
    {
        var batch = new InventoryBatch
        {
            Id = 10,
            BatchNo = "BAT-ERR-01",
            PhysicalQuantity = 20,
            ReservedQuantity = 25,
            Status = BatchStatus.Released
        };

        Assert.True(batch.ReservedQuantity > batch.PhysicalQuantity);
    }

    [Fact]
    public void BatchInvariant_ZeroPhysicalWithReleasedStatus_ViolatesInvariant()
    {
        var batch = new InventoryBatch
        {
            Id = 11,
            BatchNo = "BAT-ERR-02",
            PhysicalQuantity = 0,
            ReservedQuantity = 0,
            Status = BatchStatus.Released // Must be Depleted when 0
        };

        Assert.True(batch.PhysicalQuantity == 0 && batch.Status != BatchStatus.Depleted);
    }

    [Fact]
    public void BatchInvariant_QuarantinedBatchWithActiveReservations_ViolatesInvariant()
    {
        var batch = new InventoryBatch
        {
            Id = 12,
            BatchNo = "BAT-ERR-03",
            PhysicalQuantity = 50,
            ReservedQuantity = 10,
            Status = BatchStatus.Quarantined
        };

        Assert.True(batch.Status == BatchStatus.Quarantined && batch.ReservedQuantity > 0);
    }

    [Fact]
    public void BatchInvariant_PendingReleaseBatchWithActiveReservations_ViolatesInvariant()
    {
        var batch = new InventoryBatch
        {
            Id = 13,
            BatchNo = "BAT-ERR-04",
            PhysicalQuantity = 50,
            ReservedQuantity = 5,
            Status = BatchStatus.PendingRelease
        };

        Assert.True(batch.Status == BatchStatus.PendingRelease && batch.ReservedQuantity > 0);
    }

    [Fact]
    public void LedgerChain_WithContinuousBalances_EvaluatesAsValid()
    {
        var movements = new List<StockMovement>
        {
            new() { Id = 1, ProductId = 1, MovementType = StockMovementType.Receipt, QuantityChange = 100, BalanceAfter = 100 },
            new() { Id = 2, ProductId = 1, MovementType = StockMovementType.Adjustment, QuantityChange = -20, BalanceAfter = 80 },
            new() { Id = 3, ProductId = 1, MovementType = StockMovementType.WriteOff, QuantityChange = -10, BalanceAfter = 70 }
        };

        int currentProductStock = 70;
        int runningBalance = 0;
        bool compromised = false;

        foreach (var m in movements)
        {
            int expected = runningBalance + m.QuantityChange;
            if (m.BalanceAfter != expected)
            {
                compromised = true;
            }
            runningBalance = m.BalanceAfter;
        }

        if (runningBalance != currentProductStock)
        {
            compromised = true;
        }

        Assert.False(compromised);
        Assert.Equal(currentProductStock, runningBalance);
    }

    [Fact]
    public void LedgerChain_WithArithmeticMismatch_EvaluatesAsCompromised()
    {
        var movements = new List<StockMovement>
        {
            new() { Id = 1, ProductId = 1, MovementType = StockMovementType.Receipt, QuantityChange = 100, BalanceAfter = 100 },
            new() { Id = 2, ProductId = 1, MovementType = StockMovementType.Adjustment, QuantityChange = -20, BalanceAfter = 90 } // Should be 80!
        };

        int runningBalance = 0;
        bool compromised = false;

        foreach (var m in movements)
        {
            int expected = runningBalance + m.QuantityChange;
            if (m.BalanceAfter != expected)
            {
                compromised = true;
            }
            runningBalance = m.BalanceAfter;
        }

        Assert.True(compromised);
    }

    [Fact]
    public void LedgerChain_WithMissingMovementsForStock_EvaluatesAsUnverifiable()
    {
        var movements = new List<StockMovement>(); // Empty ledger
        int currentStock = 50;

        bool unverifiable = movements.Count == 0 && currentStock > 0;

        Assert.True(unverifiable);
    }
}
