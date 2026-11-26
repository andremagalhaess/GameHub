using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GameHub.Core.Metadata;
using GameHub.Core.Models;

namespace GameHub.Core.Platforms;

/// <summary>Epic Games Store: manifestos locais (*.item) em ProgramData. Inicia pelo protocolo do launcher.</summary>
public sealed class EpicScanner : IPlatformScanner
{
    private readonly string _manifestsDir;

    public EpicScanner(string? manifestsDir = null) =>
        _manifestsDir = manifestsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public string Platform => GameSources.Epic;

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct) => Task.Run(() =>
    {
        if (!Directory.Exists(_manifestsDir))
            return new PlatformScanResult(Platform, Array.Empty<DetectedGame>(), "Epic Games Launcher não encontrado.", Available: false);
        var list = new List<DetectedGame>();
        foreach (var file in Directory.EnumerateFiles(_manifestsDir, "*.item"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var m = doc.RootElement;
                if (m.Bool("bIsIncompleteInstall")) continue;
                var categories = m.Arr("AppCategories").Select(c => c.GetString()).ToList();
                if (categories.Count > 0 && !categories.Contains("games")) continue; // plugins, ferramentas
                var appName = m.Str("AppName");
                var title = m.Str("DisplayName");
                if (appName is null || title is null) continue;
                var dir = m.Str("InstallLocation");
                var exe = dir is not null && m.Str("LaunchExecutable") is { } le ? Path.Combine(dir, le) : null;
                var ns = m.Str("CatalogNamespace");
                var item = m.Str("CatalogItemId");
                var uri = ns is not null && item is not null
                    ? $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{appName}?action=launch&silent=true"
                    : $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true";
                list.Add(new DetectedGame(Platform, appName, title, dir, exe, uri));
            }
            catch (Exception) { }
        }
        return new PlatformScanResult(Platform, list);
    }, ct);
}

/// <summary>GOG: registro do Windows (jogos DRM-free iniciados direto pelo executável).</summary>
public sealed class GogScanner : IPlatformScanner
{
    private const string Key = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    private readonly IRegistryReader _reg;
    public GogScanner(IRegistryReader reg) => _reg = reg;
    public string Platform => GameSources.Gog;

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct)
    {
        var ids = _reg.GetSubKeys("HKLM", Key);
        if (ids.Count == 0) return Task.FromResult(new PlatformScanResult(Platform, Array.Empty<DetectedGame>(), "Nenhum jogo GOG encontrado.", Available: false));
        var list = new List<DetectedGame>();
        foreach (var id in ids)
        {
            var k = $@"{Key}\{id}";
            var name = _reg.GetValue("HKLM", k, "gameName");
            if (name is null) continue;
            if (_reg.GetValue("HKLM", k, "dependsOn") is { Length: > 0 }) continue; // DLC
            var exe = _reg.GetValue("HKLM", k, "exe");
            list.Add(new DetectedGame(Platform, id, name, _reg.GetValue("HKLM", k, "path"), exe,
                exe is null ? $"goggalaxy://openGameView/{id}" : null, _reg.GetValue("HKLM", k, "launchParam")));
        }
        return Task.FromResult(new PlatformScanResult(Platform, list));
    }
}

/// <summary>Ubisoft Connect: registro (Launcher\Installs). Inicia pelo protocolo uplay://.</summary>
public sealed class UbisoftScanner : IPlatformScanner
{
    private const string Key = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    private readonly IRegistryReader _reg;
    public UbisoftScanner(IRegistryReader reg) => _reg = reg;
    public string Platform => GameSources.Ubisoft;

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct)
    {
        var ids = _reg.GetSubKeys("HKLM", Key);
        if (ids.Count == 0) return Task.FromResult(new PlatformScanResult(Platform, Array.Empty<DetectedGame>(), "Ubisoft Connect não encontrado.", Available: false));
        var list = new List<DetectedGame>();
        foreach (var id in ids)
        {
            var dir = _reg.GetValue("HKLM", $@"{Key}\{id}", "InstallDir")?.Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var title = dir.Split('\\').Last();
            list.Add(new DetectedGame(Platform, id, title, dir, null, $"uplay://launch/{id}/0"));
        }
        return Task.FromResult(new PlatformScanResult(Platform, list));
    }
}

/// <summary>
/// EA app, Battle.net, Riot, Rockstar e Amazon Games pelas entradas de "Programas instalados" do Windows
/// (não há API pública de biblioteca nesses launchers). Usa o ícone/executável registrado pelo próprio instalador.
/// </summary>
public sealed class UninstallRegistryScanner : IPlatformScanner
{
    private static readonly string[] Roots =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private static readonly (string Publisher, string Source)[] Publishers =
    {
        ("Electronic Arts", GameSources.Ea),
        ("Blizzard Entertainment", GameSources.BattleNet),
        ("Riot Games", GameSources.Riot),
        ("Rockstar Games", GameSources.Rockstar),
        ("Amazon Game Studios", GameSources.Amazon),
        ("Amazon.com Services", GameSources.Amazon),
    };

