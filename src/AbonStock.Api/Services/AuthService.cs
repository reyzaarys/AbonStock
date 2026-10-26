using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AbonStock.Api.Dtos.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace AbonStock.Api.Services;

public class AuthService : IAuthService
{
    private readonly List<DemoUser> _users = [];
    private readonly PasswordHasher<DemoUser> _passwordHasher = new();
    private readonly IConfiguration _configuration;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly byte[] _keyBytes;
    private readonly int _expiryHours;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
        _issuer = configuration["Jwt:Issuer"] ?? "AbonStock.Api";
        _audience = configuration["Jwt:Audience"] ?? "AbonStock.Frontend";
        _expiryHours = configuration.GetValue("Jwt:ExpiryHours", 8);

        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey must be configured.");
        _keyBytes = Encoding.UTF8.GetBytes(secretKey);

        LoadUsers();
    }

    private void LoadUsers()
    {
        var usersSection = _configuration.GetSection("Auth:Users").GetChildren();
        int index = 0;
        foreach (var userSec in usersSection)
        {
            var id = userSec["Id"] ?? $"usr_{index}";
            var username = userSec["Username"] ?? string.Empty;
            var displayName = userSec["DisplayName"] ?? username;
            var role = userSec["Role"] ?? "Staff";

            var user = new DemoUser
            {
                Id = id,
                Username = username,
                DisplayName = displayName,
                Role = role
            };

            // Password is read dynamically from configuration or environment override
            var rawPassword = userSec["Password"]
                ?? _configuration[$"Auth:Users:{index}:Password"]
                ?? _configuration["Auth:DefaultPassword"];

            if (!string.IsNullOrWhiteSpace(rawPassword))
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, rawPassword);
            }

            _users.Add(user);
            index++;
        }
    }

    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Task.FromResult<LoginResponse?>(null);
        }

        var user = _users.FirstOrDefault(u => string.Equals(u.Username, request.Username, StringComparison.OrdinalIgnoreCase));
        if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Task.FromResult<LoginResponse?>(null);
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Task.FromResult<LoginResponse?>(null);
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("displayName", user.DisplayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            Expires = DateTime.UtcNow.AddHours(_expiryHours),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(_keyBytes),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        var response = new LoginResponse(
            tokenString,
            (int)TimeSpan.FromHours(_expiryHours).TotalSeconds,
            new UserProfileResponse(user.Id, user.Username, user.DisplayName, user.Role)
        );

        return Task.FromResult<LoginResponse?>(response);
    }

    public UserProfileResponse? GetCurrentUserProfile(ClaimsPrincipal principal)
    {
        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = principal.FindFirst(ClaimTypes.Name)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        var displayName = principal.FindFirst("displayName")?.Value;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return new UserProfileResponse(id, username, displayName ?? username, role ?? "Staff");
    }
}
