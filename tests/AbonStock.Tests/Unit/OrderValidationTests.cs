using System.ComponentModel.DataAnnotations;
using AbonStock.Api.Dtos.Orders;
using Xunit;

namespace AbonStock.Tests.Unit;

public class OrderValidationTests
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void CreateOrderRequest_WithEmptyCustomerName_FailsValidation()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "",
            CustomerEmail = "test@example.com",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 2 }
            }
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderRequest.CustomerName)));
    }

    [Fact]
    public void CreateOrderRequest_WithInvalidEmail_FailsValidation()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Valid Name",
            CustomerEmail = "not-an-email",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 2 }
            }
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderRequest.CustomerEmail)));
    }

    [Fact]
    public void CreateOrderRequest_WithEmptyItems_FailsValidation()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Valid Customer",
            Items = new List<CreateOrderItemRequest>()
        };

        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderRequest.Items)));
    }

    [Fact]
    public void CreateOrderItemRequest_WithZeroQuantity_FailsValidation()
    {
        var item = new CreateOrderItemRequest
        {
            ProductId = 1,
            Quantity = 0
        };

        var errors = ValidateModel(item);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderItemRequest.Quantity)));
    }

    [Fact]
    public void CreateOrderItemRequest_WithNegativeQuantity_FailsValidation()
    {
        var item = new CreateOrderItemRequest
        {
            ProductId = 1,
            Quantity = -5
        };

        var errors = ValidateModel(item);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderItemRequest.Quantity)));
    }

    [Fact]
    public void CreateOrderItemRequest_WithZeroProductId_FailsValidation()
    {
        var item = new CreateOrderItemRequest
        {
            ProductId = 0,
            Quantity = 5
        };

        var errors = ValidateModel(item);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateOrderItemRequest.ProductId)));
    }

    [Fact]
    public void CreateOrderRequest_WithValidData_PassesValidation()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Alice Doe",
            CustomerEmail = "alice@example.com",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = 1, Quantity = 3 },
                new() { ProductId = 2, Quantity = 1 }
            }
        };

        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void OrderListQuery_WithInvalidPage_FailsValidation()
    {
        var query = new OrderListQuery
        {
            Page = 0,
            PageSize = 20
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(OrderListQuery.Page)));
    }

    [Fact]
    public void OrderListQuery_WithPageSizeExceeding100_FailsValidation()
    {
        var query = new OrderListQuery
        {
            Page = 1,
            PageSize = 101
        };

        var errors = ValidateModel(query);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(OrderListQuery.PageSize)));
    }

    [Fact]
    public void ConfirmOrderRequest_WithValidRowVersion_PassesValidation()
    {
        var request = new ConfirmOrderRequest { RowVersion = 12345 };
        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void ConfirmOrderRequest_WithNullRowVersion_PassesValidation()
    {
        var request = new ConfirmOrderRequest { RowVersion = null };
        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void CancelOrderRequest_WithValidReason_PassesValidation()
    {
        var request = new CancelOrderRequest
        {
            CancellationReason = "Customer requested cancellation before shipment",
            RowVersion = 100
        };
        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void CancelOrderRequest_WithEmptyReason_FailsValidation()
    {
        var request = new CancelOrderRequest
        {
            CancellationReason = ""
        };
        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CancelOrderRequest.CancellationReason)));
    }

    [Fact]
    public void CancelOrderRequest_WithReasonExceeding500Characters_FailsValidation()
    {
        var request = new CancelOrderRequest
        {
            CancellationReason = new string('A', 501)
        };
        var errors = ValidateModel(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CancelOrderRequest.CancellationReason)));
    }

    [Fact]
    public void FulfillOrderRequest_WithValidRowVersion_PassesValidation()
    {
        var request = new FulfillOrderRequest { RowVersion = 99999 };
        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void FulfillOrderRequest_WithNullRowVersion_PassesValidation()
    {
        var request = new FulfillOrderRequest { RowVersion = null };
        var errors = ValidateModel(request);
        Assert.Empty(errors);
    }

    [Fact]
    public void OrderResponse_FromEntity_MapsCompletedAtUtcCorrectly()
    {
        var completedTime = DateTime.UtcNow;
        var order = new Api.Entities.Order
        {
            Id = 42,
            OrderNumber = "ORD-TEST-123",
            CustomerName = "Test Customer",
            Status = Api.Entities.OrderStatus.Completed,
            CompletedAtUtc = completedTime
        };

        var response = OrderResponse.FromEntity(order);

        Assert.Equal(42, response.Id);
        Assert.Equal("Completed", response.Status);
        Assert.Equal(completedTime, response.CompletedAtUtc);
    }
}
