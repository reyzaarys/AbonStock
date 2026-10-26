using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbonStock.Api.Common;
using AbonStock.Api.Data;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Dtos.Orders;
using AbonStock.Api.Dtos.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Integration;

public class InventoryIntegrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();
    private readonly PostgresTestFixture _fixture = fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task<ProductResponse> CreateTestProductAsync(string prefix = "INV-PROD")
    {
        var request = new CreateProductRequest
        {
            Sku = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Inventory Test Product",
            UnitPrice = 25.00m
        };

        var response = await _client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(product);
        return product;
    }

    [Fact]
    public async Task ReceiveStock_WithValidData_CreatesBatchAndIncreasesProductStockAndWritesLedger()
    {
        var product = await CreateTestProductAsync("REC-OK");
        var batchNo = $"BAT-{Guid.NewGuid().ToString("N")[..8]}";
        var prodDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var expDate = prodDate.AddMonths(6);

        var request = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = batchNo.ToLowerInvariant(), // test lowercase normalization
            ProductionDate = prodDate,
            ExpiryDate = expDate,
            Quantity = 100,
            Reason = "Initial production receipt"
        };

        var response = await _client.PostAsJsonAsync("/api/inventory/receipts", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);
        Assert.True(receipt.BatchId > 0);
        Assert.Equal(product.Id, receipt.ProductId);
        Assert.Equal(batchNo.ToUpperInvariant(), receipt.BatchNo);
        Assert.Equal("PendingRelease", receipt.Status);
        Assert.Equal(100, receipt.InitialQuantity);
        Assert.Equal(100, receipt.PhysicalQuantity);
        Assert.Equal(100, receipt.TotalProductQuantityOnHand);
        Assert.True(receipt.BatchVersion > 0);
        Assert.True(receipt.ProductVersion > 0);

        // Verify Product QuantityOnHand updated
        var productResponse = await _client.GetAsync($"/api/products/{product.Id}");
        var updatedProduct = await productResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(updatedProduct);
        Assert.Equal(100, updatedProduct.QuantityOnHand);

        // Verify StockMovement recorded
        var movResponse = await _client.GetAsync($"/api/inventory/movements?batchId={receipt.BatchId}");
        Assert.Equal(HttpStatusCode.OK, movResponse.StatusCode);
        var pagedMovements = await movResponse.Content.ReadFromJsonAsync<PagedResult<StockMovementResponse>>(JsonOptions);
        Assert.NotNull(pagedMovements);
        Assert.Single(pagedMovements.Items);

        var mov = pagedMovements.Items[0];
        Assert.Equal(product.Id, mov.ProductId);
        Assert.Equal(receipt.BatchId, mov.InventoryBatchId);
        Assert.Equal(batchNo.ToUpperInvariant(), mov.BatchNo);
        Assert.Equal("Receipt", mov.MovementType);
        Assert.Equal(100, mov.QuantityChange);
        Assert.Equal(100, mov.BalanceAfter);
        Assert.Equal(100, mov.BatchBalanceAfter);
        Assert.Equal("Initial production receipt", mov.Reason);
    }

    [Fact]
    public async Task ReceiveStock_WithDuplicateBatch_ReturnsConflictProblemDetails()
    {
        var product = await CreateTestProductAsync("REC-DUP");
        var batchNo = $"DUP-BAT-{Guid.NewGuid().ToString("N")[..8]}";
        var prodDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var request1 = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = batchNo,
            ProductionDate = prodDate,
            Quantity = 50
        };

        var response1 = await _client.PostAsJsonAsync("/api/inventory/receipts", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Second receipt with same batch number (different casing/whitespace)
        var request2 = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"  {batchNo.ToLowerInvariant()}  ",
            ProductionDate = prodDate,
            Quantity = 50
        };

        var response2 = await _client.PostAsJsonAsync("/api/inventory/receipts", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        var problem = await response2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("already exists", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveStock_WithUnsupportedLocation_ReturnsBadRequestProblemDetails()
    {
        var product = await CreateTestProductAsync("REC-LOC");
        var request = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            LocationId = "LOC_SBY_DIST",
            Quantity = 20
        };

        var response = await _client.PostAsJsonAsync("/api/inventory/receipts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("LOC_KDR_PROD", problem.Detail);
    }

    [Fact]
    public async Task ReceiveStock_WithExpiryBeforeProductionDate_ReturnsBadRequestProblemDetails()
    {
        var product = await CreateTestProductAsync("REC-EXP");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = today,
            ExpiryDate = today.AddDays(-1),
            Quantity = 20
        };

        var response = await _client.PostAsJsonAsync("/api/inventory/receipts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("earlier than ProductionDate", problem.Detail);
    }

    [Fact]
    public async Task ReleaseBatch_WithPassingQualityChecks_TransitionsToReleased()
    {
        var product = await CreateTestProductAsync("REL-OK");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"REL-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 80
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Attempt release with failing check
        var failReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = false,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion
        };
        var failResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", failReq);
        Assert.Equal(HttpStatusCode.BadRequest, failResp.StatusCode);

        // Release with all checks passed
        var passReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion,
            Notes = "Passed visual inspection, pleasant aroma, packaging sealed"
        };
        var passResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", passReq);
        Assert.Equal(HttpStatusCode.OK, passResp.StatusCode);
        var released = await passResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(released);
        Assert.Equal("Released", released.Status);
        Assert.NotEqual(receipt.BatchVersion, released.Version);

        // Re-releasing already released batch returns 409
        var retryReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = released.Version
        };
        var retryResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", retryReq);
        Assert.Equal(HttpStatusCode.Conflict, retryResp.StatusCode);
        var problem = await retryResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("already released", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdjustStock_OnPendingReleaseBatch_ReturnsConflictProblemDetails()
    {
        var product = await CreateTestProductAsync("ADJ-PEND");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"PEND-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 50
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        var adjReq = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -5,
            Reason = "Premature adjustment test",
            BatchRowVersion = receipt.BatchVersion
        };
        var adjResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", adjReq);
        Assert.Equal(HttpStatusCode.Conflict, adjResp.StatusCode);
        var problem = await adjResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Only Released batches can be adjusted", problem.Detail);
    }

    [Fact]
    public async Task AdjustStock_PositiveAndNegative_UpdatesBalancesAndWritesLedger()
    {
        var product = await CreateTestProductAsync("ADJ-POSNEG");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"ADJ-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 100
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Release batch
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

        // Negative adjustment: -10
        var negAdjReq = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -10,
            Reason = "Damaged during warehouse handling",
            BatchRowVersion = released.Version
        };
        var negResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", negAdjReq);
        Assert.Equal(HttpStatusCode.OK, negResp.StatusCode);
        var negResult = await negResp.Content.ReadFromJsonAsync<StockAdjustmentResponse>(JsonOptions);
        Assert.NotNull(negResult);
        Assert.Equal(100, negResult.PreviousPhysicalQuantity);
        Assert.Equal(90, negResult.NewPhysicalQuantity);
        Assert.Equal(100, negResult.PreviousProductQuantityOnHand);
        Assert.Equal(90, negResult.NewProductQuantityOnHand);
        Assert.Equal("Released", negResult.Status);

        // Positive adjustment: +5
        var posAdjReq = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = 5,
            Reason = "Found uncounted unit during recount",
            BatchRowVersion = negResult.BatchVersion
        };
        var posResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", posAdjReq);
        Assert.Equal(HttpStatusCode.OK, posResp.StatusCode);
        var posResult = await posResp.Content.ReadFromJsonAsync<StockAdjustmentResponse>(JsonOptions);
        Assert.NotNull(posResult);
        Assert.Equal(90, posResult.PreviousPhysicalQuantity);
        Assert.Equal(95, posResult.NewPhysicalQuantity);
        Assert.Equal(90, posResult.PreviousProductQuantityOnHand);
        Assert.Equal(95, posResult.NewProductQuantityOnHand);

        // Verify Movement ledger history
        var movResp = await _client.GetAsync($"/api/inventory/movements?batchId={receipt.BatchId}");
        var movements = await movResp.Content.ReadFromJsonAsync<PagedResult<StockMovementResponse>>(JsonOptions);
        Assert.NotNull(movements);
        Assert.Equal(3, movements.TotalCount);

        // Verify deterministic sorting: newest first
        var latestMov = movements.Items[0];
        Assert.Equal("Adjustment", latestMov.MovementType);
        Assert.Equal(5, latestMov.QuantityChange);
        Assert.Equal(95, latestMov.BalanceAfter);
        Assert.Equal(95, latestMov.BatchBalanceAfter);
    }

    [Fact]
    public async Task AdjustStock_ExceedingAvailableStock_ReturnsConflictProblemDetails()
    {
        var product = await CreateTestProductAsync("ADJ-NEGSTK");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"NEGSTK-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 30
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

        // Try to deduct 50 when only 30 available
        var excessiveAdj = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -50,
            Reason = "Excessive deduction",
            BatchRowVersion = released.Version
        };
        var response = await _client.PostAsJsonAsync("/api/inventory/adjustments", excessiveAdj);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("negative batch quantity", problem.Detail);
    }

    [Fact]
    public async Task AdjustStock_ReducingBatchToZero_TransitionsToDepletedAndRejectsFurtherAdjustment()
    {
        var product = await CreateTestProductAsync("ADJ-DEPL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"DEPL-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 40
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

        // Deduct full quantity (40)
        var zeroAdj = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -40,
            Reason = "Batch depleted completely",
            BatchRowVersion = released.Version
        };
        var zeroResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", zeroAdj);
        Assert.Equal(HttpStatusCode.OK, zeroResp.StatusCode);
        var depletedResult = await zeroResp.Content.ReadFromJsonAsync<StockAdjustmentResponse>(JsonOptions);
        Assert.NotNull(depletedResult);
        Assert.Equal(0, depletedResult.NewPhysicalQuantity);
        Assert.Equal("Depleted", depletedResult.Status);

        // Attempting to adjust a depleted batch must be rejected (BR-LOT-02)
        var attemptRestore = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = 10,
            Reason = "Attempt to restore depleted batch",
            BatchRowVersion = depletedResult.BatchVersion
        };
        var restoreResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", attemptRestore);
        Assert.Equal(HttpStatusCode.Conflict, restoreResp.StatusCode);
        var problem = await restoreResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Cannot adjust a depleted batch", problem.Detail);
    }

    [Fact]
    public async Task AdjustStock_WithStaleBatchRowVersion_ReturnsConflictConcurrencyProblemDetails()
    {
        var product = await CreateTestProductAsync("ADJ-CONCUR-SAME");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"CONCUR-SAME-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
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

        var staleVersion = released.Version;

        // First adjustment succeeds
        var adj1 = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -10,
            Reason = "First adjustment",
            BatchRowVersion = staleVersion
        };
        var resp1 = await _client.PostAsJsonAsync("/api/inventory/adjustments", adj1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Second adjustment with stale version fails
        var adj2 = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -5,
            Reason = "Second adjustment with stale version",
            BatchRowVersion = staleVersion
        };
        var resp2 = await _client.PostAsJsonAsync("/api/inventory/adjustments", adj2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
        var problem = await resp2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal("Concurrency Conflict", problem.Title);
    }

    [Fact]
    public async Task AdjustStock_OnDifferentBatchesOfSameProduct_ProtectsProductAggregateConsistency()
    {
        var product = await CreateTestProductAsync("ADJ-PROD-CONCUR");

        // Create Batch 1 (qty 60)
        var rec1 = await _client.PostAsJsonAsync("/api/inventory/receipts", new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BATCH-1-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 60
        });
        var b1Receipt = await rec1.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(b1Receipt);

        // Create Batch 2 (qty 40)
        var rec2 = await _client.PostAsJsonAsync("/api/inventory/receipts", new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BATCH-2-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 40
        });
        var b2Receipt = await rec2.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(b2Receipt);

        // Release Batch 1
        var rel1 = await _client.PostAsJsonAsync($"/api/inventory/batches/{b1Receipt.BatchId}/release", new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = b1Receipt.BatchVersion
        });
        var b1Released = await rel1.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b1Released);

        // Release Batch 2
        var rel2 = await _client.PostAsJsonAsync($"/api/inventory/batches/{b2Receipt.BatchId}/release", new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = b2Receipt.BatchVersion
        });
        var b2Released = await rel2.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b2Released);

        // Fetch fresh product state
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var currentProduct = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(currentProduct);
        Assert.Equal(100, currentProduct.QuantityOnHand);

        // Prepare 2 concurrent adjustments on DIFFERENT batches of the same product
        var task1 = _client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest
        {
            BatchId = b1Released.Id,
            QuantityChange = -15,
            Reason = "Adjustment on Batch 1",
            BatchRowVersion = b1Released.Version,
            ProductRowVersion = currentProduct.Version
        });

        var task2 = _client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest
        {
            BatchId = b2Released.Id,
            QuantityChange = -10,
            Reason = "Adjustment on Batch 2",
            BatchRowVersion = b2Released.Version,
            ProductRowVersion = currentProduct.Version
        });

        var responses = await Task.WhenAll(task1, task2);

        // Concurrency token on Product guarantees that either:
        // Case A: Both serialize successfully (if both updates execute sequentially and detect updated xmin/refresh), or
        // Case B: One succeeds (200 OK) and the competing request gets HTTP 409 Conflict (Concurrency Conflict).
        // Under NO circumstance should lost update occur!
        var okResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        Assert.True(okResponses.Count >= 1, "At least one concurrent adjustment must succeed.");

        if (conflictResponses.Count > 0)
        {
            var problem = await conflictResponses[0].Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
            Assert.NotNull(problem);
            Assert.Equal("Concurrency Conflict", problem.Title);

            // Re-fetch product and retry the failed adjustment with fresh state
            var freshProductResp = await _client.GetAsync($"/api/products/{product.Id}");
            var freshProduct = await freshProductResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
            Assert.NotNull(freshProduct);

            // Determine which batch failed the concurrent request
            var (failedBatchId, failedBatchVersion, failedQty) = (responses[0].StatusCode == HttpStatusCode.Conflict)
                ? (b1Released.Id, b1Released.Version, -15)
                : (b2Released.Id, b2Released.Version, -10);

            var retryReq = new StockAdjustmentRequest
            {
                BatchId = failedBatchId,
                QuantityChange = failedQty,
                Reason = "Retry adjustment after conflict",
                BatchRowVersion = failedBatchVersion,
                ProductRowVersion = freshProduct.Version
            };
            var retryResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", retryReq);
            Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
        }

        // Final verification: Product.QuantityOnHand must strictly equal the sum of batch quantities
        var finalProdResp = await _client.GetAsync($"/api/products/{product.Id}");
        var finalProduct = await finalProdResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(finalProduct);

        var b1FinalResp = await _client.GetAsync($"/api/inventory/batches/{b1Released.Id}");
        var b1Final = await b1FinalResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b1Final);

        var b2FinalResp = await _client.GetAsync($"/api/inventory/batches/{b2Released.Id}");
        var b2Final = await b2FinalResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b2Final);

        Assert.Equal(finalProduct.QuantityOnHand, b1Final.PhysicalQuantity + b2Final.PhysicalQuantity);
        Assert.Equal(75, finalProduct.QuantityOnHand); // (60 - 15) + (40 - 10) = 45 + 30 = 75
    }

    [Fact]
    public async Task AdjustStock_NegativeAdjustmentBelowReservedQuantity_ReturnsConflictProblemDetails()
    {
        var product = await CreateTestProductAsync("ADJ-RES");
        var batchNo = $"BAT-RES-{Guid.NewGuid().ToString("N")[..8]}";
        var prodDate = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Receive batch with 50 units
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = batchNo,
            ProductionDate = prodDate,
            Quantity = 50,
            Reason = "Initial receipt for reserved adjustment test"
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        Assert.Equal(HttpStatusCode.Created, recResp.StatusCode);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // 2. Release batch
        var releaseReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion,
            Notes = "Passed QC"
        };
        var relResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", releaseReq);
        Assert.Equal(HttpStatusCode.OK, relResp.StatusCode);
        var releasedBatch = await relResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(releasedBatch);

        // 3. Create and confirm an order for 30 units to establish ReservedQuantity = 30
        var orderReq = new CreateOrderRequest
        {
            CustomerName = "Reserved Test Customer",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 30 }
            }
        };
        var orderResp = await _client.PostAsJsonAsync("/api/orders", orderReq);
        Assert.Equal(HttpStatusCode.Created, orderResp.StatusCode);
        var order = await orderResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest());
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // 4. Verify batch in DB after reservation
        using var scope1 = _fixture.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AbonStockDbContext>();
        var dbBatchPreAdj = await db1.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == receipt.BatchId);
        Assert.Equal(50, dbBatchPreAdj.PhysicalQuantity);
        Assert.Equal(30, dbBatchPreAdj.ReservedQuantity);

        var initialMovementsCount = await db1.StockMovements.CountAsync(m => m.ProductId == product.Id);

        // 5. Attempt negative adjustment of -25 (would make physical quantity 25 < 30 reserved)
        var adjReq = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -25,
            Reason = "Negative adjustment below reserved quantity should fail",
            BatchRowVersion = dbBatchPreAdj.Version
        };
        var adjResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", adjReq);

        // 6. Assert HTTP 409 Conflict with descriptive domain error message
        Assert.Equal(HttpStatusCode.Conflict, adjResp.StatusCode);
        var problem = await adjResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("below reserved quantity", problem.Detail, StringComparison.OrdinalIgnoreCase);

        // 7. Verify no partial changes in DB
        using var scope2 = _fixture.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var dbBatch = await db2.InventoryBatches.AsNoTracking().FirstAsync(b => b.Id == receipt.BatchId);
        Assert.Equal(50, dbBatch.PhysicalQuantity);
        Assert.Equal(30, dbBatch.ReservedQuantity);

        var dbProduct = await db2.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(50, dbProduct.QuantityOnHand);

        var finalMovementsCount = await db2.StockMovements.CountAsync(m => m.ProductId == product.Id);
        Assert.Equal(initialMovementsCount, finalMovementsCount);
    }

    [Fact]
    public async Task GetBatches_WithPaginationAndFiltering_ReturnsPagedResultAndProductInfo()
    {
        var product = await CreateTestProductAsync("BATCH-LIST");

        var prodDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var expDate = prodDate.AddMonths(6);

        var receiptReq1 = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-L1-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = prodDate,
            ExpiryDate = expDate,
            Quantity = 40
        };
        var r1 = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq1);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var receiptReq2 = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-L2-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = prodDate.AddDays(1),
            ExpiryDate = expDate.AddDays(1),
            Quantity = 60
        };
        var r2 = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq2);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

        // Fetch batches filtered by ProductId
        var response = await _client.GetAsync($"/api/inventory/batches?productId={product.Id}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
        Assert.Equal(2, pagedResult.TotalCount);
        Assert.Equal(2, pagedResult.Items.Count);
        Assert.Equal(1, pagedResult.Page);
        Assert.Equal(10, pagedResult.PageSize);

        // Verify product info mapping on each batch
        Assert.All(pagedResult.Items, b =>
        {
            Assert.Equal(product.Id, b.ProductId);
            Assert.Equal(product.Sku, b.ProductSku);
            Assert.Equal(product.Name, b.ProductName);
            Assert.Equal(0, b.ReservedQuantity);
            Assert.Equal(b.PhysicalQuantity, b.AvailableQuantity);
        });
    }

    [Fact]
    public async Task GetBatches_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        var product = await CreateTestProductAsync("BATCH-STAT");
        var prodDate = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Create PendingRelease batch
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-PND-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = prodDate,
            Quantity = 30
        };
        var r = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var receipt = await r.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // 2. Query for PendingRelease
        var respPending = await _client.GetAsync($"/api/inventory/batches?productId={product.Id}&status=PendingRelease");
        Assert.Equal(HttpStatusCode.OK, respPending.StatusCode);
        var pendingResult = await respPending.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(pendingResult);
        Assert.Single(pendingResult.Items);
        Assert.Equal("PendingRelease", pendingResult.Items[0].Status);

        // 3. Query for Released (should be empty for this product)
        var respReleased = await _client.GetAsync($"/api/inventory/batches?productId={product.Id}&status=Released");
        Assert.Equal(HttpStatusCode.OK, respReleased.StatusCode);
        var releasedResult = await respReleased.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(releasedResult);
        Assert.Empty(releasedResult.Items);
    }

    [Fact]
    public async Task GetBatches_WithSorting_OrdersCorrectly()
    {
        var response = await _client.GetAsync("/api/inventory/batches?sortBy=productionDate&sortDescending=true&page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
    }

    [Fact]
    public async Task GetBatches_WithInvalidPagination_ReturnsBadRequest()
    {
        // Page < 1
        var respZeroPage = await _client.GetAsync("/api/inventory/batches?page=0&pageSize=10");
        Assert.Equal(HttpStatusCode.BadRequest, respZeroPage.StatusCode);

        // PageSize > 100
        var respLargePageSize = await _client.GetAsync("/api/inventory/batches?page=1&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, respLargePageSize.StatusCode);
    }

    [Fact]
    public async Task GetBatches_WhenNoMatch_ReturnsEmptyPagedResult()
    {
        var response = await _client.GetAsync("/api/inventory/batches?productId=999999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
        Assert.Empty(pagedResult.Items);
        Assert.Equal(0, pagedResult.TotalCount);
    }

    [Fact]
    public async Task GetBatches_WithSearchFilter_MatchesBatchNoAndProductName()
    {
        var product = await CreateTestProductAsync("BATCH-SRCH");
        var batchNo = $"BAT-SRCH-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = batchNo,
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 25
        };
        var r = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        // Search by BatchNo snippet (case-insensitive)
        var respSearch = await _client.GetAsync($"/api/inventory/batches?search={batchNo[..8].ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, respSearch.StatusCode);
        var searchResult = await respSearch.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(searchResult);
        Assert.Contains(searchResult.Items, b => b.BatchNo == batchNo);
    }

    [Fact]
    public async Task GetBatches_WithUnknownSortBy_FallsBackToDefaultSorting()
    {
        var response = await _client.GetAsync("/api/inventory/batches?sortBy=unrecognizedColumn&page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<BatchResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
    }
}
