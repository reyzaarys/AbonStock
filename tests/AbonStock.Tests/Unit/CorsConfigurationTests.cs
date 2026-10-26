using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AbonStock.Tests.Unit;

public class CorsConfigurationTests : IClassFixture<CorsConfigurationTests.CorsTestFactory>
{
    private const string CorsTestSecret = "test-signing-key-for-cors-testing-purposes-only-32b";
    private readonly HttpClient _client;

    public CorsConfigurationTests(CorsTestFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateTestToken());
    }

    private static string CreateTestToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "cors-test-user"),
                new Claim(ClaimTypes.Name, "corstester"),
                new Claim(ClaimTypes.Role, "Staff"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "AbonStock.Api",
            Audience = "AbonStock.Frontend",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CorsTestSecret)),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task PreflightOptions_WithAllowedOrigin5173_ReturnsCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task PreflightOptions_WithAllowedOrigin3000_ReturnsCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:3000", response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task PreflightOptions_WithDisallowedOrigin_DoesNotReturnCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "http://unauthorized-domain.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SimpleGet_WithAllowedOrigin_ReturnsCorsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task SimpleGet_WithDisallowedOrigin_DoesNotReturnCorsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add("Origin", "http://unauthorized-domain.com");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PreflightOptions_WithCommaSeparatedOrigins_AllowsAllListedOrigins()
    {
        using var customFactory = new CorsTestFactory().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cors:AllowedOrigins", "https://alpha.abonstock.com, https://beta.abonstock.com");
        });
        var client = customFactory.CreateClient();

        // Verify alpha origin is allowed
        using var reqAlpha = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqAlpha.Headers.Add("Origin", "https://alpha.abonstock.com");
        reqAlpha.Headers.Add("Access-Control-Request-Method", "GET");
        var respAlpha = await client.SendAsync(reqAlpha);
        Assert.Equal(HttpStatusCode.NoContent, respAlpha.StatusCode);
        Assert.True(respAlpha.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("https://alpha.abonstock.com", respAlpha.Headers.GetValues("Access-Control-Allow-Origin").First());

        // Verify beta origin is allowed
        using var reqBeta = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqBeta.Headers.Add("Origin", "https://beta.abonstock.com");
        reqBeta.Headers.Add("Access-Control-Request-Method", "GET");
        var respBeta = await client.SendAsync(reqBeta);
        Assert.Equal(HttpStatusCode.NoContent, respBeta.StatusCode);
        Assert.True(respBeta.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("https://beta.abonstock.com", respBeta.Headers.GetValues("Access-Control-Allow-Origin").First());

        // Verify default origin (localhost:5173) is NOT allowed when comma-separated override is provided
        using var reqDisallowed = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqDisallowed.Headers.Add("Origin", "http://localhost:5173");
        reqDisallowed.Headers.Add("Access-Control-Request-Method", "GET");
        var respDisallowed = await client.SendAsync(reqDisallowed);
        Assert.False(respDisallowed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PreflightOptions_WithIndexedOrigins_AllowsAllIndexedOrigins()
    {
        using var customFactory = new CorsTestFactory().WithWebHostBuilder(b =>
        {
            b.UseSetting("Cors:AllowedOrigins:0", "https://indexed-a.abonstock.com");
            b.UseSetting("Cors:AllowedOrigins:1", "https://indexed-b.abonstock.com");
        });
        var client = customFactory.CreateClient();

        // Verify indexed-a origin is allowed
        using var reqA = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqA.Headers.Add("Origin", "https://indexed-a.abonstock.com");
        reqA.Headers.Add("Access-Control-Request-Method", "GET");
        var respA = await client.SendAsync(reqA);
        Assert.Equal(HttpStatusCode.NoContent, respA.StatusCode);
        Assert.True(respA.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("https://indexed-a.abonstock.com", respA.Headers.GetValues("Access-Control-Allow-Origin").First());

        // Verify indexed-b origin is allowed
        using var reqB = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqB.Headers.Add("Origin", "https://indexed-b.abonstock.com");
        reqB.Headers.Add("Access-Control-Request-Method", "GET");
        var respB = await client.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.NoContent, respB.StatusCode);
        Assert.True(respB.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("https://indexed-b.abonstock.com", respB.Headers.GetValues("Access-Control-Allow-Origin").First());

        // Verify localhost:5173 is NOT allowed when overridden by indexed settings
        using var reqDisallowed = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        reqDisallowed.Headers.Add("Origin", "http://localhost:5173");
        reqDisallowed.Headers.Add("Access-Control-Request-Method", "GET");
        var respDisallowed = await client.SendAsync(reqDisallowed);
        Assert.False(respDisallowed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    public class CorsTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:SecretKey", CorsTestSecret);
            builder.UseSetting("Jwt:Issuer", "AbonStock.Api");
            builder.UseSetting("Jwt:Audience", "AbonStock.Frontend");
            builder.ConfigureServices(services =>
            {
                // Disable authorization fallback policy specifically for CORS header verification
                services.AddAuthorization(options =>
                {
                    options.FallbackPolicy = null;
                });

                // Substitute IProductService with DummyProductService so zero database connection is attempted
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddScoped<IProductService, DummyProductService>();
            });
        }
    }

    private class DummyProductService : IProductService
    {
        public Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ProductResponse> DeactivateAsync(int id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ProductResponse> GetByIdAsync(int id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ProductResponse>(Array.Empty<ProductResponse>(), 1, 20, 0));
        public Task<ProductResponse> UpdateAsync(int id, UpdateProductRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
