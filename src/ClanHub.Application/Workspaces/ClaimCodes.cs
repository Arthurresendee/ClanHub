using System.Security.Cryptography;

namespace ClanHub.Application.Workspaces;

public static class ClaimCodes
{
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(30);
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Generate()
    {
        Span<char> chars = stackalloc char[5];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return "CH-" + new string(chars);
    }

    public static bool DescriptionContains(string? description, string code) =>
        !string.IsNullOrEmpty(description) && description.Contains(code, StringComparison.OrdinalIgnoreCase);
}