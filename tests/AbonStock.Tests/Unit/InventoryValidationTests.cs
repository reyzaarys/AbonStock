using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Dtos.Inventory;
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
}
