using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Orders;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Integration;

public class DataSeederIntegrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>, IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();
    private readonly PostgresTestFixture _fixture = fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync()
    {
        // Clean only demo-specific data before each test to guarantee test independence
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        await CleanDemoDataAsync(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task CleanDemoDataAsync(AbonStockDbContext db)
    {
        var demoSkus = new[]
        {
            AbonStockDataSeeder.SkuAbonSapiOri,
            AbonStockDataSeeder.SkuAbonSapiPds,
            AbonStockDataSeeder.SkuAbonAyamGrh
        };

        var demoOrders = new[]
        {
            AbonStockDataSeeder.OrderNoCompleted,
            AbonStockDataSeeder.OrderNoConfirmed,
            AbonStockDataSeeder.OrderNoPending,
            AbonStockDataSeeder.OrderNoCancelled
        };

        var orders = await db.Orders.Where(o => demoOrders.Contains(o.OrderNumber)).ToListAsync();
        var orderIds = orders.Select(o => o.Id).ToList();

        var demoBatches = new[]
        {
            AbonStockDataSeeder.BatchNoSapiOriDepleted,
            AbonStockDataSeeder.BatchNoSapiOriReleased,
            AbonStockDataSeeder.BatchNoSapiOriNoExpiry,
            AbonStockDataSeeder.BatchNoSapiPdsReleased,
            AbonStockDataSeeder.BatchNoAyamGrhReleased
        };

        var batches = await db.InventoryBatches.Where(b => demoBatches.Contains(b.BatchNo)).ToListAsync();
        var batchIds = batches.Select(b => b.Id).ToList();

        var movements = await db.StockMovements
            .Where(m => (m.ReferenceOrderId != null && orderIds.Contains(m.ReferenceOrderId.Value)) ||
                        (m.InventoryBatchId != null && batchIds.Contains(m.InventoryBatchId.Value)))
            .ToListAsync();
        db.StockMovements.RemoveRange(movements);

        var reservations = await db.OrderReservations
            .Where(r => orderIds.Contains(r.OrderId) || batchIds.Contains(r.InventoryBatchId))
            .ToListAsync();
        db.OrderReservations.RemoveRange(reservations);

        var orderItems = await db.OrderItems.Where(i => orderIds.Contains(i.OrderId)).ToListAsync();
        db.OrderItems.RemoveRange(orderItems);
        db.Orders.RemoveRange(orders);

        db.InventoryBatches.RemoveRange(batches);

        var products = await db.Products.Where(p => demoSkus.Contains(p.Sku)).ToListAsync();
        db.Products.RemoveRange(products);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SeedAsync_OnCleanDatabase_PopulatesAllExpectedEntitiesAndRelations()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        await AbonStockDataSeeder.SeedAsync(db);

        // 1. Verify Products
        var pOri = await db.Products.FirstOrDefaultAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiOri);
        var pPds = await db.Products.FirstOrDefaultAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiPds);
        var pGrh = await db.Products.FirstOrDefaultAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonAyamGrh);

        Assert.NotNull(pOri);
        Assert.Equal("Abon Sapi Original 100g", pOri.Name);
        Assert.Equal(35000.00m, pOri.UnitPrice);
        Assert.Equal(150, pOri.QuantityOnHand);
        Assert.True(pOri.IsActive);

        Assert.NotNull(pPds);
        Assert.Equal("Abon Sapi Pedas 100g", pPds.Name);
        Assert.Equal(37500.00m, pPds.UnitPrice);
        Assert.Equal(80, pPds.QuantityOnHand);
        Assert.True(pPds.IsActive);

        Assert.NotNull(pGrh);
        Assert.Equal("Abon Ayam Gurih 100g", pGrh.Name);
        Assert.Equal(28000.00m, pGrh.UnitPrice);
        Assert.Equal(60, pGrh.QuantityOnHand);
        Assert.True(pGrh.IsActive);

        // 2. Verify Batches
        var bDepleted = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriDepleted);
        var bSo02 = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriReleased);
        var bSo03 = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriNoExpiry);
        var bSp01 = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiPdsReleased);
        var bAg01 = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoAyamGrhReleased);

        Assert.NotNull(bDepleted);
        Assert.Equal(pOri.Id, bDepleted.ProductId);
        Assert.Equal(BatchStatus.Depleted, bDepleted.Status);
        Assert.Equal(50, bDepleted.InitialQuantity);
        Assert.Equal(0, bDepleted.PhysicalQuantity);
        Assert.Equal(0, bDepleted.ReservedQuantity);

        Assert.NotNull(bSo02);
        Assert.Equal(pOri.Id, bSo02.ProductId);
        Assert.Equal(BatchStatus.Released, bSo02.Status);
        Assert.Equal(100, bSo02.InitialQuantity);
        Assert.Equal(100, bSo02.PhysicalQuantity);
        Assert.Equal(20, bSo02.ReservedQuantity);
        Assert.Equal(80, bSo02.PhysicalQuantity - bSo02.ReservedQuantity);

        Assert.NotNull(bSo03);
        Assert.Equal(pOri.Id, bSo03.ProductId);
        Assert.Equal(BatchStatus.Released, bSo03.Status);
        Assert.Equal(50, bSo03.InitialQuantity);
        Assert.Equal(50, bSo03.PhysicalQuantity);
        Assert.Equal(0, bSo03.ReservedQuantity);
        Assert.Null(bSo03.ExpiryDate);

        Assert.NotNull(bSp01);
        Assert.Equal(pPds.Id, bSp01.ProductId);
        Assert.Equal(BatchStatus.Released, bSp01.Status);
        Assert.Equal(80, bSp01.InitialQuantity);
        Assert.Equal(80, bSp01.PhysicalQuantity);
        Assert.Equal(15, bSp01.ReservedQuantity);
        Assert.Equal(65, bSp01.PhysicalQuantity - bSp01.ReservedQuantity);

        Assert.NotNull(bAg01);
        Assert.Equal(pGrh.Id, bAg01.ProductId);
        Assert.Equal(BatchStatus.Released, bAg01.Status);
        Assert.Equal(60, bAg01.InitialQuantity);
        Assert.Equal(60, bAg01.PhysicalQuantity);
        Assert.Equal(0, bAg01.ReservedQuantity);
        Assert.Equal(60, bAg01.PhysicalQuantity - bAg01.ReservedQuantity);

        // 3. Verify Orders
        var ord1 = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoCompleted);
        var ord2 = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoConfirmed);
        var ord3 = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoPending);
        var ord4 = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoCancelled);

        Assert.NotNull(ord1);
        Assert.Equal(OrderStatus.Completed, ord1.Status);
        Assert.NotNull(ord1.CompletedAtUtc);
        Assert.Single(ord1.Items);

        Assert.NotNull(ord2);
        Assert.Equal(OrderStatus.Confirmed, ord2.Status);
        Assert.NotNull(ord2.ConfirmedAtUtc);
        Assert.Null(ord2.CompletedAtUtc);
        Assert.Equal(2, ord2.Items.Count);

        Assert.NotNull(ord3);
        Assert.Equal(OrderStatus.Pending, ord3.Status);
        Assert.Null(ord3.ConfirmedAtUtc);
        Assert.Null(ord3.CompletedAtUtc);
        Assert.Equal(2, ord3.Items.Count);

        Assert.NotNull(ord4);
        Assert.Equal(OrderStatus.Cancelled, ord4.Status);
        Assert.NotNull(ord4.CancelledAtUtc);
        Assert.Equal("Permintaan pembatalan dari pelanggan sebelum pengiriman", ord4.CancellationReason);
        Assert.Single(ord4.Items);

        // 4. Verify Reservations
        var resOrd2 = await db.OrderReservations.Where(r => r.OrderId == ord2.Id).ToListAsync();
        Assert.Equal(2, resOrd2.Count);
        Assert.All(resOrd2, r => Assert.Equal(ReservationStatus.Active, r.Status));
        Assert.Contains(resOrd2, r => r.InventoryBatchId == bSo02.Id && r.Quantity == 20);
        Assert.Contains(resOrd2, r => r.InventoryBatchId == bSp01.Id && r.Quantity == 15);

        var resOrd4 = await db.OrderReservations.Where(r => r.OrderId == ord4.Id).ToListAsync();
        Assert.Single(resOrd4);
        Assert.Equal(ReservationStatus.Released, resOrd4[0].Status);

        // 5. Verify Stock Movements
        int?[] demoBatchIds = [bDepleted.Id, bSo02.Id, bSo03.Id, bSp01.Id, bAg01.Id];
        var movements = await db.StockMovements
            .Where(m => m.InventoryBatchId != null && demoBatchIds.Contains(m.InventoryBatchId))
            .ToListAsync();
        Assert.Equal(6, movements.Count); // 5 Receipts + 1 Deduction for Order 1
        Assert.Equal(5, movements.Count(m => m.MovementType == StockMovementType.Receipt));
        Assert.Equal(1, movements.Count(m => m.MovementType == StockMovementType.OrderDeduction));
    }

    [Fact]
    public async Task SeedAsync_CalledMultipleTimes_IsIdempotentAndProducesNoDuplicates()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        // First call seeds the data
        await AbonStockDataSeeder.SeedAsync(db);

        var initialProductCount = await db.Products.CountAsync(p =>
            p.Sku == AbonStockDataSeeder.SkuAbonSapiOri ||
            p.Sku == AbonStockDataSeeder.SkuAbonSapiPds ||
            p.Sku == AbonStockDataSeeder.SkuAbonAyamGrh);

        var initialBatchCount = await db.InventoryBatches.CountAsync(b =>
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriDepleted ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriReleased ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriNoExpiry ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiPdsReleased ||
            b.BatchNo == AbonStockDataSeeder.BatchNoAyamGrhReleased);

        var initialOrderCount = await db.Orders.CountAsync(o =>
            o.OrderNumber == AbonStockDataSeeder.OrderNoCompleted ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoConfirmed ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoPending ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoCancelled);

        // Call SeedAsync multiple times
        await AbonStockDataSeeder.SeedAsync(db);
        await AbonStockDataSeeder.SeedAsync(db);

        var afterProductCount = await db.Products.CountAsync(p =>
            p.Sku == AbonStockDataSeeder.SkuAbonSapiOri ||
            p.Sku == AbonStockDataSeeder.SkuAbonSapiPds ||
            p.Sku == AbonStockDataSeeder.SkuAbonAyamGrh);

        var afterBatchCount = await db.InventoryBatches.CountAsync(b =>
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriDepleted ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriReleased ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriNoExpiry ||
            b.BatchNo == AbonStockDataSeeder.BatchNoSapiPdsReleased ||
            b.BatchNo == AbonStockDataSeeder.BatchNoAyamGrhReleased);

        var afterOrderCount = await db.Orders.CountAsync(o =>
            o.OrderNumber == AbonStockDataSeeder.OrderNoCompleted ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoConfirmed ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoPending ||
            o.OrderNumber == AbonStockDataSeeder.OrderNoCancelled);

        Assert.Equal(3, initialProductCount);
        Assert.Equal(3, afterProductCount);
        Assert.Equal(5, initialBatchCount);
        Assert.Equal(5, afterBatchCount);
        Assert.Equal(4, initialOrderCount);
        Assert.Equal(4, afterOrderCount);
    }

    [Fact]
    public async Task SeedAsync_DataCompliesWithAllDomainInvariantsAndLedgerContinuity()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        await AbonStockDataSeeder.SeedAsync(db);

        var demoSkus = new[]
        {
            AbonStockDataSeeder.SkuAbonSapiOri,
            AbonStockDataSeeder.SkuAbonSapiPds,
            AbonStockDataSeeder.SkuAbonAyamGrh
        };

        var products = await db.Products
            .Include(p => p.Batches)
            .Where(p => demoSkus.Contains(p.Sku))
            .ToListAsync();

        foreach (var product in products)
        {
            // 1. Invariant: QuantityOnHand == Sum of PhysicalQuantity for non-depleted batches
            var activePhysicalSum = product.Batches
                .Where(b => b.Status != BatchStatus.Depleted)
                .Sum(b => b.PhysicalQuantity);

            Assert.Equal(product.QuantityOnHand, activePhysicalSum);

            // 2. Invariant: For each batch, ReservedQuantity == sum of active reservations
            foreach (var batch in product.Batches)
            {
                var activeReservationsSum = await db.OrderReservations
                    .Where(r => r.InventoryBatchId == batch.Id && r.Status == ReservationStatus.Active)
                    .SumAsync(r => r.Quantity);

                Assert.Equal(batch.ReservedQuantity, activeReservationsSum);

                // 3. Invariant: Available quantity >= 0
                Assert.True(batch.PhysicalQuantity - batch.ReservedQuantity >= 0);

                // 4. Invariant: Status corresponds to quantities
                if (batch.PhysicalQuantity == 0)
                {
                    Assert.Equal(BatchStatus.Depleted, batch.Status);
                }
                else
                {
                    Assert.Equal(BatchStatus.Released, batch.Status);
                }

                // 5. Invariant: Ledger continuity
                var movements = await db.StockMovements
                    .Where(m => m.InventoryBatchId == batch.Id)
                    .OrderBy(m => m.CreatedAtUtc)
                    .ThenBy(m => m.Id)
                    .ToListAsync();

                Assert.NotEmpty(movements);
                Assert.Equal(StockMovementType.Receipt, movements[0].MovementType);
                Assert.Equal(batch.InitialQuantity, movements[0].QuantityChange);
                Assert.Equal(batch.InitialQuantity, movements[0].BatchBalanceAfter);

                // Last movement batch balance matches physical quantity
                var lastMovement = movements[^1];
                Assert.Equal(batch.PhysicalQuantity, lastMovement.BatchBalanceAfter);
            }
        }
    }

    [Fact]
    public async Task SeedAsync_ConfirmedOrderCanBeFulfilledViaApiEndpoint()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        await AbonStockDataSeeder.SeedAsync(db);

        var ord2 = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoConfirmed);

        Assert.NotNull(ord2);
        Assert.Equal(OrderStatus.Confirmed, ord2.Status);

        var bSo02Pre = await db.InventoryBatches.FirstAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriReleased);
        var bSp01Pre = await db.InventoryBatches.FirstAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiPdsReleased);
        var pOriPre = await db.Products.FirstAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiOri);
        var pPdsPre = await db.Products.FirstAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiPds);

        var so02InitialPhys = bSo02Pre.PhysicalQuantity;
        var sp01InitialPhys = bSp01Pre.PhysicalQuantity;
        var pOriInitialQoh = pOriPre.QuantityOnHand;
        var pPdsInitialQoh = pPdsPre.QuantityOnHand;

        // Fulfill via HTTP endpoint POST /api/orders/{id}/fulfill
        var fulfillRequest = new FulfillOrderRequest();
        var response = await _client.PostAsJsonAsync($"/api/orders/{ord2.Id}/fulfill", fulfillRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fulfilledOrder = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(fulfilledOrder);
        Assert.Equal("Completed", fulfilledOrder.Status);
        Assert.NotNull(fulfilledOrder.CompletedAtUtc);

        // Verify Database state post-fulfillment
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var orderPost = await verifyDb.Orders.FirstAsync(o => o.Id == ord2.Id);
        Assert.Equal(OrderStatus.Completed, orderPost.Status);
        Assert.NotNull(orderPost.CompletedAtUtc);

        // Reservations for Order 2 must be Fulfilled
        var reservationsPost = await verifyDb.OrderReservations.Where(r => r.OrderId == ord2.Id).ToListAsync();
        Assert.Equal(2, reservationsPost.Count);
        Assert.All(reservationsPost, r => Assert.Equal(ReservationStatus.Fulfilled, r.Status));

        // Batch SO02: Physical decreased by 20, Reserved decreased by 20 (now 0)
        var bSo02Post = await verifyDb.InventoryBatches.FirstAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiOriReleased);
        Assert.Equal(so02InitialPhys - 20, bSo02Post.PhysicalQuantity);
        Assert.Equal(0, bSo02Post.ReservedQuantity);

        // Batch SP01: Physical decreased by 15, Reserved decreased by 15 (now 0)
        var bSp01Post = await verifyDb.InventoryBatches.FirstAsync(b => b.BatchNo == AbonStockDataSeeder.BatchNoSapiPdsReleased);
        Assert.Equal(sp01InitialPhys - 15, bSp01Post.PhysicalQuantity);
        Assert.Equal(0, bSp01Post.ReservedQuantity);

        // Product QuantityOnHand reflects physical stock deduction
        var pOriPost = await verifyDb.Products.FirstAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiOri);
        var pPdsPost = await verifyDb.Products.FirstAsync(p => p.Sku == AbonStockDataSeeder.SkuAbonSapiPds);
        Assert.Equal(pOriInitialQoh - 20, pOriPost.QuantityOnHand);
        Assert.Equal(pPdsInitialQoh - 15, pPdsPost.QuantityOnHand);

        // StockMovements of MovementType 3 (OrderDeduction) created
        var deductionMovements = await verifyDb.StockMovements
            .Where(m => m.ReferenceOrderId == ord2.Id && m.MovementType == StockMovementType.OrderDeduction)
            .ToListAsync();
        Assert.Equal(2, deductionMovements.Count);
        Assert.Contains(deductionMovements, m => m.InventoryBatchId == bSo02Post.Id && m.QuantityChange == -20);
        Assert.Contains(deductionMovements, m => m.InventoryBatchId == bSp01Post.Id && m.QuantityChange == -15);
    }

    [Fact]
    public async Task SeedAsync_CalledAfterFulfillment_PreservesFulfilledStateAndAvoidsDataReset()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        await AbonStockDataSeeder.SeedAsync(db);

        var ord2 = await db.Orders.FirstAsync(o => o.OrderNumber == AbonStockDataSeeder.OrderNoConfirmed);

        // Fulfill Order 2
        var fulfillRequest = new FulfillOrderRequest();
        var response = await _client.PostAsJsonAsync($"/api/orders/{ord2.Id}/fulfill", fulfillRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Re-execute seeder
        await AbonStockDataSeeder.SeedAsync(db);

        // Verify Order 2 remains Completed and is not reset in fresh scope
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var ord2Post = await verifyDb.Orders.FirstAsync(o => o.Id == ord2.Id);
        Assert.Equal(OrderStatus.Completed, ord2Post.Status);

        // Verify no duplicate products were added
        var productCount = await verifyDb.Products.CountAsync(p =>
            p.Sku == AbonStockDataSeeder.SkuAbonSapiOri ||
            p.Sku == AbonStockDataSeeder.SkuAbonSapiPds ||
            p.Sku == AbonStockDataSeeder.SkuAbonAyamGrh);
        Assert.Equal(3, productCount);
    }
}
