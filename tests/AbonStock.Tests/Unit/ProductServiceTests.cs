using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Dtos.Products;
using Xunit;

namespace AbonStock.Tests.Unit;

public class ProductServiceTests
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void CreateProductRequest_WithNegativeUnitPrice_FailsValidation()
    {
        var request = new CreateProductRequest
        {
            Sku = "SKU-001",
            Name = "Test Product",
            UnitPrice = -5.00m
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProductRequest.UnitPrice)));
    }

    [Fact]
    public void CreateProductRequest_WithEmptySku_FailsValidation()
    {
        var request = new CreateProductRequest
        {
            Sku = "",
            Name = "Test Product",
            UnitPrice = 10.00m
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProductRequest.Sku)));
    }

    [Fact]
    public void CreateProductRequest_WithEmptyName_FailsValidation()
    {
        var request = new CreateProductRequest
        {
            Sku = "SKU-001",
            Name = "",
            UnitPrice = 10.00m
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateProductRequest.Name)));
    }

    [Fact]
    public void CreateProductRequest_WithValidData_PassesValidation()
    {
        var request = new CreateProductRequest
        {
            Sku = "SKU-001",
            Name = "Valid Product",
            Description = "A valid test product",
            UnitPrice = 19.99m
        };

        var errors = ValidateModel(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void UpdateProductRequest_WithMissingRowVersion_FailsValidation()
    {
        var request = new UpdateProductRequest
        {
            Name = "Updated Product",
            UnitPrice = 25.00m,
            RowVersion = null
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateProductRequest.RowVersion)));
    }

    [Fact]
    public void UpdateProductRequest_WithNegativeUnitPrice_FailsValidation()
    {
        var request = new UpdateProductRequest
        {
            Name = "Updated Product",
            UnitPrice = -1.00m,
            RowVersion = 1u
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateProductRequest.UnitPrice)));
    }

    [Fact]
    public void UpdateProductRequest_WithEmptyName_FailsValidation()
    {
        var request = new UpdateProductRequest
        {
            Name = "",
            UnitPrice = 25.00m,
            RowVersion = 1u
        };

        var errors = ValidateModel(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateProductRequest.Name)));
    }

    [Fact]
    public void ProductListQuery_WithInvalidPage_FailsValidation()
    {
        var query = new ProductListQuery
        {
            Page = 0,
            PageSize = 20
        };

        var errors = ValidateModel(query);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ProductListQuery.Page)));
    }

    [Fact]
    public void ProductListQuery_WithPageSizeExceeding100_FailsValidation()
    {
        var query = new ProductListQuery
        {
            Page = 1,
            PageSize = 101
        };

        var errors = ValidateModel(query);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(ProductListQuery.PageSize)));
    }
}
