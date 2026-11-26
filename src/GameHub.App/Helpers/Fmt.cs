using System.Globalization;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;

namespace GameHub.App.Helpers;

/// <summary>Formatação em português do Brasil, usada também por x:Bind.</summary>
public static class Fmt
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Hours(long minutes)
    {
        if (minutes <= 0) return "0 h";
        if (minutes < 60) return $"{minutes} min";
        var h = minutes / 60.0;
        return h < 10 ? $"{h.ToString("0.#", PtBr)} h" : $"{Math.Round(h).ToString("N0", PtBr)} h";
    }

    public static string ReleaseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "—";
        return DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d 'de' MMMM 'de' yyyy", PtBr)
            : iso;
    }

    public static string Year(Game g) => g.ReleaseYear?.ToString() ?? "";

    public static string When(DateTimeOffset? d)
    {
        if (d is null) return "Nunca";
        var local = d.Value.ToLocalTime();
        var days = (DateTime.Today - local.Date).TotalDays;
        return days switch
        {
            0 => $"Hoje, {local:HH:mm}",
            1 => $"Ontem, {local:HH:mm}",
            < 7 => $"Há {days:0} dias",
            _ => local.ToString("d 'de' MMM 'de' yyyy", PtBr),
        };
    }

    public static string Status(GameStatus s) => s.ToDisplay();
    public static Visibility StatusBadge(GameStatus s) => s == GameStatus.None ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility IfTrue(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility IfText(string? s) => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility IfNoText(string? s) => string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
    public static string Or(string? s, string fallback = "—") => string.IsNullOrWhiteSpace(s) ? fallback : s;
    public static string List(IEnumerable<string> items) => items.Any() ? string.Join(", ", items) : "—";

    public static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 5 ? "Boa noite" : h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
    }

    /// <summary>Lista separada por vírgulas → itens limpos.</summary>
    public static List<string> SplitList(string? text) =>
        (text ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
