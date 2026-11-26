using System.Globalization;
using System.Text.RegularExpressions;

namespace GameHub.Core.Text;

public static class DateParsing
{
    private static readonly CultureInfo[] Cultures =
    {
        CultureInfo.GetCultureInfo("pt-BR"), CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("en-GB"), CultureInfo.InvariantCulture,
    };

    /// <summary>
    /// Converte datas como "10 dez. 2020", "Dec 10, 2020", "2020-12-10" para "yyyy-MM-dd".
    /// Se só o ano for identificável, retorna "yyyy". Se nada servir, null.
    /// </summary>
    public static string? ToIso(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim().Replace(" de ", " ");
        // A Steam usa "9 dez. 2020" (pt-BR) ou "Dec 9, 2020" (en); tenta com e sem o ponto da abreviação.
        foreach (var t in new[] { trimmed, trimmed.Replace(".", ""), Regex.Replace(trimmed, @"(\p{L}{3})\.?\s", "$1. ") })
            foreach (var c in Cultures)
            {
                if (DateTime.TryParse(t, c, DateTimeStyles.AllowWhiteSpaces, out var d) && d.Year > 1950)
                    return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (DateTime.TryParseExact(t, new[] { "d MMM yyyy", "d MMM. yyyy", "dd MMM yyyy", "MMM d, yyyy", "d MMMM yyyy" }, c, DateTimeStyles.AllowWhiteSpaces, out d) && d.Year > 1950)
                    return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        var m = Regex.Match(text, @"\b(19[5-9]\d|20\d\d)\b");
        return m.Success ? m.Value : null;
    }

    public static string FromUnixSeconds(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
