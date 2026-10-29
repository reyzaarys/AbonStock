using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AbonStock.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace AbonStock.Tests.Integration;

public class PostgresTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestConnectionString =
        "Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=abonstock_dev_pass_2026";

    public const string TestJwtSecretKey =
        "AbonStock_Integration_Test_Signing_Key_At_Least_32_Bytes!";

    static PostgresTestFixture()
    {
        ValidateTestDatabase(TestConnectionString);
    }

    public static void ValidateTestDatabase(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.Equals(builder.Database, "abonstock_db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CRITICAL SAFETY VIOLATION: Integration tests must NOT target the development/production database 'abonstock_db'. Aborting test execution.");
        }

        if (!string.Equals(builder.Database, "abonstock_test_db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SAFETY GUARD: Integration tests must exclusively target 'abonstock_test_db', but connection string specifies '{builder.Database}'. Aborting test execution.");
        }
    }

    public static string GenerateTestAdminToken()
    {
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(TestJwtSecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.NameIdentifier, "usr_admin")
        };

        var token = new JwtSecurityToken(
            issuer: "AbonStock.Api",
            audience: "AbonStock.Frontend",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateTestAdminToken());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ValidateTestDatabase(TestConnectionString);

        builder.UseSetting("Jwt:SecretKey", TestJwtSecretKey);
        builder.UseSetting("Jwt:Issuer", "AbonStock.Api");
        builder.UseSetting("Jwt:Audience", "AbonStock.Frontend");

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
