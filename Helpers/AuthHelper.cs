using System.Security.Cryptography;
using System.Text;

namespace ShantiBotDi.Helpers;

public static class AuthHelper
{
    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}