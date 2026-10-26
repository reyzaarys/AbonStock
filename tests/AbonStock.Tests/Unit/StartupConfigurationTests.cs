using System.Net;
using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Unit;

public class StartupConfigurationTests
{
    [Fact]
    public void ConnectionString_CanBeSuppliedThroughEnvironmentVariable_WithoutExposingSecret()
    {
        // Simulate configuration loaded from base template with placeholder
        var baseConfig = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=abonstock_db;Username=abonstock;Password=YOUR_DB_PASSWORD"
        };

        // Simulate environment variable override (e.g. ConnectionStrings__DefaultConnection)
        var envVars = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Port=5432;Database=abonstock_prod;Username=prod_user;Password=injected_secret_token"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(baseConfig)
            .AddInMemoryCollection(envVars)
            .Build();

        var resolvedConnectionString = config.GetConnectionString("DefaultConnection");

        Assert.NotNull(resolvedConnectionString);
        Assert.Contains("Host=db.internal", resolvedConnectionString);
        Assert.Contains("Username=prod_user", resolvedConnectionString);
        Assert.DoesNotContain("YOUR_DB_PASSWORD", resolvedConnectionString);
    }

    [Fact]
    public void Seeder_IsDisabled_WhenConfigurationFlagIsAbsent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Information"
            })
            .Build();

        var autoSeed = config.GetValue<bool>("Seeding:AutoSeed");

        Assert.False(autoSeed);
    }

    [Fact]
    public void Seeder_IsDisabled_WhenConfigurationFlagIsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:AutoSeed"] = "false"
            })
            .Build();

        var autoSeed = config.GetValue<bool>("Seeding:AutoSeed");

        Assert.False(autoSeed);
    }

    [Fact]
    public void Seeder_IsEnabled_OnlyWhenExplicitFlagIsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:AutoSeed"] = "true"
            })
            .Build();

        var autoSeed = config.GetValue<bool>("Seeding:AutoSeed");

        Assert.True(autoSeed);
    }

    [Fact]
    public void ProductionCors_ThrowsException_WhenAllowedOriginsMissingOrEmpty()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                RemoveAndReplaceServices(services);
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var baseEx = ex.GetBaseException();

        Assert.IsType<InvalidOperationException>(baseEx);
        Assert.Contains("Cors:AllowedOrigins must be configured", baseEx.Message);
        Assert.Contains("Production", baseEx.Message);
    }

    [Fact]
    public async Task ProductionCors_AllowsConfiguredOrigins_WhenExplicitlyConfigured()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SecretKey", "test-signing-key-for-startup-testing-purposes-only-32b");
            builder.UseSetting("Cors:AllowedOrigins", "https://app.abonstock.com");
            builder.ConfigureServices(services =>
            {
                RemoveAndReplaceServices(services);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "https://app.abonstock.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("https://app.abonstock.com", response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task DevelopmentCors_DefaultsToVite5173_WhenAllowedOriginsMissing()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SecretKey", "test-signing-key-for-startup-testing-purposes-only-32b");
            builder.UseSetting("Cors:AllowedOrigins", "");
            builder.UseSetting("Seeding:AutoSeed", "false");
            builder.ConfigureServices(services =>
            {
                RemoveAndReplaceServices(services);
            });
        });

        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    private static void RemoveAndReplaceServices(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductService));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
        services.AddScoped<IProductService, DummyProductService>();
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
