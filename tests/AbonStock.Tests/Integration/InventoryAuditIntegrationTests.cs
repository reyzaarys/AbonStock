using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Integration;

public class InventoryAuditIntegrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();
    private readonly PostgresTestFixture _fixture = fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task<ProductResponse> CreateTestProductAsync(string prefix = "AUD-PROD")
    {
        var request = new CreateProductRequest
        {
            Sku = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Audit Test Product",
            UnitPrice = 30.00m
        };

        var response = await _client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(product);
        return product;
    }

    [Fact]
    public async Task Audit_WithConsistentBatches_ReturnsConsistentAndValidLedger()
    {
        var product = await CreateTestProductAsync("AUD-OK");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-AUDOK-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
            Quantity = 100
        };

        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        var relReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion
        };
        var relResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", relReq);
        var released = await relResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(released);

        // Perform partial write-off of 25
        var wroReq = new WriteOffBatchRequest
        {
            Quantity = 25,
            Reason = "Pest sampling write-off",
            DisposalDocumentNumber = "DISP-AUD-001",
            BatchRowVersion = released.Version
        };
        var wroResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", wroReq);
        Assert.Equal(HttpStatusCode.OK, wroResp.StatusCode);

        // Execute Audit targeting this product
        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.Equal(1, audit.TotalProductsChecked);
        Assert.Equal(1, audit.ConsistentProductsCount);
        Assert.Equal(0, audit.InconsistentProductsCount);
        Assert.Empty(audit.Discrepancies);
        Assert.Empty(audit.InvariantViolations);
        Assert.Equal("Valid", audit.LedgerAudit.Status);
        Assert.True(audit.IsSystemConsistent);
        Assert.NotEmpty(audit.LedgerAudit.Limitations);
    }

    [Fact]
    public async Task Audit_WithProductWithoutBatches_EvaluatesAsConsistentIfZeroStock()
    {
        var product = await CreateTestProductAsync("AUD-NOBAT");

        // Target product directly
        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.Equal(1, audit.TotalProductsChecked);
        Assert.Equal(1, audit.ConsistentProductsCount);
        Assert.Equal(0, audit.InconsistentProductsCount);
        Assert.Empty(audit.Discrepancies);
        Assert.Empty(audit.InvariantViolations);
        Assert.Equal("Valid", audit.LedgerAudit.Status);
        Assert.True(audit.IsSystemConsistent);

        // Global audit should also confirm no discrepancy for this product
        var globalResp = await _client.GetAsync("/api/inventory/audit");
        Assert.Equal(HttpStatusCode.OK, globalResp.StatusCode);
        var globalAudit = await globalResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(globalAudit);
        Assert.Null(globalAudit.Discrepancies.FirstOrDefault(d => d.ProductId == product.Id));
    }

    [Fact]
    public async Task Audit_WithProductBalanceDiscrepancy_DetectsDiscrepancyAndCompromisedLedger()
    {
        var product = await CreateTestProductAsync("AUD-DISC");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-DISC-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 50
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        Assert.Equal(HttpStatusCode.Created, recResp.StatusCode);

        // Artificially corrupt Product.QuantityOnHand by +30 via database query
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Products\" SET \"QuantityOnHand\" = 80 WHERE \"Id\" = {product.Id}");
        }

        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.Equal(1, audit.TotalProductsChecked);
        Assert.Equal(0, audit.ConsistentProductsCount);
        Assert.Equal(1, audit.InconsistentProductsCount);
        Assert.False(audit.IsSystemConsistent);

        var productDiscrepancy = audit.Discrepancies.FirstOrDefault(d => d.ProductId == product.Id);
        Assert.NotNull(productDiscrepancy);
        Assert.Equal(50, productDiscrepancy.ExpectedQuantityOnHand);
        Assert.Equal(80, productDiscrepancy.ActualQuantityOnHand);
        Assert.Equal(30, productDiscrepancy.Discrepancy);
        Assert.False(productDiscrepancy.IsConsistent);

        Assert.Equal("Compromised", audit.LedgerAudit.Status);
        Assert.Contains(audit.LedgerAudit.Issues, i => i.Contains(product.Sku) && i.Contains("final ledger balance"));
    }

    [Fact]
    public async Task Audit_WithBatchInvariantViolations_DetectsViolations()
    {
        var product = await CreateTestProductAsync("AUD-BATVIOL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-VIOL-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 40
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Inject invariant anomalies directly into this batch via database
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            // Anomaly 1: Status Quarantined with ReservedQuantity = 10 (violates QuarantinedWithActiveReservations)
            // Anomaly 2: ReservedQuantity = 10 while database active reservations sum is 0 (violates ReservationSumMismatch)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"InventoryBatches\" SET \"Status\" = {(int)BatchStatus.Quarantined}, \"ReservedQuantity\" = 10 WHERE \"Id\" = {receipt.BatchId}");
        }

        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.False(audit.IsSystemConsistent);

        var qrtViolation = audit.InvariantViolations.FirstOrDefault(v => v.BatchId == receipt.BatchId && v.ViolationType == "QuarantinedWithActiveReservations");
        Assert.NotNull(qrtViolation);

        var resMismatch = audit.InvariantViolations.FirstOrDefault(v => v.BatchId == receipt.BatchId && v.ViolationType == "ReservationSumMismatch");
        Assert.NotNull(resMismatch);
    }

    [Fact]
    public async Task Audit_WithUnrecordedMovements_ReportsUnverifiableStatus()
    {
        var product = await CreateTestProductAsync("AUD-UNREC");

        // Insert batch directly without any StockMovement
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var ghostBatch = new InventoryBatch
            {
                ProductId = product.Id,
                BatchNo = $"BAT-GHOST-{Guid.NewGuid().ToString("N")[..6]}",
                ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
                InitialQuantity = 50,
                PhysicalQuantity = 50,
                ReservedQuantity = 0,
                Status = BatchStatus.Released
            };
            db.InventoryBatches.Add(ghostBatch);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Products\" SET \"QuantityOnHand\" = 50 WHERE \"Id\" = {product.Id}");
        }

        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.False(audit.IsSystemConsistent);

        // Product and batch balances match (both 50), so discrepancies count is 0
        Assert.Equal(0, audit.InconsistentProductsCount);

        // But ledger is UNVERIFIABLE because zero movements exist for this stock
        Assert.Equal("Unverifiable", audit.LedgerAudit.Status);
        Assert.Contains(audit.LedgerAudit.Issues, i => i.Contains(product.Sku) && i.Contains("zero stock movements exist"));
    }

    [Fact]
    public async Task Audit_WithNonExistentProductId_ReturnsNotFound404()
    {
        var resp = await _client.GetAsync("/api/inventory/audit?productId=999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Audit_DoesNotMutateAnyData()
    {
        var product = await CreateTestProductAsync("AUD-NOMUT");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-NOMUT-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
            Quantity = 75
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Snapshot entity state before running audits
        Product productBefore;
        InventoryBatch batchBefore;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            productBefore = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
            batchBefore = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == receipt.BatchId);
        }

        // Call Audit multiple times (both global scan and product-targeted)
        var resp1 = await _client.GetAsync("/api/inventory/audit");
        var resp2 = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        // Verify entity state after audit is 100% untouched
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
            var productAfter = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
            var batchAfter = await db.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == receipt.BatchId);

            Assert.Equal(productBefore.QuantityOnHand, productAfter.QuantityOnHand);
            Assert.Equal(productBefore.Version, productAfter.Version);
            Assert.Equal(productBefore.UpdatedAtUtc, productAfter.UpdatedAtUtc);

            Assert.Equal(batchBefore.PhysicalQuantity, batchAfter.PhysicalQuantity);
            Assert.Equal(batchBefore.ReservedQuantity, batchAfter.ReservedQuantity);
            Assert.Equal(batchBefore.Version, batchAfter.Version);
            Assert.Equal(batchBefore.UpdatedAtUtc, batchAfter.UpdatedAtUtc);
        }
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=abonstock_db;Username=abonstock;Password=test")]
    [InlineData("Host=localhost;Port=5432;Database=AbonStock_Db;Username=abonstock;Password=test")]
    [InlineData("Host=localhost;Port=5432;Database=production_db;Username=abonstock;Password=test")]
    [InlineData("Host=localhost;Port=5432;Database=some_other_db;Username=abonstock;Password=test")]
    public void DatabaseGuard_RejectsNonTestDatabase(string nonTestConnectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PostgresTestFixture.ValidateTestDatabase(nonTestConnectionString));

        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void DatabaseGuard_AcceptsValidTestDatabase()
    {
        const string validTestConnectionString =
            "Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=test";

        var exception = Record.Exception(() =>
            PostgresTestFixture.ValidateTestDatabase(validTestConnectionString));

        Assert.Null(exception);
    }
}
