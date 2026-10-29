using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbonStock.Api.Common;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Orders;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Integration;

public class OrderIntegrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();
    private readonly PostgresTestFixture _fixture = fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task<ProductResponse> CreateTestProductAsync(decimal price, bool isActive = true, string prefix = "ORD-TEST")
    {
        var request = new CreateProductRequest
        {
            Sku = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Order Test Product",
            UnitPrice = price
        };

        var response = await _client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(product);

        if (!isActive)
        {
            var deactResp = await _client.PatchAsync($"/api/products/{product.Id}/deactivate", null);
            Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);
        }

        return product;
    }

    [Fact]
    public async Task CreateOrder_WithValidItems_CalculatesPricesAndTotalsServerSideAndPersistsAsPending()
    {
        var product1 = await CreateTestProductAsync(25.50m, isActive: true, "ORD-CALC-1");
        var product2 = await CreateTestProductAsync(40.00m, isActive: true, "ORD-CALC-2");

        var request = new CreateOrderRequest
        {
            CustomerName = "Budi Santoso",
            CustomerEmail = "budi@example.com",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product1.Id, Quantity = 3 },
                new() { ProductId = product2.Id, Quantity = 2 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);
        Assert.True(order.Id > 0);
        Assert.StartsWith("ORD-", order.OrderNumber);
        Assert.Equal("Budi Santoso", order.CustomerName);
        Assert.Equal("budi@example.com", order.CustomerEmail);
        Assert.Equal("Pending", order.Status);
        Assert.Null(order.ConfirmedAtUtc);
        Assert.Null(order.CancelledAtUtc);
        Assert.True(order.Version > 0);

        // Verify server-side price calculation
        Assert.Equal(2, order.Items.Count);

        var item1 = order.Items.First(i => i.ProductId == product1.Id);
        Assert.Equal(3, item1.Quantity);
        Assert.Equal(25.50m, item1.UnitPrice);
        Assert.Equal(76.50m, item1.LineTotal); // 3 * 25.50 = 76.50

        var item2 = order.Items.First(i => i.ProductId == product2.Id);
        Assert.Equal(2, item2.Quantity);
        Assert.Equal(40.00m, item2.UnitPrice);
        Assert.Equal(80.00m, item2.LineTotal); // 2 * 40.00 = 80.00

        Assert.Equal(156.50m, order.TotalAmount); // 76.50 + 80.00 = 156.50

        // Verify GET /api/orders/{id}
        var getResponse = await _client.GetAsync($"/api/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(order.Id, fetched.Id);
        Assert.Equal(order.OrderNumber, fetched.OrderNumber);
        Assert.Equal(order.TotalAmount, fetched.TotalAmount);
        Assert.Equal(2, fetched.Items.Count);
    }

    [Fact]
    public async Task CreateOrder_WithEmptyItems_ReturnsBadRequestProblemDetails()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Empty Items Customer",
            Items = new List<CreateOrderItemRequest>()
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task CreateOrder_WithDuplicateProductInSameOrder_ReturnsBadRequestProblemDetails()
    {
        var product = await CreateTestProductAsync(15.00m, isActive: true, "ORD-DUP");

        var request = new CreateOrderRequest
        {
            CustomerName = "Duplicate Item Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 2 },
                new() { ProductId = product.Id, Quantity = 3 } // Duplicate ProductId
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Duplicate product ID", problem.Detail);
    }

    [Fact]
    public async Task CreateOrder_WithInactiveProduct_ReturnsBadRequestProblemDetails()
    {
        var inactiveProduct = await CreateTestProductAsync(20.00m, isActive: false, "ORD-INACT");

        var request = new CreateOrderRequest
        {
            CustomerName = "Inactive Product Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = inactiveProduct.Id, Quantity = 1 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("inactive product", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOrder_WithNonExistentProduct_ReturnsNotFoundProblemDetails()
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Missing Product Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = 999999, Quantity = 1 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("was not found", problem.Detail);
    }

    [Fact]
    public async Task CreateOrder_WithZeroOrNegativeQuantity_ReturnsBadRequestProblemDetails()
    {
        var product = await CreateTestProductAsync(10.00m, isActive: true, "ORD-NEGQTY");

        var request = new CreateOrderRequest
        {
            CustomerName = "Zero Qty Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 0 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOrderById_WithNonExistentId_ReturnsNotFoundProblemDetails()
    {
        var response = await _client.GetAsync("/api/orders/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal("Not Found", problem.Title);
    }

    [Fact]
    public async Task ListOrders_ReturnsPagedResultWithDeterministicOrdering()
    {
        var product = await CreateTestProductAsync(12.00m, isActive: true, "ORD-LIST");

        // Create 3 orders with distinctive customer names
        var uniqueTag = Guid.NewGuid().ToString("N")[..6];
        for (int i = 1; i <= 3; i++)
        {
            var req = new CreateOrderRequest
            {
                CustomerName = $"ListCustomer-{uniqueTag}-{i}",
                Items = new List<CreateOrderItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = i }
                }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", req);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        }

        // Query with customerSearch filter
        var listResponse = await _client.GetAsync($"/api/orders?customerSearch={uniqueTag}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var pagedResult = await listResponse.Content.ReadFromJsonAsync<PagedResult<OrderResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
        Assert.Equal(3, pagedResult.TotalCount);
        Assert.Equal(2, pagedResult.Items.Count);
        Assert.True(pagedResult.HasNextPage);
        Assert.False(pagedResult.HasPreviousPage);

        // Verify deterministic descending order (newest created first)
        Assert.Contains("ListCustomer-" + uniqueTag + "-3", pagedResult.Items[0].CustomerName);
        Assert.Contains("ListCustomer-" + uniqueTag + "-2", pagedResult.Items[1].CustomerName);

        // Query second page
        var page2Resp = await _client.GetAsync($"/api/orders?customerSearch={uniqueTag}&page=2&pageSize=2");
        var page2Result = await page2Resp.Content.ReadFromJsonAsync<PagedResult<OrderResponse>>(JsonOptions);
        Assert.NotNull(page2Result);
        Assert.Single(page2Result.Items);
        Assert.Contains("ListCustomer-" + uniqueTag + "-1", page2Result.Items[0].CustomerName);
    }

    [Fact]
    public async Task CreateOrder_DoesNotModifyStockOrReservationsOrMovements()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        // Set up product and batch directly
        var product = new Product
        {
            Sku = $"ORD-STOCK-CHECK-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Stock Integrity Product",
            UnitPrice = 50.00m,
            QuantityOnHand = 100,
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-STK-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 100,
            PhysicalQuantity = 100,
            ReservedQuantity = 0,
            Status = BatchStatus.Released
        };
        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync();

        var initialMovementsCount = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);
        var initialReservationsCount = await db.OrderReservations.CountAsync();

        // Create Order via API
        var createRequest = new CreateOrderRequest
        {
            CustomerName = "Integrity Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 25 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", createRequest);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdOrder = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(createdOrder);

        // Verify in database that stock, batch, reservations, and movements are completely untouched
        var freshProduct = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id);
        Assert.NotNull(freshProduct);
        Assert.Equal(100, freshProduct.QuantityOnHand); // Unchanged

        var freshBatch = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.Id);
        Assert.NotNull(freshBatch);
        Assert.Equal(100, freshBatch.PhysicalQuantity); // Unchanged
        Assert.Equal(0, freshBatch.ReservedQuantity); // Unchanged

        var postMovementsCount = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);
        Assert.Equal(initialMovementsCount, postMovementsCount); // No new movements for this product

        var postReservationsCount = await db.OrderReservations.CountAsync(r => r.OrderId == createdOrder.Id);
        Assert.Equal(0, postReservationsCount); // No reservations created for this order
    }

    private async Task<InventoryBatch> CreateTestBatchAsync(
        int productId,
        int physicalQuantity,
        BatchStatus status = BatchStatus.Released,
        DateOnly? productionDate = null,
        DateOnly? expiryDate = null,
        int? initialQuantity = null,
        bool explicitNullExpiry = false)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var batch = new InventoryBatch
        {
            ProductId = productId,
            BatchNo = $"BAT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            ProductionDate = productionDate ?? today,
            ExpiryDate = explicitNullExpiry ? null : (expiryDate ?? (productionDate ?? today).AddYears(1)),
            InitialQuantity = initialQuantity ?? Math.Max(1, physicalQuantity),
            PhysicalQuantity = physicalQuantity,
            ReservedQuantity = 0,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    private async Task<OrderResponse> CreateTestOrderAsync(params (int ProductId, int Quantity)[] items)
    {
        var request = new CreateOrderRequest
        {
            CustomerName = "Integration Customer",
            CustomerEmail = "customer@example.com",
            Items = items.Select(i => new CreateOrderItemRequest { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);
        return order;
    }

    private async Task SetProductQuantityOnHandAsync(int productId, int quantityOnHand)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var p = await db.Products.FirstAsync(x => x.Id == productId);
        p.QuantityOnHand = quantityOnHand;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ConfirmOrder_PendingOrderWithSufficientStock_TransitionsToConfirmedAndSetsTimestamp()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "CONF-SUCC");
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 20));

        var confirmRequest = new ConfirmOrderRequest { RowVersion = order.Version };
        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", confirmRequest);

        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);
        var confirmedOrder = await confirmResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(confirmedOrder);
        Assert.Equal("Confirmed", confirmedOrder.Status);
        Assert.NotNull(confirmedOrder.ConfirmedAtUtc);
        Assert.True(confirmedOrder.Version > 0);

        // Verify in DB
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.Id);
        Assert.NotNull(dbBatch);
        Assert.Equal(20, dbBatch.ReservedQuantity);
        Assert.Equal(50, dbBatch.PhysicalQuantity); // Unchanged

        var dbReservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Single(dbReservations);
        Assert.Equal(20, dbReservations[0].Quantity);
        Assert.Equal(ReservationStatus.Active, dbReservations[0].Status);
        Assert.Equal(batch.Id, dbReservations[0].InventoryBatchId);

        var movementsCount = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);
        Assert.Equal(0, movementsCount); // No movements during reservation
    }

    [Fact]
    public async Task ConfirmOrder_WithFifoAllocationAcrossMultipleBatches_AllocatesInProductionDateAscendingOrder()
    {
        var product = await CreateTestProductAsync(30.00m, isActive: true, "CONF-FIFO");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Batch 1 is older, Batch 2 is newer
        var batch1 = await CreateTestBatchAsync(product.Id, 30, BatchStatus.Released, productionDate: today.AddDays(-10));
        var batch2 = await CreateTestBatchAsync(product.Id, 30, BatchStatus.Released, productionDate: today.AddDays(-5));

        var order = await CreateTestOrderAsync((product.Id, 45));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest { RowVersion = order.Version });
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch1 = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch1.Id);
        var dbBatch2 = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch2.Id);
        Assert.NotNull(dbBatch1);
        Assert.NotNull(dbBatch2);

        // Batch 1 should be completely exhausted first (30 allocated)
        Assert.Equal(30, dbBatch1.ReservedQuantity);
        // Batch 2 should satisfy the remainder (15 allocated)
        Assert.Equal(15, dbBatch2.ReservedQuantity);

        var reservations = await db.OrderReservations.AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.Equal(2, reservations.Count);
        Assert.Equal(batch1.Id, reservations[0].InventoryBatchId);
        Assert.Equal(30, reservations[0].Quantity);
        Assert.Equal(batch2.Id, reservations[1].InventoryBatchId);
        Assert.Equal(15, reservations[1].Quantity);
    }

    [Fact]
    public async Task ConfirmOrder_WithMatchingProductionDates_AllocatesDeterministicallyByBatchIdAscending()
    {
        var product = await CreateTestProductAsync(20.00m, isActive: true, "CONF-TIE");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var batchA = await CreateTestBatchAsync(product.Id, 20, BatchStatus.Released, productionDate: today);
        var batchB = await CreateTestBatchAsync(product.Id, 20, BatchStatus.Released, productionDate: today);
        Assert.True(batchA.Id < batchB.Id);

        var order = await CreateTestOrderAsync((product.Id, 30));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatchA = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchA.Id);
        var dbBatchB = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchB.Id);
        Assert.NotNull(dbBatchA);
        Assert.NotNull(dbBatchB);

        // Lower Id (batchA) gets 20 first, higher Id (batchB) gets remaining 10
        Assert.Equal(20, dbBatchA.ReservedQuantity);
        Assert.Equal(10, dbBatchB.ReservedQuantity);
    }

    [Fact]
    public async Task ConfirmOrder_ExcludesPendingReleaseQuarantinedAndDepletedBatches()
    {
        var product = await CreateTestProductAsync(15.00m, isActive: true, "CONF-STATUS");

        await CreateTestBatchAsync(product.Id, 50, BatchStatus.PendingRelease);
        await CreateTestBatchAsync(product.Id, 50, BatchStatus.Quarantined);
        await CreateTestBatchAsync(product.Id, 0, BatchStatus.Depleted);
        var releasedBatch = await CreateTestBatchAsync(product.Id, 15, BatchStatus.Released);

        // Order for 20 (more than the 15 available in the only Released batch)
        var order = await CreateTestOrderAsync((product.Id, 20));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, confirmResp.StatusCode);
        var problem = await confirmResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Insufficient available stock", problem.Detail);

        // Now create order for 10 which fits in the 15 Released batch
        var validOrder = await CreateTestOrderAsync((product.Id, 10));
        var validResp = await _client.PostAsJsonAsync($"/api/orders/{validOrder.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var dbReleased = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == releasedBatch.Id);
        Assert.NotNull(dbReleased);
        Assert.Equal(10, dbReleased.ReservedQuantity);
    }

    [Fact]
    public async Task ConfirmOrder_ExcludesExpiredAndNullExpiryBatches_AllocatesOnlyEligibleFutureExpiryBatches()
    {
        var product = await CreateTestProductAsync(10.00m, isActive: true, "CONF-EXP");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Expired yesterday -> excluded
        var expiredBatch = await CreateTestBatchAsync(product.Id, 30, BatchStatus.Released, productionDate: today.AddDays(-20), expiryDate: today.AddDays(-1));
        // Null expiry -> excluded under FEFO sales policy (cannot be automatically allocated)
        var nullExpiryBatch = await CreateTestBatchAsync(product.Id, 20, BatchStatus.Released, productionDate: today.AddDays(-10), explicitNullExpiry: true);
        // Future expiry -> eligible
        var futureExpiryBatch = await CreateTestBatchAsync(product.Id, 20, BatchStatus.Released, productionDate: today.AddDays(-5), expiryDate: today.AddDays(15));

        // Order requires 15 items (fits into the 20 available in futureExpiryBatch).
        var order = await CreateTestOrderAsync((product.Id, 15));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbExpired = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == expiredBatch.Id);
        var dbNullExp = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == nullExpiryBatch.Id);
        var dbFutureExp = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == futureExpiryBatch.Id);

        Assert.NotNull(dbExpired);
        Assert.NotNull(dbNullExp);
        Assert.NotNull(dbFutureExp);

        Assert.Equal(0, dbExpired.ReservedQuantity); // Untouched (expired)
        Assert.Equal(0, dbNullExp.ReservedQuantity); // Untouched (null expiry excluded from order allocation)
        Assert.Equal(15, dbFutureExp.ReservedQuantity); // 15 allocated from future-expiry batch

        // Trying to order 10 more (total remaining eligible = 5) fails with 409 Conflict
        var secondOrder = await CreateTestOrderAsync((product.Id, 10));
        var secondResp = await _client.PostAsJsonAsync($"/api/orders/{secondOrder.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, secondResp.StatusCode);
    }

    [Fact]
    public async Task ConfirmOrder_WithFefoAllocation_AllocatesEarlierExpiryBeforeEarlierProductionDate()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "CONF-FEFO-ORD");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Batch A: Earlier production (Jan 2026), but later expiry (Jun 2027)
        var batchA = await CreateTestBatchAsync(
            product.Id,
            20,
            BatchStatus.Released,
            productionDate: new DateOnly(2026, 1, 1),
            expiryDate: new DateOnly(2027, 6, 1));

        // Batch B: Later production (Feb 2026), but earlier expiry (Jan 2027)
        var batchB = await CreateTestBatchAsync(
            product.Id,
            20,
            BatchStatus.Released,
            productionDate: new DateOnly(2026, 2, 1),
            expiryDate: new DateOnly(2027, 1, 1));

        // Order requires 25 units:
        // Under FEFO: Batch B (earlier expiry) must be exhausted first (20 units),
        // then Batch A takes remainder (5 units).
        var order = await CreateTestOrderAsync((product.Id, 25));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest { RowVersion = order.Version });
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatchA = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batchA.Id);
        var dbBatchB = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batchB.Id);

        // FEFO Invariant: Batch B (earlier expiry) exhausted first
        Assert.Equal(20, dbBatchB.ReservedQuantity);
        // Batch A satisfies remaining 5
        Assert.Equal(5, dbBatchA.ReservedQuantity);
    }

    [Fact]
    public async Task ConfirmOrder_WithInsufficientStock_RollsBackAllReservationsAndLeavesOrderPending()
    {
        var productA = await CreateTestProductAsync(10.00m, isActive: true, "CONF-ROLL-A");
        var productB = await CreateTestProductAsync(15.00m, isActive: true, "CONF-ROLL-B");

        var batchA = await CreateTestBatchAsync(productA.Id, 10); // Has 10 available
        var batchB = await CreateTestBatchAsync(productB.Id, 5);  // Has 5 available

        // Order needs Product A: 5 (sufficient), Product B: 10 (INSUFFICIENT)
        var order = await CreateTestOrderAsync((productA.Id, 5), (productB.Id, 10));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, confirmResp.StatusCode);

        var problem = await confirmResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Insufficient available stock", problem.Detail);

        // Verify total rollback in DB
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatus.Pending, dbOrder.Status);
        Assert.Null(dbOrder.ConfirmedAtUtc);

        var dbBatchA = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchA.Id);
        var dbBatchB = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchB.Id);
        Assert.NotNull(dbBatchA);
        Assert.NotNull(dbBatchB);
        Assert.Equal(0, dbBatchA.ReservedQuantity); // Rolled back!
        Assert.Equal(0, dbBatchB.ReservedQuantity); // Rolled back!

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Empty(reservations); // No reservations created!
    }

    [Fact]
    public async Task ConfirmOrder_ReconfirmingAlreadyConfirmedOrder_ReturnsConflictProblemDetailsAndDoesNotDuplicateReservations()
    {
        var product = await CreateTestProductAsync(12.00m, isActive: true, "CONF-RECONF");
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 15));

        // First confirmation succeeds
        var confirmResp1 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp1.StatusCode);

        // Second confirmation returns 409 Conflict
        var confirmResp2 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, confirmResp2.StatusCode);
        var problem = await confirmResp2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("cannot be confirmed because it is in status 'Confirmed'", problem.Detail);

        // Verify in DB that reservations were not duplicated
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.Id);
        Assert.NotNull(dbBatch);
        Assert.Equal(15, dbBatch.ReservedQuantity); // Still exactly 15, not 30

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Single(reservations);
    }

    [Fact]
    public async Task ConfirmOrder_WithNonExistentOrder_ReturnsNotFoundProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/api/orders/999999/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmOrder_WithInactiveProduct_ReturnsBadRequestProblemDetails()
    {
        var product = await CreateTestProductAsync(18.00m, isActive: true, "CONF-INACT");
        await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 10));

        // Deactivate product after order was created
        var deactResp = await _client.PatchAsync($"/api/products/{product.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);

        // Attempt to confirm
        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.BadRequest, confirmResp.StatusCode);
        var problem = await confirmResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("inactive product", problem.Detail);
    }

    [Fact]
    public async Task ConfirmOrder_DoesNotModifyPhysicalStockOrQuantityOnHandOrStockMovements()
    {
        var product = await CreateTestProductAsync(22.00m, isActive: true, "CONF-NO-DEDUCT");
        var batch = await CreateTestBatchAsync(product.Id, 100);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        // Set product initial cached QuantityOnHand
        var p = await db.Products.FirstAsync(p => p.Id == product.Id);
        p.QuantityOnHand = 100;
        await db.SaveChangesAsync();

        var initialMovements = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);

        var order = await CreateTestOrderAsync((product.Id, 30));
        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // Verify physical stock and QuantityOnHand remain untouched
        var freshProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        var freshBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        var postMovements = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);

        Assert.Equal(100, freshProduct.QuantityOnHand); // Physical stock aggregate untouched!
        Assert.Equal(100, freshBatch.PhysicalQuantity); // Physical quantity untouched!
        Assert.Equal(30, freshBatch.ReservedQuantity);   // Reserved quantity updated!
        Assert.Equal(initialMovements, postMovements);   // No StockMovements created!
    }

    [Fact]
    public async Task ConfirmOrder_ConcurrentOrdersCompetingForStock_PreventsOverselling()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "CONF-CONCUR");
        // Only 10 available
        var batch = await CreateTestBatchAsync(product.Id, 10);

        // Order 1 needs 7, Order 2 needs 7. Total requested = 14 > 10 available.
        var order1 = await CreateTestOrderAsync((product.Id, 7));
        var order2 = await CreateTestOrderAsync((product.Id, 7));

        // Fire both confirmations concurrently
        var task1 = _client.PostAsJsonAsync($"/api/orders/{order1.Id}/confirm", new ConfirmOrderRequest());
        var task2 = _client.PostAsJsonAsync($"/api/orders/{order2.Id}/confirm", new ConfirmOrderRequest());

        var responses = await Task.WhenAll(task1, task2);

        var successResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        // Exactly one should succeed, and one should fail with 409 Conflict
        Assert.Single(successResponses);
        Assert.Single(conflictResponses);

        // Verify in DB that total reserved quantity is strictly 7 (never oversold to 14)
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(7, dbBatch.ReservedQuantity);

        var reservationsOrder1 = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order1.Id).ToListAsync();
        var reservationsOrder2 = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order2.Id).ToListAsync();

        Assert.True(
            (reservationsOrder1.Count == 1 && reservationsOrder2.Count == 0) ||
            (reservationsOrder1.Count == 0 && reservationsOrder2.Count == 1));
    }

    [Fact]
    public async Task CancelOrder_PendingOrderWithoutReservations_TransitionsToCancelled()
    {
        var product = await CreateTestProductAsync(15.00m, isActive: true, "CANC-PEND");
        var order = await CreateTestOrderAsync((product.Id, 5));

        var cancelReq = new CancelOrderRequest
        {
            CancellationReason = "Customer decided not to proceed before confirmation",
            RowVersion = order.Version
        };

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", cancelReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Equal("Customer decided not to proceed before confirmation", cancelled.CancellationReason);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var dbOrder = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
        Assert.NotNull(dbOrder);
        Assert.Equal(OrderStatus.Cancelled, dbOrder.Status);
        Assert.NotNull(dbOrder.CancelledAtUtc);
        Assert.Equal("Customer decided not to proceed before confirmation", dbOrder.CancellationReason);

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Empty(reservations);
    }

    [Fact]
    public async Task CancelOrder_ConfirmedOrder_ReleasesAllActiveReservationsAndRestoresBatchReservedQuantity()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "CANC-CONF");
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 20));

        // Confirm order -> reserves 20
        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // Cancel order
        var cancelReq = new CancelOrderRequest
        {
            CancellationReason = "Customer requested cancellation after confirmation"
        };
        var cancelResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        var cancelled = await cancelResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Equal("Customer requested cancellation after confirmation", cancelled.CancellationReason);

        // Verify in DB that batch ReservedQuantity is restored and reservation is Released
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(0, dbBatch.ReservedQuantity); // Restored from 20 to 0!
        Assert.Equal(50, dbBatch.PhysicalQuantity); // Unchanged

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Single(reservations);
        Assert.Equal(ReservationStatus.Released, reservations[0].Status);
        Assert.NotNull(reservations[0].ReleasedAtUtc);
        Assert.True(reservations[0].ReleasedAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task CancelOrder_WithMultipleBatchesAndItems_ReleasesAllBatchesAndReservationsCorrectly()
    {
        var product = await CreateTestProductAsync(30.00m, isActive: true, "CANC-MULTI");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 2 batches for product
        var batch1 = await CreateTestBatchAsync(product.Id, 30, BatchStatus.Released, productionDate: today.AddDays(-10));
        var batch2 = await CreateTestBatchAsync(product.Id, 30, BatchStatus.Released, productionDate: today.AddDays(-5));

        // Order for 45 (reserves 30 on batch1 and 15 on batch2)
        var order = await CreateTestOrderAsync((product.Id, 45));
        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // Cancel order
        var cancelReq = new CancelOrderRequest { CancellationReason = "Multi-batch cancellation test" };
        var cancelResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch1 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch1.Id);
        var dbBatch2 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch2.Id);

        Assert.Equal(0, dbBatch1.ReservedQuantity); // Restored from 30 back to 0
        Assert.Equal(0, dbBatch2.ReservedQuantity); // Restored from 15 back to 0

        var reservations = await db.OrderReservations.AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, r =>
        {
            Assert.Equal(ReservationStatus.Released, r.Status);
            Assert.NotNull(r.ReleasedAtUtc);
        });
    }

    [Fact]
    public async Task CancelOrder_ValidatesCancellationReason_RejectsBlankOrOversizedReason()
    {
        var product = await CreateTestProductAsync(10.00m, isActive: true, "CANC-VAL");
        var order = await CreateTestOrderAsync((product.Id, 5));

        // Blank reason
        var blankResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, blankResp.StatusCode);

        // Whitespace only
        var spaceResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, spaceResp.StatusCode);

        // Over 500 characters
        var longResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = new string('X', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, longResp.StatusCode);

        // Valid reason succeeds
        var validResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Valid cancellation reason" });
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_RepeatedCancellation_ReturnsConflictAndDoesNotDoubleRelease()
    {
        var product = await CreateTestProductAsync(12.00m, isActive: true, "CANC-REPEAT");
        var batch = await CreateTestBatchAsync(product.Id, 40);
        var order = await CreateTestOrderAsync((product.Id, 15));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // First cancellation succeeds
        var cancelResp1 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "First cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelResp1.StatusCode);

        // Second cancellation returns 409 Conflict
        var cancelResp2 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Duplicate cancel attempt" });
        Assert.Equal(HttpStatusCode.Conflict, cancelResp2.StatusCode);
        var problem = await cancelResp2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("cannot be cancelled because it is in status 'Cancelled'", problem.Detail);

        // Verify batch ReservedQuantity was not decremented twice
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(0, dbBatch.ReservedQuantity); // Still 0, never negative!
    }

    [Fact]
    public async Task CancelOrder_CompletedOrder_ReturnsConflict()
    {
        var product = await CreateTestProductAsync(14.00m, isActive: true, "CANC-COMPL");
        await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 10));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Transition order to Completed in database
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var dbOrder = await db.Orders.FirstAsync(o => o.Id == order.Id);
            dbOrder.Status = OrderStatus.Completed;
            await db.SaveChangesAsync();
        }

        // Attempt cancellation on Completed order
        var cancelResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Try cancel completed" });
        Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);
        var problem = await cancelResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("cannot be cancelled because it is in status 'Completed'", problem.Detail);
    }

    [Fact]
    public async Task CancelOrder_WithNonExistentOrder_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/orders/999999/cancel", new CancelOrderRequest { CancellationReason = "Cancel missing order" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_ConcurrentRequests_CannotDoubleReleaseReservations()
    {
        var product = await CreateTestProductAsync(20.00m, isActive: true, "CANC-CONCUR");
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 25));

        // Confirm order -> batch.ReservedQuantity = 25
        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Fire 2 concurrent cancellation requests
        var task1 = _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Concurrent cancel 1" });
        var task2 = _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Concurrent cancel 2" });

        var responses = await Task.WhenAll(task1, task2);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        // Exactly one succeeds, other gets 409 Conflict
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        // Verify in DB that ReservedQuantity is exactly 0 (decremented once by 25, never to -25)
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(0, dbBatch.ReservedQuantity);

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Single(reservations);
        Assert.Equal(ReservationStatus.Released, reservations[0].Status);
    }

    [Fact]
    public async Task CancelOrder_DoesNotModifyPhysicalStockOrQuantityOnHandOrStockMovements()
    {
        var product = await CreateTestProductAsync(16.00m, isActive: true, "CANC-NODEDUCT");
        var batch = await CreateTestBatchAsync(product.Id, 100);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var p = await db.Products.FirstAsync(p => p.Id == product.Id);
        p.QuantityOnHand = 100;
        await db.SaveChangesAsync();

        var initialMovements = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);

        var order = await CreateTestOrderAsync((product.Id, 25));
        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Cancel the order
        var cancelResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequest { CancellationReason = "Check physical stock unchanged" });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // Verify physical quantities and movements remain strictly unchanged
        var freshProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        var freshBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        var postMovements = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);

        Assert.Equal(100, freshProduct.QuantityOnHand); // Physical stock aggregate untouched!
        Assert.Equal(100, freshBatch.PhysicalQuantity); // Physical quantity untouched!
        Assert.Equal(0, freshBatch.ReservedQuantity);   // Reserved quantity restored to 0!
        Assert.Equal(initialMovements, postMovements);   // No StockMovements created during reservation or cancellation!
    }

    [Fact]
    public async Task ConfirmAndCancel_ConcurrentlyOnSamePendingOrder_ProducesConsistentFinalStateWithoutLeakedReservations()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "CONF-CANC-RACE");
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 20));

        // Use TaskCompletionSource barrier to release both concurrent tasks simultaneously onto the thread pool
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var confirmTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await _client.PostAsJsonAsync(
                $"/api/orders/{order.Id}/confirm",
                new ConfirmOrderRequest { RowVersion = order.Version });
        });

        var cancelTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await _client.PostAsJsonAsync(
                $"/api/orders/{order.Id}/cancel",
                new CancelOrderRequest
                {
                    CancellationReason = "Concurrent cancellation request",
                    RowVersion = order.Version
                });
        });

        // Trigger simultaneous execution
        startGate.SetResult();

        var responses = await Task.WhenAll(confirmTask, cancelTask);
        var confirmResp = responses[0];
        var cancelResp = responses[1];

        // Both requests must return well-defined status codes (200 OK or 409 Conflict)
        Assert.True(
            confirmResp.StatusCode == HttpStatusCode.OK || confirmResp.StatusCode == HttpStatusCode.Conflict,
            $"Confirm returned unexpected status: {confirmResp.StatusCode}");
        Assert.True(
            cancelResp.StatusCode == HttpStatusCode.OK || cancelResp.StatusCode == HttpStatusCode.Conflict,
            $"Cancel returned unexpected status: {cancelResp.StatusCode}");

        // Verify final state strictly via a new, isolated DbContext
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();

        // Final status must be either Confirmed or Cancelled - never in an indeterminate state
        Assert.True(
            dbOrder.Status == OrderStatus.Confirmed || dbOrder.Status == OrderStatus.Cancelled,
            $"Order ended in unexpected status: {dbOrder.Status}");

        if (dbOrder.Status == OrderStatus.Confirmed)
        {
            // Confirm won: Confirm succeeded, Cancel was rejected as conflict
            Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);

            // Exactly 20 units reserved, 1 active reservation
            Assert.Equal(20, dbBatch.ReservedQuantity);
            Assert.Single(reservations);
            Assert.Equal(ReservationStatus.Active, reservations[0].Status);
            Assert.Equal(20, reservations[0].Quantity);
        }
        else // dbOrder.Status == OrderStatus.Cancelled
        {
            // Cancel won (either Cancel won first, or Confirm won and Cancel subsequent released it)
            Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
            Assert.NotNull(dbOrder.CancelledAtUtc);
            Assert.Equal("Concurrent cancellation request", dbOrder.CancellationReason);

            // All reservations released, ReservedQuantity strictly restored to 0
            Assert.Equal(0, dbBatch.ReservedQuantity);
            Assert.All(reservations, r => Assert.Equal(ReservationStatus.Released, r.Status));
        }

        // In all outcomes, physical quantity is never modified and no StockMovements are created
        Assert.Equal(50, dbBatch.PhysicalQuantity);
        var movementsCount = await db.StockMovements.CountAsync(m => m.ProductId == product.Id);
        Assert.Equal(0, movementsCount);
    }

    // ==========================================
    // STAGE 5 — ORDER FULFILLMENT TESTS
    // ==========================================

    [Fact]
    public async Task FulfillOrder_ConfirmedOrderSingleBatch_TransitionsToCompletedDeductsStockAndCreatesMovement()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "FUL-SUCC-1");
        await SetProductQuantityOnHandAsync(product.Id, 50);
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 20));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        var fulfilledOrder = await fulfillResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(fulfilledOrder);
        Assert.Equal("Completed", fulfilledOrder.Status);
        Assert.NotNull(fulfilledOrder.CompletedAtUtc);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Completed, dbOrder.Status);
        Assert.NotNull(dbOrder.CompletedAtUtc);

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(30, dbBatch.PhysicalQuantity);
        Assert.Equal(0, dbBatch.ReservedQuantity);
        Assert.Equal(BatchStatus.Released, dbBatch.Status);

        var dbProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(30, dbProduct.QuantityOnHand);

        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        Assert.Single(reservations);
        Assert.Equal(ReservationStatus.Fulfilled, reservations[0].Status);

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceOrderId == order.Id)
            .ToListAsync();
        Assert.Single(movements);
        var mov = movements[0];
        Assert.Equal(StockMovementType.OrderDeduction, mov.MovementType);
        Assert.Equal(-20, mov.QuantityChange);
        Assert.Equal(30, mov.BalanceAfter);
        Assert.Equal(30, mov.BatchBalanceAfter);
        Assert.Equal(batch.Id, mov.InventoryBatchId);
        Assert.Equal(product.Id, mov.ProductId);
    }

    [Fact]
    public async Task FulfillOrder_SingleItemMultiBatch_DeductsFromBothBatchesAndDepletesExhaustedBatch()
    {
        var product = await CreateTestProductAsync(20.00m, isActive: true, "FUL-MULTI-1");
        await SetProductQuantityOnHandAsync(product.Id, 35);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var batch1 = await CreateTestBatchAsync(product.Id, 15, productionDate: today.AddDays(-2));
        var batch2 = await CreateTestBatchAsync(product.Id, 20, productionDate: today.AddDays(-1));

        var order = await CreateTestOrderAsync((product.Id, 25));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Completed, dbOrder.Status);
        Assert.NotNull(dbOrder.CompletedAtUtc);

        var dbBatch1 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch1.Id);
        Assert.Equal(0, dbBatch1.PhysicalQuantity);
        Assert.Equal(0, dbBatch1.ReservedQuantity);
        Assert.Equal(BatchStatus.Depleted, dbBatch1.Status);

        var dbBatch2 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch2.Id);
        Assert.Equal(10, dbBatch2.PhysicalQuantity);
        Assert.Equal(0, dbBatch2.ReservedQuantity);
        Assert.Equal(BatchStatus.Released, dbBatch2.Status);

        var dbProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(10, dbProduct.QuantityOnHand);

        var reservations = await db.OrderReservations.AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.Id)
            .ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Fulfilled, r.Status));

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceOrderId == order.Id)
            .OrderBy(m => m.InventoryBatchId)
            .ToListAsync();
        Assert.Equal(2, movements.Count);

        var mov1 = movements.First(m => m.InventoryBatchId == batch1.Id);
        Assert.Equal(StockMovementType.OrderDeduction, mov1.MovementType);
        Assert.Equal(-15, mov1.QuantityChange);
        Assert.Equal(0, mov1.BatchBalanceAfter);

        var mov2 = movements.First(m => m.InventoryBatchId == batch2.Id);
        Assert.Equal(StockMovementType.OrderDeduction, mov2.MovementType);
        Assert.Equal(-10, mov2.QuantityChange);
        Assert.Equal(10, mov2.BatchBalanceAfter);
    }

    [Fact]
    public async Task FulfillOrder_BatchReachingZeroStock_BecomesDepleted()
    {
        var product = await CreateTestProductAsync(10.00m, isActive: true, "FUL-DEP-1");
        await SetProductQuantityOnHandAsync(product.Id, 10);
        var batch = await CreateTestBatchAsync(product.Id, 10);
        var order = await CreateTestOrderAsync((product.Id, 10));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(0, dbBatch.PhysicalQuantity);
        Assert.Equal(0, dbBatch.ReservedQuantity);
        Assert.Equal(BatchStatus.Depleted, dbBatch.Status);

        var dbProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(0, dbProduct.QuantityOnHand);
    }

    [Fact]
    public async Task FulfillOrder_OrderWithInvalidStatus_ReturnsConflict()
    {
        var product = await CreateTestProductAsync(30.00m, isActive: true, "FUL-INVALID-STATUS");
        await SetProductQuantityOnHandAsync(product.Id, 50);
        await CreateTestBatchAsync(product.Id, 50);

        // Subcase 1: Pending order
        var pendingOrder = await CreateTestOrderAsync((product.Id, 5));
        var pendingFulfillResp = await _client.PostAsJsonAsync($"/api/orders/{pendingOrder.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, pendingFulfillResp.StatusCode);

        // Subcase 2: Cancelled order
        var cancelOrder = await CreateTestOrderAsync((product.Id, 5));
        await _client.PostAsJsonAsync($"/api/orders/{cancelOrder.Id}/cancel", new CancelOrderRequest { CancellationReason = "Test cancel" });
        var cancelledFulfillResp = await _client.PostAsJsonAsync($"/api/orders/{cancelOrder.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, cancelledFulfillResp.StatusCode);
    }

    [Fact]
    public async Task FulfillOrder_RepeatFulfillment_ReturnsConflictAndDoesNotDoubleDeduct()
    {
        var product = await CreateTestProductAsync(15.00m, isActive: true, "FUL-REPEAT-1");
        await SetProductQuantityOnHandAsync(product.Id, 40);
        var batch = await CreateTestBatchAsync(product.Id, 40);
        var order = await CreateTestOrderAsync((product.Id, 10));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // First fulfillment: 200 OK
        var firstFulfill = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.OK, firstFulfill.StatusCode);

        // Second fulfillment attempt: 409 Conflict
        var secondFulfill = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, secondFulfill.StatusCode);

        // Verify stock was not deducted twice
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(30, dbBatch.PhysicalQuantity); // 40 - 10 = 30 (not 20!)

        var dbProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(30, dbProduct.QuantityOnHand);

        var movementsCount = await db.StockMovements.CountAsync(m => m.ReferenceOrderId == order.Id);
        Assert.Equal(1, movementsCount);
    }

    [Fact]
    public async Task FulfillOrder_MissingOrInconsistentReservations_ReturnsConflict()
    {
        var product = await CreateTestProductAsync(18.00m, isActive: true, "FUL-MISMATCH-1");
        await SetProductQuantityOnHandAsync(product.Id, 50);
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 10));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Alter reservation quantity in DB to cause mismatch (10 ordered, but only 7 reserved)
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var res = await db.OrderReservations.FirstAsync(r => r.OrderId == order.Id);
            res.Quantity = 7;
            await db.SaveChangesAsync();
        }

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, fulfillResp.StatusCode);

        // Verify order remains Confirmed, physical stock untouched
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Confirmed, dbOrder.Status);

            var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
            Assert.Equal(50, dbBatch.PhysicalQuantity);
        }
    }

    [Fact]
    public async Task FulfillOrder_BatchExpiredOrQuarantined_ReturnsConflictAndRollsBack()
    {
        var product = await CreateTestProductAsync(22.00m, isActive: true, "FUL-EXP-QUAR");
        await SetProductQuantityOnHandAsync(product.Id, 50);
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 15));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Quarantining batch before fulfillment
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var b = await db.InventoryBatches.FirstAsync(x => x.Id == batch.Id);
            b.Status = BatchStatus.Quarantined;
            await db.SaveChangesAsync();
        }

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, fulfillResp.StatusCode);

        // Now test expired batch
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var b = await db.InventoryBatches.FirstAsync(x => x.Id == batch.Id);
            b.Status = BatchStatus.Released;
            b.ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await db.SaveChangesAsync();
        }

        var expiredFulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, expiredFulfillResp.StatusCode);

        // Verify order remained Confirmed and physical stock untouched
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Confirmed, dbOrder.Status);

            var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
            Assert.Equal(50, dbBatch.PhysicalQuantity);
            Assert.Equal(15, dbBatch.ReservedQuantity);

            var moveCount = await db.StockMovements.CountAsync(m => m.ReferenceOrderId == order.Id);
            Assert.Equal(0, moveCount);
        }
    }

    [Fact]
    public async Task FulfillOrder_InsufficientBatchStock_RollsBackTransactionCompletely()
    {
        var product = await CreateTestProductAsync(16.00m, isActive: true, "FUL-INSUF-PHYS");
        await SetProductQuantityOnHandAsync(product.Id, 30);
        var batch = await CreateTestBatchAsync(product.Id, 30);
        var order = await CreateTestOrderAsync((product.Id, 20));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Simulate reserved quantity reduced below required 20 (e.g. set to 10, while physical is 30)
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var b = await db.InventoryBatches.FirstAsync(x => x.Id == batch.Id);
            b.ReservedQuantity = 10;
            await db.SaveChangesAsync();
        }

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, fulfillResp.StatusCode);

        // Verify transaction was completely rolled back
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Confirmed, dbOrder.Status);
            Assert.Null(dbOrder.CompletedAtUtc);

            var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
            Assert.Equal(30, dbBatch.PhysicalQuantity);
            Assert.Equal(10, dbBatch.ReservedQuantity);

            var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
            Assert.All(reservations, r => Assert.Equal(ReservationStatus.Active, r.Status));

            var movementsCount = await db.StockMovements.CountAsync(m => m.ReferenceOrderId == order.Id);
            Assert.Equal(0, movementsCount);
        }
    }

    [Fact]
    public async Task FulfillOrder_ConcurrentFulfillAndCancel_SerializesAndExactlyOneSucceeds()
    {
        var product = await CreateTestProductAsync(25.00m, isActive: true, "FUL-CONC-1");
        await SetProductQuantityOnHandAsync(product.Id, 50);
        var batch = await CreateTestBatchAsync(product.Id, 50);
        var order = await CreateTestOrderAsync((product.Id, 20));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Refetch order to obtain fresh Version
        var getResp = await _client.GetAsync($"/api/orders/{order.Id}");
        var confirmedOrder = await getResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(confirmedOrder);

        var startGate = new TaskCompletionSource();

        var fulfillTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await _client.PostAsJsonAsync(
                $"/api/orders/{order.Id}/fulfill",
                new FulfillOrderRequest { RowVersion = confirmedOrder.Version });
        });

        var cancelTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await _client.PostAsJsonAsync(
                $"/api/orders/{order.Id}/cancel",
                new CancelOrderRequest
                {
                    CancellationReason = "Race condition cancellation",
                    RowVersion = confirmedOrder.Version
                });
        });

        startGate.SetResult();

        var responses = await Task.WhenAll(fulfillTask, cancelTask);
        var fulfillResp = responses[0];
        var cancelResp = responses[1];

        // Exactly one should succeed with 200 OK, the other with 409 Conflict
        var statusCodes = new[] { fulfillResp.StatusCode, cancelResp.StatusCode };
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        var dbBatch = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch.Id);
        var dbProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        var reservations = await db.OrderReservations.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync();
        var movements = await db.StockMovements.AsNoTracking().Where(m => m.ReferenceOrderId == order.Id).ToListAsync();

        if (dbOrder.Status == OrderStatus.Completed)
        {
            Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);
            Assert.NotNull(dbOrder.CompletedAtUtc);

            Assert.Equal(30, dbBatch.PhysicalQuantity);
            Assert.Equal(0, dbBatch.ReservedQuantity);
            Assert.Equal(30, dbProduct.QuantityOnHand); // Initial 50 - 20 fulfilled = 30
            Assert.All(reservations, r => Assert.Equal(ReservationStatus.Fulfilled, r.Status));
            Assert.Single(movements);
            Assert.Equal(StockMovementType.OrderDeduction, movements[0].MovementType);
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, dbOrder.Status);
            Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, fulfillResp.StatusCode);
            Assert.NotNull(dbOrder.CancelledAtUtc);

            Assert.Equal(50, dbBatch.PhysicalQuantity);
            Assert.Equal(0, dbBatch.ReservedQuantity);
            Assert.Equal(50, dbProduct.QuantityOnHand); // Untouched on cancellation
            Assert.All(reservations, r => Assert.Equal(ReservationStatus.Released, r.Status));
            Assert.Empty(movements);
        }
    }

    [Fact]
    public async Task FulfillOrder_WithRowVersion_ValidVersionSucceedsAndInvalidVersionReturnsConflict()
    {
        var product = await CreateTestProductAsync(14.00m, isActive: true, "FUL-ROWVER-1");
        await SetProductQuantityOnHandAsync(product.Id, 30);
        await CreateTestBatchAsync(product.Id, 30);
        var order = await CreateTestOrderAsync((product.Id, 10));

        await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());

        // Get confirmed order to read Version
        var getResp = await _client.GetAsync($"/api/orders/{order.Id}");
        var confirmedOrder = await getResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(confirmedOrder);

        // Subtest A: Stale/invalid RowVersion returns 409 Conflict
        var invalidReq = new FulfillOrderRequest { RowVersion = 99999999 };
        var invalidResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", invalidReq);
        Assert.Equal(HttpStatusCode.Conflict, invalidResp.StatusCode);

        // Subtest B: Valid RowVersion succeeds
        var validReq = new FulfillOrderRequest { RowVersion = confirmedOrder.Version };
        var validResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", validReq);
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);

        var completedOrder = await validResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(completedOrder);
        Assert.Equal("Completed", completedOrder.Status);
    }

    [Fact]
    public async Task FulfillOrder_WithNonExistentOrder_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/orders/999999/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FulfillOrder_MultiProductOrder_TransitionsToCompletedDeductsEachProductAndCreatesMovements()
    {
        var product1 = await CreateTestProductAsync(25.00m, isActive: true, "FUL-MPROD-1");
        var product2 = await CreateTestProductAsync(40.00m, isActive: true, "FUL-MPROD-2");

        await SetProductQuantityOnHandAsync(product1.Id, 50);
        await SetProductQuantityOnHandAsync(product2.Id, 60);

        var batch1 = await CreateTestBatchAsync(product1.Id, 50);
        var batch2 = await CreateTestBatchAsync(product2.Id, 60);

        // Order with 2 distinct products: 15 units of product1, 20 units of product2
        var order = await CreateTestOrderAsync(
            (product1.Id, 15),
            (product2.Id, 20));

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        var fulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.OK, fulfillResp.StatusCode);

        var completedOrder = await fulfillResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(completedOrder);
        Assert.Equal("Completed", completedOrder.Status);
        Assert.NotNull(completedOrder.CompletedAtUtc);

        // Verify final state via isolated DbContext
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Completed, dbOrder.Status);
        Assert.NotNull(dbOrder.CompletedAtUtc);

        // Verify Product 1 deductions
        var dbProduct1 = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product1.Id);
        Assert.Equal(35, dbProduct1.QuantityOnHand); // 50 - 15 = 35

        var dbBatch1 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch1.Id);
        Assert.Equal(35, dbBatch1.PhysicalQuantity); // 50 - 15 = 35
        Assert.Equal(0, dbBatch1.ReservedQuantity);
        Assert.Equal(BatchStatus.Released, dbBatch1.Status);

        // Verify Product 2 deductions
        var dbProduct2 = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product2.Id);
        Assert.Equal(40, dbProduct2.QuantityOnHand); // 60 - 20 = 40

        var dbBatch2 = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == batch2.Id);
        Assert.Equal(40, dbBatch2.PhysicalQuantity); // 60 - 20 = 40
        Assert.Equal(0, dbBatch2.ReservedQuantity);
        Assert.Equal(BatchStatus.Released, dbBatch2.Status);

        // Verify reservations status
        var reservations = await db.OrderReservations.AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.OrderItemId)
            .ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.All(reservations, r => Assert.Equal(ReservationStatus.Fulfilled, r.Status));

        // Verify StockMovements for each product and batch
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceOrderId == order.Id)
            .OrderBy(m => m.ProductId)
            .ToListAsync();
        Assert.Equal(2, movements.Count);

        var mov1 = movements.First(m => m.ProductId == product1.Id);
        Assert.Equal(StockMovementType.OrderDeduction, mov1.MovementType);
        Assert.Equal(-15, mov1.QuantityChange);
        Assert.Equal(35, mov1.BalanceAfter);
        Assert.Equal(35, mov1.BatchBalanceAfter);
        Assert.Equal(batch1.Id, mov1.InventoryBatchId);

        var mov2 = movements.First(m => m.ProductId == product2.Id);
        Assert.Equal(StockMovementType.OrderDeduction, mov2.MovementType);
        Assert.Equal(-20, mov2.QuantityChange);
        Assert.Equal(40, mov2.BalanceAfter);
        Assert.Equal(40, mov2.BatchBalanceAfter);
        Assert.Equal(batch2.Id, mov2.InventoryBatchId);

        // Verify repeat fulfillment is rejected and no double deduction occurs
        var repeatFulfillResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/fulfill", new FulfillOrderRequest());
        Assert.Equal(HttpStatusCode.Conflict, repeatFulfillResp.StatusCode);

        var recheckProduct1 = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product1.Id);
        var recheckProduct2 = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product2.Id);
        Assert.Equal(35, recheckProduct1.QuantityOnHand);
        Assert.Equal(40, recheckProduct2.QuantityOnHand);

        var recheckMovementsCount = await db.StockMovements.CountAsync(m => m.ReferenceOrderId == order.Id);
        Assert.Equal(2, recheckMovementsCount);
    }
}
