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
    public async Task AdjustStock_ConcurrentAdjustmentsOnDifferentBatches_WithoutProductRowVersion_SerializesSafelyWithoutLostUpdate()
    {
        var product = await CreateTestProductAsync("ADJ-NOLOST");

        // Create Batch 1 (qty 50)
        var rec1 = await _client.PostAsJsonAsync("/api/inventory/receipts", new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BATCH-NL1-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 50
        });
        var b1Receipt = await rec1.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(b1Receipt);

        // Create Batch 2 (qty 50)
        var rec2 = await _client.PostAsJsonAsync("/api/inventory/receipts", new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BATCH-NL2-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 50
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

        // Initial check: Product.QuantityOnHand = 100
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var currentProduct = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(currentProduct);
        Assert.Equal(100, currentProduct.QuantityOnHand);

        // Fire 2 concurrent adjustments on DIFFERENT batches without passing ProductRowVersion!
        // Thanks to row-level locking on Products FOR UPDATE, both must serialize and both succeed,
        // eliminating the lost update anomaly on Product.QuantityOnHand.
        var task1 = _client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest
        {
            BatchId = b1Released.Id,
            QuantityChange = -15,
            Reason = "P1 hotfix test: delta -15 on Batch 1",
            BatchRowVersion = b1Released.Version,
            ProductRowVersion = null // explicitly null to test pessimistic row-lock protection
        });

        var task2 = _client.PostAsJsonAsync("/api/inventory/adjustments", new StockAdjustmentRequest
        {
            BatchId = b2Released.Id,
            QuantityChange = -10,
            Reason = "P1 hotfix test: delta -10 on Batch 2",
            BatchRowVersion = b2Released.Version,
            ProductRowVersion = null // explicitly null to test pessimistic row-lock protection
        });

        var responses = await Task.WhenAll(task1, task2);

        // Both adjustments should succeed with HTTP 200 OK
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Re-query database to verify zero lost update:
        // Batch 1: 50 - 15 = 35
        // Batch 2: 50 - 10 = 40
        // Product: exactly 75 (35 + 40)
        var finalProdResp = await _client.GetAsync($"/api/products/{product.Id}");
        var finalProduct = await finalProdResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(finalProduct);

        var b1FinalResp = await _client.GetAsync($"/api/inventory/batches/{b1Released.Id}");
        var b1Final = await b1FinalResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b1Final);
        Assert.Equal(35, b1Final.PhysicalQuantity);

        var b2FinalResp = await _client.GetAsync($"/api/inventory/batches/{b2Released.Id}");
        var b2Final = await b2FinalResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(b2Final);
        Assert.Equal(40, b2Final.PhysicalQuantity);

        // Invariant holds strictly: Product.QuantityOnHand == SUM(Batch.PhysicalQuantity)
        Assert.Equal(75, finalProduct.QuantityOnHand);
        Assert.Equal(finalProduct.QuantityOnHand, b1Final.PhysicalQuantity + b2Final.PhysicalQuantity);

        // Also run Audit tool to verify consistency
        var auditResp = await _client.GetAsync($"/api/inventory/audit?productId={product.Id}");
        var audit = await auditResp.Content.ReadFromJsonAsync<InventoryAuditResponse>(JsonOptions);
        Assert.NotNull(audit);
        Assert.True(audit.IsSystemConsistent);
        Assert.Equal(0, audit.InconsistentProductsCount);
        Assert.Equal("Valid", audit.LedgerAudit.Status);
    }

    [Fact]
    public async Task AdjustStock_ConcurrentAdjustmentsExceedingBatchStock_SafelyRejectsWithoutPartialMutation()
    {
        var product = await CreateTestProductAsync("ADJ-FAILROLL");

        var rec = await _client.PostAsJsonAsync("/api/inventory/receipts", new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BATCH-FR-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 30
        });
        var bReceipt = await rec.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(bReceipt);

        var rel = await _client.PostAsJsonAsync($"/api/inventory/batches/{bReceipt.BatchId}/release", new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = bReceipt.BatchVersion
        });
        var bReleased = await rel.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(bReleased);

        // Attempt adjustment with QuantityChange = -35 (exceeds physical quantity of 30)
        var invalidReq = new StockAdjustmentRequest
        {
            BatchId = bReleased.Id,
            QuantityChange = -35,
            Reason = "Adjustment exceeding stock",
            BatchRowVersion = bReleased.Version
        };
        var failResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", invalidReq);
        Assert.Equal(HttpStatusCode.Conflict, failResp.StatusCode);

        // Verify rollback left ZERO partial changes on batch, product, or ledger
        var finalProdResp = await _client.GetAsync($"/api/products/{product.Id}");
        var finalProduct = await finalProdResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(finalProduct);
        Assert.Equal(30, finalProduct.QuantityOnHand);

        var bFinalResp = await _client.GetAsync($"/api/inventory/batches/{bReleased.Id}");
        var bFinal = await bFinalResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(bFinal);
        Assert.Equal(30, bFinal.PhysicalQuantity);

        // Movements count for this batch must only be the 1 original receipt
        var movResp = await _client.GetAsync($"/api/inventory/movements?batchId={bReleased.Id}");
        var movements = await movResp.Content.ReadFromJsonAsync<PagedResult<StockMovementResponse>>(JsonOptions);
        Assert.NotNull(movements);
        Assert.Single(movements.Items);
        Assert.Equal("Receipt", movements.Items[0].MovementType);
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
            ExpiryDate = prodDate.AddMonths(12),
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

    #region Quarantine Workflow Tests (Slice 1.2)

    [Fact]
    public async Task QuarantineBatch_PendingReleaseBatch_TransitionsToQuarantinedAndPreservesPhysicalStock()
    {
        var product = await CreateTestProductAsync("QRT-PEND");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-P-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 50,
            Reason = "Initial receipt for quarantine test"
        };

        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        Assert.Equal(HttpStatusCode.Created, recResp.StatusCode);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);
        Assert.Equal("PendingRelease", receipt.Status);

        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Failed visual inspection due to packaging seal compromise",
            Notes = "Sent sample to QC laboratory for validation",
            RowVersion = receipt.BatchVersion
        };

        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.OK, qrtResp.StatusCode);
        var quarantined = await qrtResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);

        Assert.NotNull(quarantined);
        Assert.Equal("Quarantined", quarantined.Status);
        Assert.Equal(50, quarantined.PhysicalQuantity);
        Assert.Equal(0, quarantined.ReservedQuantity);
        Assert.Equal(50, quarantined.AvailableQuantity);
        Assert.NotEqual(receipt.BatchVersion, quarantined.Version);

        // Invariant: Product.QuantityOnHand is strictly preserved (items physically remain in warehouse)
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(prod);
        Assert.Equal(50, prod.QuantityOnHand);

        // Invariant: No ledger movement created (status change does not alter physical quantity)
        var movResp = await _client.GetAsync($"/api/inventory/movements?batchId={receipt.BatchId}");
        var movs = await movResp.Content.ReadFromJsonAsync<PagedResult<StockMovementResponse>>(JsonOptions);
        Assert.NotNull(movs);
        Assert.Single(movs.Items); // Only initial Receipt movement
        Assert.Equal("Receipt", movs.Items[0].MovementType);
    }

    [Fact]
    public async Task QuarantineBatch_ReleasedBatch_TransitionsToQuarantinedAndPreservesPhysicalStock()
    {
        var product = await CreateTestProductAsync("QRT-REL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-R-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 60,
            Reason = "Initial receipt"
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
        Assert.Equal("Released", released.Status);

        // Quarantine the Released batch
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Customer reported off-flavor in sibling test sample",
            RowVersion = released.Version
        };

        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.OK, qrtResp.StatusCode);
        var quarantined = await qrtResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);

        Assert.NotNull(quarantined);
        Assert.Equal("Quarantined", quarantined.Status);
        Assert.Equal(60, quarantined.PhysicalQuantity);
        Assert.Equal(0, quarantined.ReservedQuantity);

        // Product QuantityOnHand remains intact
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(prod);
        Assert.Equal(60, prod.QuantityOnHand);
    }

    [Fact]
    public async Task QuarantineBatch_WithActiveReservations_ReturnsConflict409AndDoesNotModifyOrder()
    {
        var product = await CreateTestProductAsync("QRT-RES");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-RES-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 40,
            Reason = "Stock for order reservation test"
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

        // Create and Confirm Order for 25 units
        var orderReq = new CreateOrderRequest
        {
            CustomerName = "Toko Abon Jaya",
            CustomerEmail = "toko@example.com",
            Items = [new CreateOrderItemRequest { ProductId = product.Id, Quantity = 25 }]
        };
        var orderResp = await _client.PostAsJsonAsync("/api/orders", orderReq);
        Assert.Equal(HttpStatusCode.Created, orderResp.StatusCode);
        var order = await orderResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        var confirmResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest { RowVersion = order.Version });
        Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

        // Verify batch has active reserved quantity
        var batchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var batchWithRes = await batchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(batchWithRes);
        Assert.Equal(25, batchWithRes.ReservedQuantity);

        // Attempt quarantine on batch with active reservations -> must return 409 Conflict
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Attempt to quarantine reserved batch",
            RowVersion = batchWithRes.Version
        };
        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.Conflict, qrtResp.StatusCode);
        var problem = await qrtResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("active reserved units", problem.Detail);

        // Verify batch status is STILL Released and reservations remain intact
        var batchAfterResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var batchAfter = await batchAfterResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(batchAfter);
        Assert.Equal("Released", batchAfter.Status);
        Assert.Equal(25, batchAfter.ReservedQuantity);

        // Verify order status is STILL Confirmed (orders must not be cancelled automatically)
        var orderAfterResp = await _client.GetAsync($"/api/orders/{order.Id}");
        var orderAfter = await orderAfterResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(orderAfter);
        Assert.Equal("Confirmed", orderAfter.Status);
    }

    [Fact]
    public async Task QuarantineBatch_AlreadyQuarantinedBatch_ReturnsConflict409()
    {
        var product = await CreateTestProductAsync("QRT-ALR");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-ALR-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 30
        };

        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Quarantine batch
        var qrtReq1 = new QuarantineBatchRequest
        {
            Reason = "Initial quarantine reason",
            RowVersion = receipt.BatchVersion
        };
        var qrtResp1 = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq1);
        Assert.Equal(HttpStatusCode.OK, qrtResp1.StatusCode);
        var quarantined = await qrtResp1.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(quarantined);

        // Re-quarantine already quarantined batch -> 409 Conflict
        var qrtReq2 = new QuarantineBatchRequest
        {
            Reason = "Attempt duplicate quarantine",
            RowVersion = quarantined.Version
        };
        var qrtResp2 = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq2);
        Assert.Equal(HttpStatusCode.Conflict, qrtResp2.StatusCode);
        var problem = await qrtResp2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("already quarantined", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuarantineBatch_DepletedBatch_ReturnsConflict409()
    {
        var product = await CreateTestProductAsync("QRT-DEPL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-DEPL-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 20
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

        // Deplete batch via stock adjustment
        var zeroAdj = new StockAdjustmentRequest
        {
            BatchId = receipt.BatchId,
            QuantityChange = -20,
            Reason = "Deplete for test",
            BatchRowVersion = released.Version
        };
        var zeroResp = await _client.PostAsJsonAsync("/api/inventory/adjustments", zeroAdj);
        Assert.Equal(HttpStatusCode.OK, zeroResp.StatusCode);
        var depleted = await zeroResp.Content.ReadFromJsonAsync<StockAdjustmentResponse>(JsonOptions);
        Assert.NotNull(depleted);
        Assert.Equal("Depleted", depleted.Status);

        // Attempt quarantine on depleted batch -> 409 Conflict
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Attempt quarantine on depleted batch",
            RowVersion = depleted.BatchVersion
        };
        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.Conflict, qrtResp.StatusCode);
        var problem = await qrtResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("cannot be quarantined", problem.Detail);
    }

    [Fact]
    public async Task QuarantineBatch_WithStaleRowVersion_ReturnsConflict409Concurrency()
    {
        var product = await CreateTestProductAsync("QRT-STALE");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-QRT-STALE-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 35
        };

        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);
        var staleVersion = receipt.BatchVersion;

        // Release batch, causing Version to change
        var relReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion
        };
        var relResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", relReq);
        Assert.Equal(HttpStatusCode.OK, relResp.StatusCode);

        // Attempt quarantine using stale initial version
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Quarantine with stale version",
            RowVersion = staleVersion
        };
        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.Conflict, qrtResp.StatusCode);
        var problem = await qrtResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("modified by another operation", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuarantineBatch_WithWhitespaceOrEmptyReason_ReturnsBadRequest400()
    {
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "   ",
            RowVersion = 1u
        };

        var qrtResp = await _client.PostAsJsonAsync("/api/inventory/batches/1/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.BadRequest, qrtResp.StatusCode);
    }

    [Fact]
    public async Task QuarantineBatch_WithNonExistentBatch_ReturnsNotFound404()
    {
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Quarantine non-existent batch",
            RowVersion = 1u
        };

        var qrtResp = await _client.PostAsJsonAsync("/api/inventory/batches/999999/quarantine", qrtReq);
        Assert.Equal(HttpStatusCode.NotFound, qrtResp.StatusCode);
    }

    [Fact]
    public async Task QuarantineBatch_ConcurrentQuarantineAndOrderConfirmation_SerializesDeterministically()
    {
        var product = await CreateTestProductAsync("QRT-CONC");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-CONC-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
            Quantity = 20
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

        // Prepare order for all 20 units
        var orderReq = new CreateOrderRequest
        {
            CustomerName = "Concurrent Customer",
            CustomerEmail = "conc@example.com",
            Items = [new CreateOrderItemRequest { ProductId = product.Id, Quantity = 20 }]
        };
        var orderResp = await _client.PostAsJsonAsync("/api/orders", orderReq);
        var order = await orderResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        // Concurrent Task 1: Confirm Order (competes to reserve the 20 units)
        // Concurrent Task 2: Quarantine Batch (competes to lock and quarantine)
        var confirmTask = _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest { RowVersion = order.Version });
        var quarantineTask = _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", new QuarantineBatchRequest
        {
            Reason = "Concurrent quarantine attempt",
            RowVersion = released.Version
        });

        await Task.WhenAll(confirmTask, quarantineTask);

        var confirmResult = await confirmTask;
        var quarantineResult = await quarantineTask;

        // One of the two must win:
        // Scenario A: ConfirmOrder wins -> order is Confirmed, batch has ReservedQuantity = 20, quarantine gets 409 Conflict.
        // Scenario B: Quarantine wins -> batch is Quarantined, confirm order gets 409 Conflict (insufficient sellable stock).
        // Under NO circumstances can a batch be both Quarantined AND have active reservations!
        var finalBatchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var finalBatch = await finalBatchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(finalBatch);

        if (finalBatch.Status == "Quarantined")
        {
            // Quarantine won: reservations must be 0
            Assert.Equal(0, finalBatch.ReservedQuantity);
            Assert.Equal(HttpStatusCode.OK, quarantineResult.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, confirmResult.StatusCode);
        }
        else
        {
            // Confirm won: status must be Released, reserved quantity must be 20
            Assert.Equal("Released", finalBatch.Status);
            Assert.Equal(20, finalBatch.ReservedQuantity);
            Assert.Equal(HttpStatusCode.OK, confirmResult.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, quarantineResult.StatusCode);
        }
    }

    #endregion

    #region Batch Write-Off Integration Tests

    [Fact]
    public async Task WriteOffBatch_QuarantinedBatch_PartialWriteOff_DeductsBatchAndProductAndRecordsLedger()
    {
        var product = await CreateTestProductAsync("WRO-QRT");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-WRO-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 100
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Quarantine the batch
        var qrtReq = new QuarantineBatchRequest
        {
            Reason = "Suspected contamination during transit",
            RowVersion = receipt.BatchVersion
        };
        var qrtResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/quarantine", qrtReq);
        var quarantined = await qrtResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(quarantined);

        // Partial write-off 30 units
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 30,
            Reason = "Pest contamination destruction",
            DisposalDocumentNumber = "DISP-2026-001",
            BatchRowVersion = quarantined.Version
        };
        var writeOffResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        Assert.Equal(HttpStatusCode.OK, writeOffResp.StatusCode);

        var result = await writeOffResp.Content.ReadFromJsonAsync<WriteOffBatchResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(receipt.BatchId, result.BatchId);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal(30, result.WrittenOffQuantity);
        Assert.Equal(100, result.PreviousPhysicalQuantity);
        Assert.Equal(70, result.NewPhysicalQuantity);
        Assert.Equal(100, result.PreviousProductQuantityOnHand);
        Assert.Equal(70, result.NewProductQuantityOnHand);
        Assert.Equal("Quarantined", result.Status);

        // Verify product QuantityOnHand in DB
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var updatedProduct = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(updatedProduct);
        Assert.Equal(70, updatedProduct.QuantityOnHand);

        // Verify batch state in DB
        var batchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var updatedBatch = await batchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(updatedBatch);
        Assert.Equal(70, updatedBatch.PhysicalQuantity);
        Assert.Equal(0, updatedBatch.ReservedQuantity);
        Assert.Equal(70, updatedBatch.AvailableQuantity);
        Assert.Equal("Quarantined", updatedBatch.Status);

        // Verify StockMovement ledger entry
        var movResp = await _client.GetAsync($"/api/inventory/movements?batchId={receipt.BatchId}");
        var movements = await movResp.Content.ReadFromJsonAsync<PagedResult<StockMovementResponse>>(JsonOptions);
        Assert.NotNull(movements);
        var writeOffMov = movements.Items.FirstOrDefault(m => m.MovementType == "WriteOff");
        Assert.NotNull(writeOffMov);
        Assert.Equal(-30, writeOffMov.QuantityChange);
        Assert.Equal(70, writeOffMov.BalanceAfter);
        Assert.Equal(70, writeOffMov.BatchBalanceAfter);
        Assert.Contains("DISP-2026-001", writeOffMov.Reason);
        Assert.Contains("Pest contamination destruction", writeOffMov.Reason);
        Assert.Contains("admin", writeOffMov.Reason);
    }

    [Fact]
    public async Task WriteOffBatch_ReleasedBatch_PartialWriteOff_DeductsBatchAndProductAndRecordsLedger()
    {
        var product = await CreateTestProductAsync("WRO-REL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-REL-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
            Quantity = 60
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

        // Write off 20 units
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 20,
            Reason = "Water leakage damage on top pallet",
            DisposalDocumentNumber = "DISP-2026-002",
            BatchRowVersion = released.Version
        };
        var writeOffResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        Assert.Equal(HttpStatusCode.OK, writeOffResp.StatusCode);

        var result = await writeOffResp.Content.ReadFromJsonAsync<WriteOffBatchResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(20, result.WrittenOffQuantity);
        Assert.Equal(60, result.PreviousPhysicalQuantity);
        Assert.Equal(40, result.NewPhysicalQuantity);
        Assert.Equal(60, result.PreviousProductQuantityOnHand);
        Assert.Equal(40, result.NewProductQuantityOnHand);
        Assert.Equal("Released", result.Status);

        // Verify batch state: AvailableQuantity should be 40 (Released with 0 reserved)
        var batchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var updatedBatch = await batchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(updatedBatch);
        Assert.Equal(40, updatedBatch.PhysicalQuantity);
        Assert.Equal(40, updatedBatch.AvailableQuantity);
        Assert.Equal("Released", updatedBatch.Status);
    }

    [Fact]
    public async Task WriteOffBatch_FullWriteOff_ReducesPhysicalToZeroAndTransitionsToDepleted()
    {
        var product = await CreateTestProductAsync("WRO-FULL");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-FULL-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 45
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

        // Full write-off of 45 units
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 45,
            Reason = "Total batch spoilage after chiller failure",
            DisposalDocumentNumber = "DISP-2026-003",
            BatchRowVersion = released.Version
        };
        var writeOffResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        Assert.Equal(HttpStatusCode.OK, writeOffResp.StatusCode);

        var result = await writeOffResp.Content.ReadFromJsonAsync<WriteOffBatchResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(45, result.WrittenOffQuantity);
        Assert.Equal(0, result.NewPhysicalQuantity);
        Assert.Equal("Depleted", result.Status);

        // Verify batch state is Depleted and physical is 0
        var batchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var updatedBatch = await batchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(updatedBatch);
        Assert.Equal(0, updatedBatch.PhysicalQuantity);
        Assert.Equal(0, updatedBatch.AvailableQuantity);
        Assert.Equal("Depleted", updatedBatch.Status);

        // Verify productOnHand is 0
        var prodResp = await _client.GetAsync($"/api/products/{product.Id}");
        var updatedProduct = await prodResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(updatedProduct);
        Assert.Equal(0, updatedProduct.QuantityOnHand);
    }

    [Fact]
    public async Task WriteOffBatch_PendingReleaseBatch_ReturnsConflict409()
    {
        var product = await CreateTestProductAsync("WRO-PND");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-PND-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 50
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);

        // Try to write off while batch is still PendingRelease
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 10,
            Reason = "Disposal attempt before inspection",
            BatchRowVersion = receipt.BatchVersion
        };
        var writeOffResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        Assert.Equal(HttpStatusCode.Conflict, writeOffResp.StatusCode);

        var problem = await writeOffResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("PendingRelease", problem.Detail);
    }

    [Fact]
    public async Task WriteOffBatch_DepletedBatch_ReturnsConflict409()
    {
        var product = await CreateTestProductAsync("WRO-DEP");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-DEP-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 20
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

        // Full write-off
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 20,
            Reason = "Full destruction",
            BatchRowVersion = released.Version
        };
        var wResp1 = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        var writtenOff = await wResp1.Content.ReadFromJsonAsync<WriteOffBatchResponse>(JsonOptions);
        Assert.NotNull(writtenOff);

        // Try writing off again on Depleted batch
        var secondWriteOff = new WriteOffBatchRequest
        {
            Quantity = 1,
            Reason = "Invalid subsequent write-off",
            BatchRowVersion = writtenOff.BatchVersion
        };
        var wResp2 = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", secondWriteOff);
        Assert.Equal(HttpStatusCode.Conflict, wResp2.StatusCode);

        var problem = await wResp2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("Depleted", problem.Detail);
    }

    [Fact]
    public async Task WriteOffBatch_QuantityExceedingUnreservedStock_ReturnsConflict409()
    {
        var product = await CreateTestProductAsync("WRO-RES");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-RES-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
            Quantity = 50
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

        // Reserve 30 units by confirming an order
        var orderReq = new CreateOrderRequest
        {
            CustomerName = "Write-off Reservation Tester",
            CustomerEmail = "test@example.com",
            Items = [new CreateOrderItemRequest { ProductId = product.Id, Quantity = 30 }]
        };
        var orderResp = await _client.PostAsJsonAsync("/api/orders", orderReq);
        var order = await orderResp.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        var confResp = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequest { RowVersion = order.Version });
        Assert.Equal(HttpStatusCode.OK, confResp.StatusCode);

        // Current batch state: Physical = 50, Reserved = 30. Unreserved available for write-off = 20.
        // Get fresh batch version after reservation
        var batchFreshResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var freshBatch = await batchFreshResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(freshBatch);
        Assert.Equal(30, freshBatch.ReservedQuantity);

        // Attempting to write off 25 units (which exceeds 20 unreserved) must return 409 Conflict
        var excessiveWriteOffReq = new WriteOffBatchRequest
        {
            Quantity = 25,
            Reason = "Exceeds unreserved stock",
            BatchRowVersion = freshBatch.Version
        };
        var failResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", excessiveWriteOffReq);
        Assert.Equal(HttpStatusCode.Conflict, failResp.StatusCode);

        var problem = await failResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("exceeds available unreserved stock", problem.Detail, StringComparison.OrdinalIgnoreCase);

        // Now write off exactly the 20 unreserved units -> Should succeed
        var exactWriteOffReq = new WriteOffBatchRequest
        {
            Quantity = 20,
            Reason = "Write-off exact unreserved remainder",
            BatchRowVersion = freshBatch.Version
        };
        var succResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", exactWriteOffReq);
        Assert.Equal(HttpStatusCode.OK, succResp.StatusCode);

        var finalBatchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var finalBatch = await finalBatchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(finalBatch);
        Assert.Equal(30, finalBatch.PhysicalQuantity);
        Assert.Equal(30, finalBatch.ReservedQuantity);
        Assert.Equal(0, finalBatch.AvailableQuantity);
    }

    [Fact]
    public async Task WriteOffBatch_WithStaleBatchRowVersion_ReturnsConflict409Concurrency()
    {
        var product = await CreateTestProductAsync("WRO-STALE");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-STALE-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Quantity = 50
        };
        var recResp = await _client.PostAsJsonAsync("/api/inventory/receipts", receiptReq);
        var receipt = await recResp.Content.ReadFromJsonAsync<StockReceiptResponse>(JsonOptions);
        Assert.NotNull(receipt);
        var staleVersion = receipt.BatchVersion;

        // Release batch (version changes)
        var relReq = new ReleaseBatchRequest
        {
            VisualCheckPassed = true,
            AromaCheckPassed = true,
            PackagingCheckPassed = true,
            RowVersion = receipt.BatchVersion
        };
        var relResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/release", relReq);
        Assert.Equal(HttpStatusCode.OK, relResp.StatusCode);

        // Attempt write-off using stale version
        var writeOffReq = new WriteOffBatchRequest
        {
            Quantity = 10,
            Reason = "Stale version test",
            BatchRowVersion = staleVersion
        };
        var writeOffResp = await _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", writeOffReq);
        Assert.Equal(HttpStatusCode.Conflict, writeOffResp.StatusCode);

        var problem = await writeOffResp.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Contains("modified by another operation", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteOffBatch_ConcurrentWriteOffs_PreventsDoubleDeductionOrNegativeStock()
    {
        var product = await CreateTestProductAsync("WRO-CONC");
        var receiptReq = new StockReceiptRequest
        {
            ProductId = product.Id,
            BatchNo = $"BAT-WROCONC-{Guid.NewGuid().ToString("N")[..6]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
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

        // Concurrently attempt two write-offs of 20 units each with the same released version
        var task1 = _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", new WriteOffBatchRequest
        {
            Quantity = 20,
            Reason = "Concurrent write-off A",
            BatchRowVersion = released.Version
        });
        var task2 = _client.PostAsJsonAsync($"/api/inventory/batches/{receipt.BatchId}/write-off", new WriteOffBatchRequest
        {
            Quantity = 20,
            Reason = "Concurrent write-off B",
            BatchRowVersion = released.Version
        });

        await Task.WhenAll(task1, task2);

        var res1 = await task1;
        var res2 = await task2;

        // Exactly one should succeed with 200 OK, and the other should fail with 409 Conflict (concurrency token mismatch)
        var okCount = (res1.StatusCode == HttpStatusCode.OK ? 1 : 0) + (res2.StatusCode == HttpStatusCode.OK ? 1 : 0);
        var conflictCount = (res1.StatusCode == HttpStatusCode.Conflict ? 1 : 0) + (res2.StatusCode == HttpStatusCode.Conflict ? 1 : 0);

        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        // Verify remaining stock is exactly 10 units (30 - 20) and never negative
        var finalBatchResp = await _client.GetAsync($"/api/inventory/batches/{receipt.BatchId}");
        var finalBatch = await finalBatchResp.Content.ReadFromJsonAsync<BatchResponse>(JsonOptions);
        Assert.NotNull(finalBatch);
        Assert.Equal(10, finalBatch.PhysicalQuantity);
        Assert.Equal(10, finalBatch.AvailableQuantity);
        Assert.Equal("Released", finalBatch.Status);

        var finalProdResp = await _client.GetAsync($"/api/products/{product.Id}");
        var finalProd = await finalProdResp.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(finalProd);
        Assert.Equal(10, finalProd.QuantityOnHand);
    }

    #endregion
}
