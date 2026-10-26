using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Entities;
using Xunit;

namespace AbonStock.Tests.Unit;

public class InventoryValidationTests
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void StockReceiptRequest_WithZeroQuantity_FailsValidation()
    {
        var request = new StockReceiptRequest
        {
            ProductId = 1,
            BatchNo = "BATCH-001",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 0
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockReceiptRequest.Quantity)));
    }

    [Fact]
    public void StockReceiptRequest_WithNegativeQuantity_FailsValidation()
    {
        var request = new StockReceiptRequest
        {
            ProductId = 1,
            BatchNo = "BATCH-001",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = -50
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockReceiptRequest.Quantity)));
    }

    [Fact]
    public void StockReceiptRequest_WithEmptyBatchNo_FailsValidation()
    {
        var request = new StockReceiptRequest
        {
            ProductId = 1,
            BatchNo = "",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 100
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockReceiptRequest.BatchNo)));
    }

    [Fact]
    public void StockReceiptRequest_WithZeroProductId_FailsValidation()
    {
        var request = new StockReceiptRequest
        {
            ProductId = 0,
            BatchNo = "BATCH-001",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 100
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockReceiptRequest.ProductId)));
    }

    [Fact]
    public void StockReceiptRequest_WithValidData_PassesValidation()
    {
        var request = new StockReceiptRequest
        {
            ProductId = 1,
            BatchNo = "BATCH-001",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 100,
            Reason = "Initial stock arrival"
        };

        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void ReleaseBatchRequest_WithMissingRowVersion_FailsValidation()
    {
        var request = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = null
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ReleaseBatchRequest.RowVersion)));
    }

    [Fact]
    public void ReleaseBatchRequest_WithMissingQualityChecks_FailsValidation()
    {
        var request = new ReleaseBatchRequest
        {
            VisualCheckPassed = null,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = 1u
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ReleaseBatchRequest.VisualCheckPassed)));
    }

    [Fact]
    public void ReleaseBatchRequest_WithValidData_PassesValidation()
    {
        var request = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = 1u,
            Notes = "Passed all QA standards"
        };

        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void StockAdjustmentRequest_WithMissingBatchRowVersion_FailsValidation()
    {
        var request = new StockAdjustmentRequest
        {
            BatchId = 1,
            QuantityChange = -5,
            Reason = "Damaged during handling",
            BatchRowVersion = null
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockAdjustmentRequest.BatchRowVersion)));
    }

    [Fact]
    public void StockAdjustmentRequest_WithEmptyReason_FailsValidation()
    {
        var request = new StockAdjustmentRequest
        {
            BatchId = 1,
            QuantityChange = -5,
            Reason = "",
            BatchRowVersion = 1u
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockAdjustmentRequest.Reason)));
    }

    [Fact]
    public void StockAdjustmentRequest_WithZeroBatchId_FailsValidation()
    {
        var request = new StockAdjustmentRequest
        {
            BatchId = 0,
            QuantityChange = 10,
            Reason = "Physical count reconciliation",
            BatchRowVersion = 1u
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockAdjustmentRequest.BatchId)));
    }

    [Fact]
    public void StockAdjustmentRequest_WithValidData_PassesValidation()
    {
        var request = new StockAdjustmentRequest
        {
            BatchId = 1,
            QuantityChange = 10,
            Reason = "Physical count reconciliation",
            BatchRowVersion = 1u
        };

        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void StockMovementListQuery_WithInvalidPage_FailsValidation()
    {
        var query = new StockMovementListQuery
        {
            Page = 0,
            PageSize = 20
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockMovementListQuery.Page)));
    }

    [Fact]
    public void StockMovementListQuery_WithPageSizeExceeding100_FailsValidation()
    {
        var query = new StockMovementListQuery
        {
            Page = 1,
            PageSize = 101
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(StockMovementListQuery.PageSize)));
    }

    [Fact]
    public void BatchListQuery_WithInvalidPage_FailsValidation()
    {
        var query = new BatchListQuery
        {
            Page = 0,
            PageSize = 20
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(BatchListQuery.Page)));
    }

    [Fact]
    public void BatchListQuery_WithZeroPageSize_FailsValidation()
    {
        var query = new BatchListQuery
        {
            Page = 1,
            PageSize = 0
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(BatchListQuery.PageSize)));
    }

    [Fact]
    public void BatchListQuery_WithPageSizeExceeding100_FailsValidation()
    {
        var query = new BatchListQuery
        {
            Page = 1,
            PageSize = 101
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(BatchListQuery.PageSize)));
    }

    [Fact]
    public void BatchListQuery_WithValidParameters_PassesValidation()
    {
        var query = new BatchListQuery
        {
            Page = 1,
            PageSize = 25,
            ProductId = 2,
            Status = BatchStatus.Released,
            Search = "BAT-2026",
            SortBy = "productionDate",
            SortDescending = true
        };

        var errors = ValidateModel(query);
        Assert.Empty(errors);
    }

    [Fact]
    public void BatchResponse_FromEntity_WithProduct_MapsAllFieldsCorrectly()
    {
        var product = new Product
        {
            Id = 5,
            Sku = "ABON-SAPI-100",
            Name = "Abon Sapi 100g",
            UnitPrice = 35000m
        };

        var batch = new InventoryBatch
        {
            Id = 10,
            ProductId = 5,
            Product = product,
            BatchNo = "BAT-2026-001",
            ProductionDate = new DateOnly(2026, 10, 1),
            ExpiryDate = new DateOnly(2027, 4, 1),
            LocationId = "LOC_KDR_PROD",
            InitialQuantity = 100,
            PhysicalQuantity = 90,
            ReservedQuantity = 20,
            Status = BatchStatus.Released,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = 42
        };

        var response = BatchResponse.FromEntity(batch);

        Assert.Equal(10, response.Id);
        Assert.Equal(5, response.ProductId);
        Assert.Equal("ABON-SAPI-100", response.ProductSku);
        Assert.Equal("Abon Sapi 100g", response.ProductName);
        Assert.Equal("BAT-2026-001", response.BatchNo);
        Assert.Equal(new DateOnly(2026, 10, 1), response.ProductionDate);
        Assert.Equal(new DateOnly(2027, 4, 1), response.ExpiryDate);
        Assert.Equal("LOC_KDR_PROD", response.LocationId);
        Assert.Equal(100, response.InitialQuantity);
        Assert.Equal(90, response.PhysicalQuantity);
        Assert.Equal(20, response.ReservedQuantity);
        Assert.Equal(70, response.AvailableQuantity); // 90 - 20 = 70
        Assert.Equal("Released", response.Status);
        Assert.Equal(42u, response.Version);
    }

    [Fact]
    public void BatchResponse_FromEntity_WithoutProduct_HandlesNullProductSafely()
    {
        var batch = new InventoryBatch
        {
            Id = 11,
            ProductId = 6,
            Product = null!,
            BatchNo = "BAT-2026-002",
            ProductionDate = new DateOnly(2026, 10, 2),
            ExpiryDate = null,
            LocationId = "LOC_KDR_PROD",
            InitialQuantity = 50,
            PhysicalQuantity = 50,
            ReservedQuantity = 0,
            Status = BatchStatus.PendingRelease,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = 1
        };

        var response = BatchResponse.FromEntity(batch);

        Assert.Null(response.ProductSku);
        Assert.Null(response.ProductName);
        Assert.Equal(50, response.AvailableQuantity);
        Assert.Equal("PendingRelease", response.Status);
    }
}
