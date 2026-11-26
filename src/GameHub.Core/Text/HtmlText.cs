using System.Net;
using System.Text.RegularExpressions;

namespace GameHub.Core.Text;

/// <summary>Converte descrições HTML das APIs em texto simples legível.</summary>
public static partial class HtmlText
{
    public static string? ToPlain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var s = BreakTags().Replace(html, "\n");
        s = ListItem().Replace(s, "\n• ");
        s = AnyTag().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = Spaces().Replace(s, " ");
        s = ManyNewlines().Replace(s, "\n\n");
        s = string.Join("\n", s.Split('\n').Select(l => l.Trim()));
        s = ManyNewlines().Replace(s, "\n\n").Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Resumo curto (até ~maxChars) cortado em fim de frase quando possível.</summary>
    public static string? Summarize(string? text, int maxChars = 300)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\n", " ").Trim();
        t = Spaces().Replace(t, " ");
        if (t.Length <= maxChars) return t;
        var cut = t[..maxChars];
        var dot = cut.LastIndexOfAny(new[] { '.', '!', '?' });
        if (dot > maxChars / 2) return cut[..(dot + 1)];
        var sp = cut.LastIndexOf(' ');
        return (sp > 0 ? cut[..sp] : cut).TrimEnd(',', ';', ':') + "…";
    }

    [GeneratedRegex(@"<\s*(br|/p|/h\d|/div|/li|/ul)\s*/?\s*>", RegexOptions.IgnoreCase)] private static partial Regex BreakTags();
    [GeneratedRegex(@"<\s*li[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex ListItem();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex AnyTag();
    [GeneratedRegex(@"[ \t\r\f\v]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex ManyNewlines();
}
