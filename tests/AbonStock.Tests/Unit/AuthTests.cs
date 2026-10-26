using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Auth;
using AbonStock.Api.Dtos.Products;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AbonStock.Tests.Unit;

public class AuthTests : IClassFixture<AuthTests.AuthTestFactory>
{
    private const string ValidSecretKey = "test-signing-key-for-auth-testing-purposes-only-32b";
    private const string ValidIssuer = "AbonStock.Api";
    private const string ValidAudience = "AbonStock.Frontend";
    private const string AdminPassword = "AdminPassword123!";
    private const string StaffPassword = "StaffPassword123!";

    private readonly AuthTestFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(AuthTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccessAndValidJwtToken()
    {
        var request = new LoginRequest
        {
            Username = "admin",
            Password = AdminPassword
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.Token));
        Assert.True(loginResult.ExpiresIn > 0);
        Assert.Equal("admin", loginResult.User.Username);
        Assert.Equal("Admin", loginResult.User.Role);
        Assert.Equal("usr_admin", loginResult.User.Id);

        // Validate token cryptographic signature and claims
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ValidateIssuer = true,
            ValidIssuer = ValidIssuer,
            ValidateAudience = true,
            ValidAudience = ValidAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(loginResult.Token, validationParams, out var validatedToken);
        Assert.NotNull(principal);
        Assert.Equal("admin", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("Admin", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("usr_admin", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsUnauthorizedGenericError()
    {
        var request = new LoginRequest
        {
            Username = "nonexistent_user",
            Password = "SomePassword123!"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.Equal("Invalid username or password.", problem.Detail);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedGenericError()
    {
        var request = new LoginRequest
        {
            Username = "admin",
            Password = "WrongPassword999!"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        // Generic error message must be identical to unknown user to prevent user enumeration
        Assert.Equal("Invalid username or password.", problem.Detail);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_ReturnsUnauthorized()
    {
        var expiredToken = CreateTestToken(
            ValidSecretKey,
            ValidIssuer,
            ValidAudience,
            "usr_admin",
            "admin",
            "Admin",
            expires: DateTime.UtcNow.AddMinutes(-10));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidSignature_ReturnsUnauthorized()
    {
        const string forgedKey = "tampered-secret-key-that-does-not-match-at-all-32b!";
        var forgedToken = CreateTestToken(
            forgedKey,
            ValidIssuer,
            ValidAudience,
            "usr_admin",
            "admin",
            "Admin");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidIssuerOrAudience_ReturnsUnauthorized()
    {
        var wrongIssuerToken = CreateTestToken(
            ValidSecretKey,
            "Untrusted.Rogue.Issuer",
            ValidAudience,
            "usr_admin",
            "admin",
            "Admin");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuerToken);

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_IsAccessibleWithoutToken()
    {
        using var client = _factory.CreateClient();

        // Send an unauthenticated request to login endpoint
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "admin",
            Password = AdminPassword
        });

        // Must succeed without 401 challenge, verifying [AllowAnonymous]
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithValidToken_ReturnsCurrentUserProfile()
    {
        // 1. Authenticate to get valid token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "staff",
            Password = StaffPassword
        });
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);

        // 2. Query /api/auth/me
        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        var profileResponse = await authedClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal("staff", profile.Username);
        Assert.Equal("Staff", profile.Role);
        Assert.Equal("Warehouse Staff", profile.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("YOUR_JWT_SIGNING_KEY_MINIMUM_32_CHARACTERS_PLACEHOLDER")]
    [InlineData("short-key-under-32-bytes!")]
    public void SigningKey_WhenMissingOrPlaceholderOrTooShort_ThrowsExceptionOnStartup(string invalidKey)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:SecretKey", invalidKey);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductService));
                if (descriptor != null) services.Remove(descriptor);
                services.AddScoped<IProductService, DummyProductService>();
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var baseEx = ex.GetBaseException();

        Assert.IsType<InvalidOperationException>(baseEx);
        Assert.Contains("Jwt:SecretKey must be configured", baseEx.Message);
    }

    private static string CreateTestToken(
        string secretKey,
        string issuer,
        string audience,
        string userId,
        string username,
        string role,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var tokenExpiry = expires ?? now.AddHours(1);
        var notBefore = tokenExpiry < now ? tokenExpiry.AddMinutes(-10) : now.AddMinutes(-1);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            NotBefore = notBefore,
            Expires = tokenExpiry,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public class AuthTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:SecretKey", ValidSecretKey);
            builder.UseSetting("Jwt:Issuer", ValidIssuer);
            builder.UseSetting("Jwt:Audience", ValidAudience);

            // Configure test passwords dynamically without checking secrets into config files
            builder.UseSetting("Auth:Users:0:Password", AdminPassword);
            builder.UseSetting("Auth:Users:1:Password", StaffPassword);

            builder.ConfigureServices(services =>
            {
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
