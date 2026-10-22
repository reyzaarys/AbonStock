using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Products;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AbonStock.Tests.Integration;

public class ProductIntegrationTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task CreateProduct_WithValidData_ReturnsCreatedAndNormalizesSku()
    {
        var rawSku = "  test-sku-" + Guid.NewGuid().ToString("N")[..8] + "  ";
        var request = new CreateProductRequest
        {
            Sku = rawSku,
            Name = "Integration Test Product",
            Description = "Product created in integration test",
            UnitPrice = 49.99m
        };

        var response = await _client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        Assert.Equal(rawSku.Trim().ToUpperInvariant(), created.Sku);
        Assert.Equal(49.99m, created.UnitPrice);
        Assert.Equal(0, created.QuantityOnHand);
        Assert.True(created.IsActive);
        Assert.True(created.Version > 0);

        // Verify GET by ID
        var getResponse = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task CreateProduct_WithNegativePrice_ReturnsBadRequestProblemDetails()
    {
        var request = new CreateProductRequest
        {
            Sku = "NEG-PRICE-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Negative Price Product",
            UnitPrice = -15.00m
        };

        var response = await _client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
    }

    [Fact]
    public async Task CreateProduct_WithDuplicateSku_ReturnsConflictProblemDetails()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var request1 = new CreateProductRequest
        {
            Sku = "dup-sku-" + uniqueSuffix,
            Name = "Product First",
            UnitPrice = 20.00m
        };
        var firstResponse = await _client.PostAsJsonAsync("/api/products", request1);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Attempt duplicate insert with different casing/whitespace
        var request2 = new CreateProductRequest
        {
            Sku = "  DUP-SKU-" + uniqueSuffix + "  ",
            Name = "Product Second",
            UnitPrice = 30.00m
        };
        var duplicateResponse = await _client.PostAsJsonAsync("/api/products", request2);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.Conflict, problem.Status);
        Assert.Contains("already exists", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateProduct_WithValidData_UpdatesAndReturnsNewVersionToken()
    {
        var createRequest = new CreateProductRequest
        {
            Sku = "UPDATE-SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Original Name",
            UnitPrice = 10.00m
        };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(created);

        var updateRequest = new UpdateProductRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            UnitPrice = 15.50m,
            RowVersion = created.Version
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(15.50m, updated.UnitPrice);
        Assert.NotEqual(created.Version, updated.Version); // xmin changed on update
    }

    [Fact]
    public async Task UpdateProduct_WithStaleRowVersion_ReturnsConflictConcurrencyProblemDetails()
    {
        var createRequest = new CreateProductRequest
        {
            Sku = "CONCUR-SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Concurrent Test",
            UnitPrice = 10.00m
        };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(created);

        var initialVersion = created.Version;

        // Perform valid update 1
        var update1 = new UpdateProductRequest
        {
            Name = "Valid Update 1",
            UnitPrice = 20.00m,
            RowVersion = initialVersion
        };
        var response1 = await _client.PutAsJsonAsync($"/api/products/{created.Id}", update1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Attempt update 2 with the stale initialVersion
        var staleUpdate = new UpdateProductRequest
        {
            Name = "Stale Update 2",
            UnitPrice = 30.00m,
            RowVersion = initialVersion // Stale version
        };
        var staleResponse = await _client.PutAsJsonAsync($"/api/products/{created.Id}", staleUpdate);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var problem = await staleResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal("Concurrency Conflict", problem.Title);
    }

    [Fact]
    public async Task DeactivateProduct_SetsIsActiveFalse()
    {
        var createRequest = new CreateProductRequest
        {
            Sku = "DEACT-SKU-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Product To Deactivate",
            UnitPrice = 10.00m
        };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(created);

        var patchResponse = await _client.PatchAsync($"/api/products/{created.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var deactivated = await patchResponse.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions);
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // Verify that default list query excludes it
        var listResponse = await _client.GetAsync($"/api/products?search={created.Sku}&includeInactive=false");
        var listResult = await listResponse.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>(JsonOptions);
        Assert.NotNull(listResult);
        Assert.DoesNotContain(listResult.Items, p => p.Id == created.Id);

        // Verify that list with includeInactive=true includes it
        var listAllResponse = await _client.GetAsync($"/api/products?search={created.Sku}&includeInactive=true");
        var listAllResult = await listAllResponse.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>(JsonOptions);
        Assert.NotNull(listAllResult);
        Assert.Contains(listAllResult.Items, p => p.Id == created.Id);
    }

    [Fact]
    public async Task GetProduct_WithNonExistentId_ReturnsNotFoundProblemDetails()
    {
        var response = await _client.GetAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.NotFound, problem.Status);
        Assert.Equal("Not Found", problem.Title);
    }

    [Fact]
    public async Task UpdateProduct_WithNonExistentId_ReturnsNotFoundProblemDetails()
    {
        var updateRequest = new UpdateProductRequest
        {
            Name = "Non-existent Product",
            UnitPrice = 25.00m,
            RowVersion = 1u
        };

        var response = await _client.PutAsJsonAsync("/api/products/999999", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.NotFound, problem.Status);
        Assert.Equal("Not Found", problem.Title);
    }
}
