using System.Globalization;
using System.Text;

namespace GameHub.Core.Text;

/// <summary>Normaliza títulos para comparação (detecção de duplicados e busca).</summary>
public static class TitleNormalizer
{
    private static readonly Dictionary<string, string> Roman = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6",
        ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "the", "edition", "goty", "definitive", "remastered", "deluxe", "complete", "ultimate",
        "game", "of", "year", "enhanced", "directors", "cut",
        "edicao", "definitiva", "completa", "remasterizado", "remasterizada",
    };

    /// <summary>Forma canônica: minúsculas, sem acentos/pontuação, números romanos → arábicos, sem palavras de edição.</summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        var words = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Roman.TryGetValue(w, out var n) ? n : w)
            .Where(w => !NoiseWords.Contains(w))
            .ToList();
        return string.Join(' ', words);
    }

    /// <summary>Similaridade 0..1 entre dois títulos (Dice sobre bigramas do título normalizado).</summary>
    public static double Similarity(string? a, string? b)
    {
        var na = Normalize(a).Replace(" ", "");
        var nb = Normalize(b).Replace(" ", "");
        if (na.Length == 0 || nb.Length == 0) return 0;
        if (na == nb) return 1;
        if (na.Length < 2 || nb.Length < 2) return 0;
        var bigramsA = new Dictionary<string, int>();
        for (var i = 0; i < na.Length - 1; i++)
        {
            var bg = na.Substring(i, 2);
            bigramsA[bg] = bigramsA.GetValueOrDefault(bg) + 1;
        }
        var matches = 0;
        for (var i = 0; i < nb.Length - 1; i++)
        {
            var bg = nb.Substring(i, 2);
            if (bigramsA.TryGetValue(bg, out var count) && count > 0)
            {
                matches++;
                bigramsA[bg] = count - 1;
            }
        }
        return 2.0 * matches / (na.Length - 1 + nb.Length - 1);
    }
}
