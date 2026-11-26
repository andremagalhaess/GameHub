using System.Text.Json;
using GameHub.Core.Metadata;
using GameHub.Core.Models;

namespace GameHub.Core.Platforms;

/// <summary>
/// Jogos Steam instalados, lidos dos arquivos locais (libraryfolders.vdf + appmanifest_*.acf).
/// Não precisa de login nem de chave. Inicia pelo protocolo steam://rungameid/ID.
/// </summary>
public sealed class SteamLocalScanner : IPlatformScanner
{
    // Ferramentas/redistribuíveis que aparecem como "apps" mas não são jogos.
    private static readonly HashSet<string> NonGameAppIds = new()
    {
        "228980", "1070560", "1391110", "1628350", "1493710", "2180100", "1826330", "250820", "365670", "223850",
    };

    private readonly IRegistryReader _registry;
    private readonly string? _steamPathOverride;

    public SteamLocalScanner(IRegistryReader registry, string? steamPathOverride = null)
    {
        _registry = registry;
        _steamPathOverride = steamPathOverride;
    }

    public string Platform => GameSources.Steam;

    public string? FindSteamPath()
    {
        if (_steamPathOverride is not null) return _steamPathOverride;
        var p = _registry.GetValue("HKCU", @"Software\Valve\Steam", "SteamPath")
             ?? _registry.GetValue("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        return p?.Replace('/', '\\');
    }

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct) => Task.Run(() => Scan(ct), ct);

    private PlatformScanResult Scan(CancellationToken ct)
    {
        var steam = FindSteamPath();
        if (steam is null || !Directory.Exists(steam))
            return new(Platform, Array.Empty<DetectedGame>(), "Steam não está instalada.", Available: false);

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var root = VdfNode.Parse(File.ReadAllText(vdf)).Child("libraryfolders");
            if (root is not null)
                foreach (var lib in root.Children.Values)
                    if (lib["path"] is { } path) libraries.Add(path);
        }

        var games = new Dictionary<string, DetectedGame>();
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var state = VdfNode.Parse(File.ReadAllText(acf)).Child("AppState");
                    var id = state?["appid"];
                    var name = state?["name"];
                    if (id is null || name is null || NonGameAppIds.Contains(id) || IsTool(name)) continue;
                    // StateFlags bit 4 = totalmente instalado
                    var installed = int.TryParse(state!["StateFlags"], out var flags) && (flags & 4) == 4;
                    var dir = state["installdir"] is { } d ? Path.Combine(apps, "common", d) : null;
                    games[id] = new DetectedGame(Platform, id, name, dir, null, $"steam://rungameid/{id}", IsInstalled: installed);
                }
                catch (Exception) { /* manifesto corrompido: ignora */ }
            }
        }
        return new(Platform, games.Values.ToList());
    }

    internal static bool IsTool(string name) =>
        name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Dedicated Server", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(" SDK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Biblioteca completa da conta Steam (inclusive não instalados) com horas jogadas, pela
/// Steam Web API oficial (IPlayerService/GetOwnedGames). Exige chave da Web API e SteamID64,
/// e o perfil com "Detalhes de jogos" público.
/// </summary>
public sealed class SteamWebApiScanner : IPlatformScanner
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<string?> _steamId;

    public SteamWebApiScanner(HttpClient http, Func<string?> apiKey, Func<string?> steamId)
    {
        _http = http;
        _apiKey = apiKey;
        _steamId = steamId;
    }

    public string Platform => GameSources.Steam;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey()) && !string.IsNullOrWhiteSpace(_steamId());

    public async Task<PlatformScanResult> ScanAsync(CancellationToken ct)
    {
        if (!IsConfigured) return new(Platform, Array.Empty<DetectedGame>(), "Steam Web API não configurada.", Available: false);
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                  $"?key={Uri.EscapeDataString(_apiKey()!)}&steamid={Uri.EscapeDataString(_steamId()!.Trim())}&include_appinfo=1&include_played_free_games=1&format=json";
        using var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return new(Platform, Array.Empty<DetectedGame>(), "A Steam recusou a chave da Web API.");
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var response = doc.RootElement.Prop("response");
        if (response is null || response.Value.Prop("games") is null)
            return new(Platform, Array.Empty<DetectedGame>(),
                "A Steam não retornou jogos. Confira o SteamID64 e se \"Detalhes de jogos\" está público no seu perfil.");
        var list = new List<DetectedGame>();
        foreach (var g in response.Value.Arr("games"))
        {
            var id = g.Long("appid");
            var name = g.Str("name");
            if (id is null || name is null || SteamLocalScanner.IsTool(name)) continue;
            var last = g.Long("rtime_last_played") is { } t and > 0 ? DateTimeOffset.FromUnixTimeSeconds(t) : (DateTimeOffset?)null;
            list.Add(new DetectedGame(Platform, id.Value.ToString(), name, LaunchUri: $"steam://rungameid/{id}",
                IsInstalled: false, PlaytimeMinutes: g.Long("playtime_forever") ?? 0, LastPlayedAt: last));
        }
        return new(Platform, list);
    }
}