    // Os próprios launchers não são jogos.
    private static readonly string[] LauncherNames =
    {
        "EA app", "Origin", "Battle.net", "Riot Client", "Rockstar Games Launcher", "Rockstar Games Social Club",
        "Amazon Games", "Ubisoft Connect", "Epic Games Launcher", "Steam", "GOG GALAXY",
    };

    private readonly IRegistryReader _reg;
    public UninstallRegistryScanner(IRegistryReader reg) => _reg = reg;
    public string Platform => "Outros launchers";

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct)
    {
        var list = new List<DetectedGame>();
        foreach (var hive in new[] { "HKLM", "HKCU" })
        foreach (var root in Roots)
        foreach (var sub in _reg.GetSubKeys(hive, root))
        {
            var k = $@"{root}\{sub}";
            var publisher = _reg.GetValue(hive, k, "Publisher");
            var name = _reg.GetValue(hive, k, "DisplayName");
            if (publisher is null || name is null) continue;
            var src = Publishers.FirstOrDefault(p => publisher.StartsWith(p.Publisher, StringComparison.OrdinalIgnoreCase)).Source;
            if (src is null || LauncherNames.Any(l => name.StartsWith(l, StringComparison.OrdinalIgnoreCase))) continue;
            if (_reg.GetValue(hive, k, "SystemComponent") == "1") continue;

            var dir = _reg.GetValue(hive, k, "InstallLocation")?.Trim('"');
            var uninstall = _reg.GetValue(hive, k, "UninstallString");
            string? exe = null, args = null;

            if (src == GameSources.Riot && uninstall is not null && RiotLaunch(uninstall) is { } riot)
            {
                (exe, args) = riot;
            }
            else
            {
                var icon = _reg.GetValue(hive, k, "DisplayIcon");
                if (icon is not null)
                {
                    var path = icon.Split(',')[0].Trim('"');
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !LooksLikeUninstaller(path)) exe = path;
                }
            }
            list.Add(new DetectedGame(src, sub, name, dir, exe, null, args));
        }
        return Task.FromResult(new PlatformScanResult(Platform, list.DistinctBy(g => (g.Source, g.SourceId)).ToList()));
    }

    private static bool LooksLikeUninstaller(string path)
    {
        var f = Path.GetFileName(path).ToLowerInvariant();
        return f.Contains("unins") || f.Contains("uninstall") || f.Contains("remover") || f.Contains("cleanup");
    }

    /// <summary>
    /// A desinstalação da Riot é "RiotClientServices.exe --uninstall-product=valorant --uninstall-patchline=live";
    /// o equivalente para jogar é "--launch-product=... --launch-patchline=...".
    /// </summary>
    public static (string Exe, string Args)? RiotLaunch(string uninstallString)
    {
        var m = Regex.Match(uninstallString, "\"?(?<exe>[^\"]*RiotClientServices\\.exe)\"?\\s+--uninstall-product=(?<p>\\S+)\\s+--uninstall-patchline=(?<l>\\S+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return (m.Groups["exe"].Value, $"--launch-product={m.Groups["p"].Value} --launch-patchline={m.Groups["l"].Value}");
    }
}

/// <summary>Xbox app / PC Game Pass / Microsoft Store: pastas XboxGames com MicrosoftGame.config.</summary>
public sealed class XboxScanner : IPlatformScanner
{
    private readonly Func<IEnumerable<string>> _roots;

    public XboxScanner(Func<IEnumerable<string>>? roots = null) =>
        _roots = roots ?? (() => DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => Path.Combine(d.RootDirectory.FullName, "XboxGames")));

    public string Platform => GameSources.Xbox;

    public Task<PlatformScanResult> ScanAsync(CancellationToken ct) => Task.Run(() =>
    {
        var list = new List<DetectedGame>();
        var anyRoot = false;
        foreach (var root in _roots())
        {
            if (!Directory.Exists(root)) continue;
            anyRoot = true;
            foreach (var gameDir in Directory.EnumerateDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                var config = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                if (!File.Exists(config)) continue;
                if (Parse(File.ReadAllText(config), Path.Combine(gameDir, "Content")) is { } g) list.Add(g);
            }
        }
        return anyRoot
            ? new PlatformScanResult(Platform, list)
            : new PlatformScanResult(Platform, Array.Empty<DetectedGame>(), "Nenhuma pasta XboxGames encontrada.", Available: false);
    }, ct);

    public static DetectedGame? Parse(string xml, string contentDir)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            var name = identity?.Attribute("Name")?.Value;
            var visuals = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
            var title = visuals?.Attribute("DefaultDisplayName")?.Value;
            if (string.IsNullOrWhiteSpace(title) || title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                title = Path.GetFileName(Path.GetDirectoryName(contentDir.TrimEnd('\\', '/')));
            var exeName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable")?.Attribute("Name")?.Value;
            if (name is null || title is null) return null;
            var exe = exeName is null ? null : Path.Combine(contentDir, exeName);
            return new DetectedGame(GameSources.Xbox, name, title, contentDir, exe);
        }
        catch (Exception) { return null; }
    }
}
