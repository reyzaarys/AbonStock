using AbonStock.Api.Common.Exceptions;
using AbonStock.Api.Entities;
using AbonStock.Api.Services;
using Xunit;

namespace AbonStock.Tests.Unit;

public class FefoAllocationTests
{
    private readonly DateOnly _today = new(2026, 10, 26);

    [Fact]
    public void Fefo_AllocatesEarlierExpiryBeforeEarlierProductionDate_FefoOverFifo()
    {
        // Batch A was produced earlier (Jan 2026), but expires later (Jun 2027)
        var batchA_OlderProduction = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-PROD-OLD",
            ProductionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 6, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        // Batch B was produced later (Feb 2026), but expires earlier (Jan 2027)
        var batchB_EarlierExpiry = new InventoryBatch
        {
            Id = 2,
            BatchNo = "BAT-EXP-EARLY",
            ProductionDate = new DateOnly(2026, 2, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        var batches = new List<InventoryBatch> { batchA_OlderProduction, batchB_EarlierExpiry };

        // Request 30 items
        var allocations = FefoAllocationHelper.AllocateFefo(batches, 30, _today);

        // FEFO invariant: Batch B must be allocated first despite being produced later than Batch A
        Assert.Single(allocations);
        Assert.Equal(batchB_EarlierExpiry.Id, allocations[0].Batch.Id);
        Assert.Equal(30, allocations[0].AllocatedQuantity);
    }

    [Fact]
    public void Fefo_TieBreaker1_AllocatesEarlierProductionDateWhenExpiryMatches()
    {
        // Both batches expire on same date (2027-06-01)
        var batch1_LaterProd = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-PROD-FEB",
            ProductionDate = new DateOnly(2026, 2, 1),
            ExpiryDate = new DateOnly(2027, 6, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 40,
            ReservedQuantity = 0
        };

        var batch2_EarlierProd = new InventoryBatch
        {
            Id = 2,
            BatchNo = "BAT-PROD-JAN",
            ProductionDate = new DateOnly(2026, 1, 15),
            ExpiryDate = new DateOnly(2027, 6, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 40,
            ReservedQuantity = 0
        };

        var batches = new List<InventoryBatch> { batch1_LaterProd, batch2_EarlierProd };

        var allocations = FefoAllocationHelper.AllocateFefo(batches, 25, _today);

        // Tie-breaker 1: Earlier production date takes priority
        Assert.Single(allocations);
        Assert.Equal(batch2_EarlierProd.Id, allocations[0].Batch.Id);
        Assert.Equal(25, allocations[0].AllocatedQuantity);
    }

    [Fact]
    public void Fefo_TieBreaker2_AllocatesLowerBatchIdWhenExpiryAndProductionDateMatch()
    {
        var batchHighId = new InventoryBatch
        {
            Id = 99,
            BatchNo = "BAT-HIGH-ID",
            ProductionDate = new DateOnly(2026, 3, 1),
            ExpiryDate = new DateOnly(2027, 3, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        var batchLowId = new InventoryBatch
        {
            Id = 5,
            BatchNo = "BAT-LOW-ID",
            ProductionDate = new DateOnly(2026, 3, 1),
            ExpiryDate = new DateOnly(2027, 3, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        var batches = new List<InventoryBatch> { batchHighId, batchLowId };

        var allocations = FefoAllocationHelper.AllocateFefo(batches, 20, _today);

        // Tie-breaker 2: Lower Id takes priority
        Assert.Single(allocations);
        Assert.Equal(batchLowId.Id, allocations[0].Batch.Id);
        Assert.Equal(20, allocations[0].AllocatedQuantity);
    }

    [Fact]
    public void Fefo_ExcludesBatchesWithNullExpiryDate()
    {
        // Batch with null ExpiryDate (e.g. traditional recipe Batch 3 BAT-202610-SO03)
        var batchNullExpiry = new InventoryBatch
        {
            Id = 3,
            BatchNo = "BAT-NULL-EXP",
            ProductionDate = new DateOnly(2026, 10, 15),
            ExpiryDate = null,
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        var eligible = FefoAllocationHelper.SelectAndOrderEligibleBatches(new[] { batchNullExpiry }, _today);
        Assert.Empty(eligible);

        // Attempting to allocate from only null-expiry batches throws ConflictException
        var ex = Assert.Throws<ConflictException>(() =>
            FefoAllocationHelper.AllocateFefo(new[] { batchNullExpiry }, 10, _today));
        Assert.Contains("Insufficient available stock", ex.Message);
    }

    [Fact]
    public void Fefo_ExcludesExpiredBatches()
    {
        var expiredYesterday = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-EXPIRED",
            ProductionDate = _today.AddMonths(-12),
            ExpiryDate = _today.AddDays(-1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 100,
            ReservedQuantity = 0
        };

        var eligible = FefoAllocationHelper.SelectAndOrderEligibleBatches(new[] { expiredYesterday }, _today);
        Assert.Empty(eligible);

        var ex = Assert.Throws<ConflictException>(() =>
            FefoAllocationHelper.AllocateFefo(new[] { expiredYesterday }, 10, _today));
        Assert.Contains("Insufficient available stock", ex.Message);
    }

    [Fact]
    public void Fefo_IncludesBatchExpiringToday()
    {
        var expiringToday = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-EXP-TODAY",
            ProductionDate = _today.AddMonths(-6),
            ExpiryDate = _today,
            Status = BatchStatus.Released,
            PhysicalQuantity = 30,
            ReservedQuantity = 0
        };

        var eligible = FefoAllocationHelper.SelectAndOrderEligibleBatches(new[] { expiringToday }, _today);
        Assert.Single(eligible);

        var allocations = FefoAllocationHelper.AllocateFefo(new[] { expiringToday }, 15, _today);
        Assert.Single(allocations);
        Assert.Equal(15, allocations[0].AllocatedQuantity);
    }

    [Theory]
    [InlineData(BatchStatus.PendingRelease)]
    [InlineData(BatchStatus.Quarantined)]
    [InlineData(BatchStatus.Depleted)]
    public void Fefo_ExcludesBatchesNotInReleasedStatus(BatchStatus nonReleasedStatus)
    {
        var batch = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-NON-RELEASED",
            ProductionDate = _today.AddDays(-10),
            ExpiryDate = _today.AddMonths(6),
            Status = nonReleasedStatus,
            PhysicalQuantity = 100,
            ReservedQuantity = 0
        };

        var eligible = FefoAllocationHelper.SelectAndOrderEligibleBatches(new[] { batch }, _today);
        Assert.Empty(eligible);
    }

    [Fact]
    public void Fefo_ExcludesBatchesWithNoAvailableStock()
    {
        var fullyReservedBatch = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-FULLY-RESERVED",
            ProductionDate = _today.AddDays(-10),
            ExpiryDate = _today.AddMonths(6),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 50 // Zero available
        };

        var eligible = FefoAllocationHelper.SelectAndOrderEligibleBatches(new[] { fullyReservedBatch }, _today);
        Assert.Empty(eligible);
    }

    [Fact]
    public void Fefo_SplitsAllocationAcrossMultipleBatchesInStrictFefoOrder()
    {
        var batch1_ExpiresFirst = new InventoryBatch
        {
            Id = 10,
            BatchNo = "BAT-EXP-1ST",
            ProductionDate = new DateOnly(2026, 5, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 20,
            ReservedQuantity = 0
        };

        var batch2_ExpiresSecond = new InventoryBatch
        {
            Id = 20,
            BatchNo = "BAT-EXP-2ND",
            ProductionDate = new DateOnly(2026, 4, 1),
            ExpiryDate = new DateOnly(2027, 2, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 25,
            ReservedQuantity = 5 // 20 available
        };

        var batch3_ExpiresThird = new InventoryBatch
        {
            Id = 30,
            BatchNo = "BAT-EXP-3RD",
            ProductionDate = new DateOnly(2026, 3, 1),
            ExpiryDate = new DateOnly(2027, 3, 1),
            Status = BatchStatus.Released,
            PhysicalQuantity = 50,
            ReservedQuantity = 0
        };

        var batches = new List<InventoryBatch> { batch3_ExpiresThird, batch1_ExpiresFirst, batch2_ExpiresSecond };

        // Request 35 items:
        // - Batch 1 (expires 2027-01-01): takes all 20 available
        // - Batch 2 (expires 2027-02-01): takes remaining 15 of 20 available
        // - Batch 3 (expires 2027-03-01): untouched (0)
        var allocations = FefoAllocationHelper.AllocateFefo(batches, 35, _today);

        Assert.Equal(2, allocations.Count);
        Assert.Equal(batch1_ExpiresFirst.Id, allocations[0].Batch.Id);
        Assert.Equal(20, allocations[0].AllocatedQuantity);

        Assert.Equal(batch2_ExpiresSecond.Id, allocations[1].Batch.Id);
        Assert.Equal(15, allocations[1].AllocatedQuantity);
    }

    [Fact]
    public void Fefo_ThrowsValidationException_WhenRequestedQuantityIsZeroOrNegative()
    {
        var batch = new InventoryBatch
        {
            Id = 1,
            BatchNo = "BAT-01",
            ProductionDate = _today,
            ExpiryDate = _today.AddMonths(6),
            Status = BatchStatus.Released,
            PhysicalQuantity = 10,
            ReservedQuantity = 0
        };

        Assert.Throws<ValidationException>(() => FefoAllocationHelper.AllocateFefo(new[] { batch }, 0, _today));
        Assert.Throws<ValidationException>(() => FefoAllocationHelper.AllocateFefo(new[] { batch }, -5, _today));
    }
}
