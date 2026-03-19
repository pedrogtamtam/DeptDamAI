using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace DeptDam.Services;

public class ApiKeyService
{
    public (string ClientId, string ClientSecret) GenerateCredentials()
    {
        var clientId = "client_" + Guid.NewGuid().ToString("N");
        
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        var clientSecret = Convert.ToBase64String(secretBytes);

        return (clientId, clientSecret);
    }

    public string HashSecret(string secret)
    {
        // Simple fast hash for high-entropy machine generated secrets
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hash);
    }

    public bool VerifySecret(string secret, string hash)
    {
        return HashSecret(secret) == hash;
    }
}
