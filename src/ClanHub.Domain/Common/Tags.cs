namespace ClanHub.Domain;

public static class Tags
{
    /// <summary>Normaliza uma tag: maiúsculas, com '#', letra O trocada pelo número 0.</summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var t = tag.Trim().ToUpperInvariant().Replace('O', '0');
        return t.StartsWith('#') ? t : "#" + t;
    }

    public static bool IsValid(string? tag)
    {
        var t = Normalize(tag);
        if (t.Length < 4 || t.Length > 14) return false;
        foreach (var c in t.AsSpan(1))
            if ("0289PYLQGRJCUV".IndexOf(c) < 0) return false;
        return true;
    }

    /// <summary>Codifica a tag para uso em URL (# vira %23).</summary>
    public static string ToUrl(string tag) => Uri.EscapeDataString(Normalize(tag));
}

public interface ISoftDeletable { bool IsDeleted { get; set; } }