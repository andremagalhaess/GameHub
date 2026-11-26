namespace GameHub.App.Helpers;

/// <summary>Links externos abertos no navegador padrão (só quando o usuário clica).</summary>
public static class WebLinks
{
    public static Uri HowLongToBeat(string? title) =>
        new("https://howlongtobeat.com/?q=" + Uri.EscapeDataString((title ?? "").Trim()));

    public static Uri Backloggd(string? title) =>
        new("https://www.backloggd.com/search/games/" + Uri.EscapeDataString((title ?? "").Trim()) + "/");

    public static async void Open(Uri uri)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(uri); }
        catch (Exception ex) { GameHub.App.Services.Log.Error("Falha ao abrir link", ex); }
    }
}
