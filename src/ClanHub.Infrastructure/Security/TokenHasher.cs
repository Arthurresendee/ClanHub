using System.Security.Cryptography;
using System.Text;

namespace ClanHub.Infrastructure.Security;

public static class TokenHasher
{
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}