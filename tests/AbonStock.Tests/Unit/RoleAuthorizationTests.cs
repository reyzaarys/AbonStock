using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Dtos.Orders;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AbonStock.Tests.Unit;

public class RoleAuthorizationTests : IClassFixture<RoleAuthorizationTests.RoleAuthTestFactory>
{
    private const string ValidSecretKey = "test-signing-key-for-role-auth-testing-purposes-32b";
    private const string ValidIssuer = "AbonStock.Api";
    private const string ValidAudience = "AbonStock.Frontend";

    private readonly RoleAuthTestFactory _factory;

    public RoleAuthorizationTests(RoleAuthTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithRole(string role, string username = "testuser")
    {
        var token = CreateTestToken(role, username);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string CreateTestToken(string role, string username)
    {
        var now = DateTime.UtcNow;
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, $"usr_{username}"),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    #region 401 Unauthorized for Unauthenticated Requests

    [Theory]
    [InlineData("GET", "/api/products")]
    [InlineData("POST", "/api/products")]
    [InlineData("PUT", "/api/products/1")]
    [InlineData("PATCH", "/api/products/1/deactivate")]
    [InlineData("GET", "/api/inventory/batches")]
    [InlineData("POST", "/api/inventory/receipts")]
    [InlineData("POST", "/api/inventory/batches/1/release")]
    [InlineData("POST", "/api/inventory/batches/1/quarantine")]
    [InlineData("POST", "/api/inventory/batches/1/write-off")]
    [InlineData("POST", "/api/inventory/adjustments")]
    [InlineData("GET", "/api/inventory/audit")]
    [InlineData("GET", "/api/orders")]
    [InlineData("POST", "/api/orders")]
    [InlineData("POST", "/api/orders/1/confirm")]
    [InlineData("POST", "/api/orders/1/cancel")]
    [InlineData("POST", "/api/orders/1/fulfill")]
    public async Task Unauthenticated_RequestsToBusinessEndpoints_ReturnUnauthorized401(string method, string url)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region 403 Forbidden for Staff on Administrative Endpoints

    [Fact]
    public async Task StaffRole_CannotCall_PostProducts_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var payload = new CreateProductRequest
        {
            Sku = "ABON-SAPI-50G",
            Name = "Abon Sapi 50g",
            UnitPrice = 20000
        };

        var response = await client.PostAsJsonAsync("/api/products", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CannotCall_PutProducts_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var payload = new UpdateProductRequest
        {
            Name = "Abon Sapi 50g Updated",
            UnitPrice = 25000,
            RowVersion = 1
        };

        var response = await client.PutAsJsonAsync("/api/products/1", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CannotCall_PatchDeactivateProduct_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var response = await client.PatchAsync("/api/products/1/deactivate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CannotCall_PostInventoryAdjustment_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var payload = new StockAdjustmentRequest
        {
            BatchId = 1,
            QuantityChange = -5,
            Reason = "Damaged during packaging",
            BatchRowVersion = 1
        };

        var response = await client.PostAsJsonAsync("/api/inventory/adjustments", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CannotCall_PostBatchWriteOff_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var payload = new WriteOffBatchRequest
        {
            Quantity = 10,
            Reason = "Pest contamination write-off",
            BatchRowVersion = 1
        };

        var response = await client.PostAsJsonAsync("/api/inventory/batches/1/write-off", payload);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CannotCall_GetInventoryAudit_ReturnsForbidden403()
    {
        using var client = CreateClientWithRole("Staff");

        var response = await client.GetAsync("/api/inventory/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Allowed Access for Staff on Operational Endpoints

    [Fact]
    public async Task StaffRole_CanCall_GetProductsListAndDetail_ReturnsSuccess200()
    {
        using var client = CreateClientWithRole("Staff");

        var listResp = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        var detailResp = await client.GetAsync("/api/products/1");
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CanCall_InventoryQueriesReceiptAndRelease_ReturnsSuccess()
    {
        using var client = CreateClientWithRole("Staff");

        // GET batches
        var batchesResp = await client.GetAsync("/api/inventory/batches");
        Assert.Equal(HttpStatusCode.OK, batchesResp.StatusCode);

        // GET movements
        var movementsResp = await client.GetAsync("/api/inventory/movements");
        Assert.Equal(HttpStatusCode.OK, movementsResp.StatusCode);

        // POST receipt
        var receiptPayload = new StockReceiptRequest
        {
            ProductId = 1,
            BatchNo = "BAT-202610-TEST",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 100
        };
        var receiptResp = await client.PostAsJsonAsync("/api/inventory/receipts", receiptPayload);
        Assert.Equal(HttpStatusCode.Created, receiptResp.StatusCode);

        // POST release
        var releasePayload = new ReleaseBatchRequest
        {
            RowVersion = 1,
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true
        };
        var releaseResp = await client.PostAsJsonAsync("/api/inventory/batches/1/release", releasePayload);
        Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);

        // POST quarantine batch
        var quarantinePayload = new QuarantineBatchRequest
        {
            RowVersion = 1,
            Reason = "Suspected moisture damage"
        };
        var quarantineResp = await client.PostAsJsonAsync("/api/inventory/batches/1/quarantine", quarantinePayload);
        Assert.Equal(HttpStatusCode.OK, quarantineResp.StatusCode);
    }

    [Fact]
    public async Task StaffRole_CanCall_OrderWorkflows_ReturnsSuccess()
    {
        using var client = CreateClientWithRole("Staff");

        // GET orders
        var ordersResp = await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.OK, ordersResp.StatusCode);

        // GET order detail
        var detailResp = await client.GetAsync("/api/orders/1");
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);

        // POST create order
        var createPayload = new CreateOrderRequest
        {
            CustomerName = "Toko Berkah",
            Items = [new CreateOrderItemRequest { ProductId = 1, Quantity = 5 }]
        };
        var createResp = await client.PostAsJsonAsync("/api/orders", createPayload);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // POST confirm order
        var confirmResp = await client.PostAsJsonAsync("/api/orders/1/confirm", new ConfirmOrderRequest { RowVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // POST cancel order
        var cancelResp = await client.PostAsJsonAsync("/api/orders/1/cancel", new CancelOrderRequest { CancellationReason = "Customer request", RowVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // POST fulfill order
        var fulfillResp = await client.PostAsJsonAsync("/api/orders/1/fulfill", new FulfillOrderRequest { RowVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);
    }

    #endregion

    #region Allowed Access for Admin on Administrative and Operational Endpoints

    [Fact]
    public async Task AdminRole_CanCall_AdministrativeEndpoints_ReturnsSuccess()
    {
        using var client = CreateClientWithRole("Admin");

        // POST products
        var createPayload = new CreateProductRequest
        {
            Sku = "ABON-SAPI-100G",
            Name = "Abon Sapi 100g",
            UnitPrice = 35000
        };
        var createResp = await client.PostAsJsonAsync("/api/products", createPayload);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // PUT products
        var updatePayload = new UpdateProductRequest
        {
            Name = "Abon Sapi 100g Updated",
            UnitPrice = 37000,
            RowVersion = 1
        };
        var updateResp = await client.PutAsJsonAsync("/api/products/1", updatePayload);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        // PATCH deactivate
        var deactivateResp = await client.PatchAsync("/api/products/1/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateResp.StatusCode);

        // POST adjustment
        var adjustPayload = new StockAdjustmentRequest
        {
            BatchId = 1,
            QuantityChange = -2,
            Reason = "Admin inventory count correction",
            BatchRowVersion = 1
        };
        var adjustResp = await client.PostAsJsonAsync("/api/inventory/adjustments", adjustPayload);
        Assert.Equal(HttpStatusCode.OK, adjustResp.StatusCode);

        // POST write-off
        var writeOffPayload = new WriteOffBatchRequest
        {
            Quantity = 5,
            Reason = "Admin approved disposal",
            DisposalDocumentNumber = "DISP-202610-001",
            BatchRowVersion = 1
        };
        var writeOffResp = await client.PostAsJsonAsync("/api/inventory/batches/1/write-off", writeOffPayload);
        Assert.Equal(HttpStatusCode.OK, writeOffResp.StatusCode);

        // GET inventory audit
        var auditResp = await client.GetAsync("/api/inventory/audit");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
    }

    [Fact]
    public async Task AdminRole_CanAlsoCall_OperationalEndpoints_ReturnsSuccess()
    {
        using var client = CreateClientWithRole("Admin");

        var prodResp = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, prodResp.StatusCode);

        var batchesResp = await client.GetAsync("/api/inventory/batches");
        Assert.Equal(HttpStatusCode.OK, batchesResp.StatusCode);

        var ordersResp = await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.OK, ordersResp.StatusCode);

        var quarantineResp = await client.PostAsJsonAsync("/api/inventory/batches/1/quarantine", new QuarantineBatchRequest
        {
            RowVersion = 1,
            Reason = "Admin quarantine check"
        });
        Assert.Equal(HttpStatusCode.OK, quarantineResp.StatusCode);
    }

    #endregion

    public class RoleAuthTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:SecretKey", ValidSecretKey);
            builder.UseSetting("Jwt:Issuer", ValidIssuer);
            builder.UseSetting("Jwt:Audience", ValidAudience);

            builder.ConfigureServices(services =>
            {
                // Substitute all business services with in-memory dummy implementations so ZERO database queries are run
                services.Remove(services.Single(d => d.ServiceType == typeof(IProductService)));
                services.AddScoped<IProductService, DummyProductService>();

                services.Remove(services.Single(d => d.ServiceType == typeof(IInventoryService)));
                services.AddScoped<IInventoryService, DummyInventoryService>();

                services.Remove(services.Single(d => d.ServiceType == typeof(IOrderService)));
                services.AddScoped<IOrderService, DummyOrderService>();
            });
        }
    }

    private class DummyProductService : IProductService
    {
        public Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProductResponse(1, request.Sku, request.Name, request.Description, request.UnitPrice, 0, true, DateTime.UtcNow, DateTime.UtcNow, 1));
        public Task<ProductResponse> DeactivateAsync(int id, CancellationToken ct = default)
            => Task.FromResult(new ProductResponse(id, "SKU-01", "Name", null, 10000, 0, false, DateTime.UtcNow, DateTime.UtcNow, 2));
        public Task<ProductResponse> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(new ProductResponse(id, "SKU-01", "Name", null, 10000, 0, true, DateTime.UtcNow, DateTime.UtcNow, 1));
        public Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ProductResponse>(Array.Empty<ProductResponse>(), 1, 20, 0));
        public Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProductResponse(id, "SKU-01", request.Name, request.Description, request.UnitPrice, 0, true, DateTime.UtcNow, DateTime.UtcNow, 2));
    }

    private class DummyInventoryService : IInventoryService
    {
        public Task<StockReceiptResponse> ReceiveStockAsync(StockReceiptRequest request, CancellationToken ct = default)
            => Task.FromResult(new StockReceiptResponse(1, request.ProductId, request.BatchNo, request.ProductionDate, request.ExpiryDate, "LOC_01", request.Quantity, request.Quantity, "PendingRelease", 100, 1, 1, DateTime.UtcNow));
        public Task<BatchResponse> ReleaseBatchAsync(int batchId, ReleaseBatchRequest request, CancellationToken ct = default)
            => Task.FromResult(new BatchResponse(batchId, 1, "SKU-01", "Name", "BAT-01", DateOnly.FromDateTime(DateTime.UtcNow), null, "LOC_01", 100, 100, 0, 100, "Released", DateTime.UtcNow, DateTime.UtcNow, 2));
        public Task<BatchResponse> QuarantineBatchAsync(int batchId, QuarantineBatchRequest request, CancellationToken ct = default)
            => Task.FromResult(new BatchResponse(batchId, 1, "SKU-01", "Name", "BAT-01", DateOnly.FromDateTime(DateTime.UtcNow), null, "LOC_01", 100, 100, 0, 100, "Quarantined", DateTime.UtcNow, DateTime.UtcNow, 2));
        public Task<WriteOffBatchResponse> WriteOffBatchAsync(int batchId, WriteOffBatchRequest request, string? actor = null, CancellationToken ct = default)
            => Task.FromResult(new WriteOffBatchResponse(batchId, 1, "BAT-01", request.Quantity, 100, 100 - request.Quantity, 200, 200 - request.Quantity, "Quarantined", 1, 2, 2, DateTime.UtcNow));
        public Task<StockAdjustmentResponse> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct = default)
            => Task.FromResult(new StockAdjustmentResponse(request.BatchId, 1, 100, 100 + request.QuantityChange, 200, 200 + request.QuantityChange, "Released", 2, 2, DateTime.UtcNow));
        public Task<PagedResult<BatchResponse>> GetBatchesAsync(BatchListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<BatchResponse>(Array.Empty<BatchResponse>(), 1, 20, 0));
        public Task<BatchResponse> GetBatchByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(new BatchResponse(id, 1, "SKU-01", "Name", "BAT-01", DateOnly.FromDateTime(DateTime.UtcNow), null, "LOC_01", 100, 100, 0, 100, "Released", DateTime.UtcNow, DateTime.UtcNow, 1));
        public Task<PagedResult<StockMovementResponse>> GetMovementsAsync(StockMovementListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<StockMovementResponse>(Array.Empty<StockMovementResponse>(), 1, 20, 0));
        public Task<InventoryAuditResponse> AuditInventoryAsync(int? productId = null, CancellationToken ct = default)
            => Task.FromResult(new InventoryAuditResponse(
                DateTime.UtcNow,
                1, 1, 0,
                Array.Empty<ProductDiscrepancyResponse>(),
                Array.Empty<BatchInvariantViolationResponse>(),
                new LedgerAuditResponse("Valid", 0, Array.Empty<string>(), Array.Empty<string>(), "Valid"),
                true));
    }

    private class DummyOrderService : IOrderService
    {
        public Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
            => Task.FromResult(new OrderResponse(1, "ORD-TEST-001", request.CustomerName, request.CustomerEmail, "Pending", 100000, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, 1, []));
        public Task<OrderResponse> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(new OrderResponse(id, "ORD-TEST-001", "Customer", null, "Pending", 100000, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, 1, []));
        public Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<OrderResponse>(Array.Empty<OrderResponse>(), 1, 20, 0));
        public Task<OrderResponse> ConfirmAsync(int id, ConfirmOrderRequest request, CancellationToken ct = default)
            => Task.FromResult(new OrderResponse(id, "ORD-TEST-001", "Customer", null, "Confirmed", 100000, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, null, null, 2, []));
        public Task<OrderResponse> CancelAsync(int id, CancelOrderRequest request, CancellationToken ct = default)
            => Task.FromResult(new OrderResponse(id, "ORD-TEST-001", "Customer", null, "Cancelled", 100000, DateTime.UtcNow, DateTime.UtcNow, null, DateTime.UtcNow, request.CancellationReason, null, 3, []));
        public Task<OrderResponse> FulfillAsync(int id, FulfillOrderRequest request, CancellationToken ct = default)
            => Task.FromResult(new OrderResponse(id, "ORD-TEST-001", "Customer", null, "Completed", 100000, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, null, DateTime.UtcNow, 4, []));
    }
}
