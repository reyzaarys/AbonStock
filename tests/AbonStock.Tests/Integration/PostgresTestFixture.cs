using AbonStock.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AbonStock.Tests.Integration;

public class PostgresTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestConnectionString =
        "Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=abonstock_dev_pass_2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the application's DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AbonStockDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register DbContext pointing to isolated abonstock_test_db
            services.AddDbContext<AbonStockDbContext>(options =>
            {
                options.UseNpgsql(TestConnectionString);
            });
        });

        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        // Ensure database schema is migrated on the isolated test database
        await dbContext.Database.MigrateAsync();
    }

    public new Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
