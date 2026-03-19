using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DeptDam.Data;
using DeptDam.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DeptDam.Controllers;

[ApiController]
[Route("connect/token")]
public class TokenController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ApiKeyService _apiKeyService;
    private readonly IConfiguration _configuration;

    public TokenController(ApplicationDbContext dbContext, ApiKeyService apiKeyService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _apiKeyService = apiKeyService;
        _configuration = configuration;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GenerateToken([FromForm] TokenRequest request)
    {
        if (request.grant_type != "client_credentials")
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        if (string.IsNullOrEmpty(request.client_id) || string.IsNullOrEmpty(request.client_secret))
        {
            return BadRequest(new { error = "invalid_client" });
        }

        // Find client
        var client = await _dbContext.ApiClients.FirstOrDefaultAsync(c => c.ClientId == request.client_id);
        
        if (client == null || !_apiKeyService.VerifySecret(request.client_secret, client.ClientSecretHash))
        {
            return Unauthorized(new { error = "invalid_client" });
        }

        var jwtSettings = _configuration.GetSection("JwtSettings");
        var issuer = jwtSettings["Issuer"] ?? "DeptDam";
        var audience = jwtSettings["Audience"] ?? "DeptDamApi";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? "A_VERY_SECURE_SECRET_KEY_NEEDS_TO_BE_LONG_ENOUGH_123456789"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, client.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("client_name", client.Name),
            new Claim("tenant_id", client.TenantId) // Include tenant ID to secure API calls
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds
        );

        return Ok(new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(token),
            token_type = "Bearer",
            expires_in = 7200
        });
    }
}

public class TokenRequest
{
    public string grant_type { get; set; } = string.Empty;
    public string client_id { get; set; } = string.Empty;
    public string client_secret { get; set; } = string.Empty;
}
