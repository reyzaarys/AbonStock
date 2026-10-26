using System.Security.Claims;
using System.Text;
using AbonStock.Api.Data;
using AbonStock.Api.Middleware;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AbonStock API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure CORS
const string CorsPolicyName = Program.DefaultCorsPolicy;
string[]? allowedOrigins = null;

var scalarOrigins = builder.Configuration["Cors:AllowedOrigins"];
if (!string.IsNullOrWhiteSpace(scalarOrigins))
{
    allowedOrigins = scalarOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
else
{
    var arrayOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (arrayOrigins != null && arrayOrigins.Length > 0)
    {
        allowedOrigins = arrayOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToArray();
    }
}

if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Cors:AllowedOrigins must be configured with at least one valid origin in Production environment.");
    }

    allowedOrigins = ["http://localhost:5173", "http://localhost:3000"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure JWT Authentication & Authorization
var jwtSection = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSection["SecretKey"];
var issuer = jwtSection["Issuer"] ?? "AbonStock.Api";
var audience = jwtSection["Audience"] ?? "AbonStock.Frontend";

if (string.IsNullOrWhiteSpace(secretKey) ||
    secretKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
    secretKey.Contains("YOUR_JWT", StringComparison.OrdinalIgnoreCase) ||
    Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey must be configured with a secure, non-placeholder signing key of at least 32 bytes (256 bits) for HS256.");
}

var keyBytes = Encoding.UTF8.GetBytes(secretKey);
var signingKey = new SymmetricSecurityKey(keyBytes);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
});

builder.Services.AddAuthorization(options =>
{
    // Fallback policy: require authenticated user on all endpoints unless [AllowAnonymous] is specified
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AbonStockDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AbonStock API v1");
    });
}

// Database Seeding: Explicit opt-in only. Disabled by default when absent or false.
var autoSeed = app.Configuration.GetValue<bool>("Seeding:AutoSeed");
if (autoSeed)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();
    await AbonStockDataSeeder.SeedAsync(dbContext);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Make the implicit Program class public so test projects can access it via WebApplicationFactory
public partial class Program
{
    public const string DefaultCorsPolicy = "AllowFrontend";
}
