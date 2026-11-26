using System.Diagnostics;
using GameHub.Core.Data;
using GameHub.Core.Launching;
using GameHub.Core.Models;
using GameHub.Core.Platforms;

namespace GameHub.Core.Tests;

public class PlatformTests
{
    private sealed class FakeRegistry : IRegistryReader
    {
        public Dictionary<(string, string), Dictionary<string, string>> Keys { get; } = new();
        public void Set(string hive, string key, string name, string value)
        {
            if (!Keys.TryGetValue((hive, key), out var d)) Keys[(hive, key)] = d = new();
            d[name] = value;
        }
        public string? GetValue(string hive, string key, string name) =>
            Keys.TryGetValue((hive, key), out var d) && d.TryGetValue(name, out var v) ? v : null;
        public IReadOnlyList<string> GetSubKeys(string hive, string key) =>
            Keys.Keys.Where(k => k.Item1 == hive && k.Item2.StartsWith(key + "\\"))
                .Select(k => k.Item2[(key.Length + 1)..].Split('\\')[0]).Distinct().ToList();
    }

    [Fact]
    public void Vdf_parses_nested_and_escaped_values()
    {
        var root = VdfNode.Parse("""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "apps" { "570" "123" }
                }
                "1" { "path" "D:\\SteamLibrary" }
            }
            """);
        var libs = root.Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", libs.Child("0")!["path"]);
        Assert.Equal(@"D:\SteamLibrary", libs.Child("1")!["path"]);
        Assert.Equal("123", libs.Child("0")!.Child("apps")!["570"]);
    }

    [Fact]
    public async Task Steam_local_scanner_reads_manifests_and_skips_tools()
    {
        using var dir = new TempDir();
        var steam = dir.File("Steam");
        var apps = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\") + "\" } }");
        File.WriteAllText(Path.Combine(apps, "appmanifest_570.acf"), "\"AppState\" { \"appid\" \"570\" \"name\" \"Dota 2\" \"StateFlags\" \"4\" \"installdir\" \"dota 2 beta\" }");
        File.WriteAllText(Path.Combine(apps, "appmanifest_228980.acf"), "\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"StateFlags\" \"4\" }");
        File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), "lixo {{");

        var r = await new SteamLocalScanner(new FakeRegistry(), steam).ScanAsync(default);
        var g = Assert.Single(r.Games);
        Assert.Equal("Dota 2", g.Title);
        Assert.Equal("steam://rungameid/570", g.LaunchUri);
        Assert.True(g.IsInstalled);
    }

    [Fact]
    public async Task Steam_missing_is_reported_as_unavailable()
    {
        var r = await new SteamLocalScanner(new FakeRegistry()).ScanAsync(default);
        Assert.False(r.Available);
        Assert.Empty(r.Games);
    }

    [Fact]
    public async Task Steam_web_api_imports_owned_games_with_playtime()
    {
        var fake = new FakeHttp(req => req.RequestUri!.Query.Contains("key=K") && req.RequestUri.Query.Contains("steamid=7656")
            ? FakeHttp.Json("""{"response":{"game_count":2,"games":[{"appid":570,"name":"Dota 2","playtime_forever":6000,"rtime_last_played":1700000000},{"appid":620,"name":"Portal 2","playtime_forever":0}]}}""")
            : FakeHttp.Json("""{"response":{}}"""));
        var r = await new SteamWebApiScanner(fake.Client(), () => "K", () => "7656").ScanAsync(default);
        Assert.Equal(2, r.Games.Count);
        Assert.Equal(6000, r.Games[0].PlaytimeMinutes);
        Assert.False(r.Games[0].IsInstalled);

        var priv = await new SteamWebApiScanner(fake.Client(), () => "K", () => "999").ScanAsync(default);
        Assert.Contains("público", priv.Error);
    }

    [Fact]
    public async Task Epic_scanner_reads_item_manifests()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.item"), """
            {"DisplayName":"Alan Wake 2","InstallLocation":"C:\\Games\\AlanWake2","LaunchExecutable":"AlanWake2.exe",
             "AppName":"Dill","CatalogNamespace":"ns1","CatalogItemId":"item1","AppCategories":["public","games","applications"],"bIsIncompleteInstall":false}
            """);
        File.WriteAllText(dir.File("b.item"), """{"DisplayName":"Plugin","AppName":"P","AppCategories":["plugins"]}""");
        File.WriteAllText(dir.File("c.item"), """{"DisplayName":"Baixando","AppName":"X","AppCategories":["games"],"bIsIncompleteInstall":true}""");
        var r = await new EpicScanner(dir.Path).ScanAsync(default);
        var g = Assert.Single(r.Games);
        Assert.Equal("Alan Wake 2", g.Title);
        Assert.Equal("com.epicgames.launcher://apps/ns1%3Aitem1%3ADill?action=launch&silent=true", g.LaunchUri);
    }

    [Fact]
    public async Task Gog_ubisoft_and_uninstall_registry_scanners()
    {
        var reg = new FakeRegistry();
        reg.Set("HKLM", @"SOFTWARE\WOW6432Node\GOG.com\Games\1207658924", "gameName", "The Witcher 3");
        reg.Set("HKLM", @"SOFTWARE\WOW6432Node\GOG.com\Games\1207658924", "exe", @"C:\GOG\Witcher3\bin\x64\witcher3.exe");
        reg.Set("HKLM", @"SOFTWARE\WOW6432Node\GOG.com\Games\1495134320", "gameName", "DLC");
        reg.Set("HKLM", @"SOFTWARE\WOW6432Node\GOG.com\Games\1495134320", "dependsOn", "1207658924");
        reg.Set("HKLM", @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\720", "InstallDir", "C:/Ubisoft/Far Cry 5/");
        const string un = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        reg.Set("HKLM", un + @"\Riot Game valorant.live", "DisplayName", "VALORANT");
        reg.Set("HKLM", un + @"\Riot Game valorant.live", "Publisher", "Riot Games, Inc");
        reg.Set("HKLM", un + @"\Riot Game valorant.live", "UninstallString", "\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=valorant --uninstall-patchline=live");
        reg.Set("HKLM", un + @"\Riot Client", "DisplayName", "Riot Client");
        reg.Set("HKLM", un + @"\Riot Client", "Publisher", "Riot Games, Inc");
        reg.Set("HKLM", un + @"\Overwatch", "DisplayName", "Overwatch");
        reg.Set("HKLM", un + @"\Overwatch", "Publisher", "Blizzard Entertainment");
        reg.Set("HKLM", un + @"\Overwatch", "DisplayIcon", "\"C:\\Overwatch\\Overwatch Launcher.exe\",0");
        reg.Set("HKLM", un + @"\Office", "DisplayName", "Office");
        reg.Set("HKLM", un + @"\Office", "Publisher", "Microsoft");

        var gog = await new GogScanner(reg).ScanAsync(default);
        Assert.Equal("The Witcher 3", Assert.Single(gog.Games).Title);

        var ubi = Assert.Single((await new UbisoftScanner(reg).ScanAsync(default)).Games);
        Assert.Equal("Far Cry 5", ubi.Title);
        Assert.Equal("uplay://launch/720/0", ubi.LaunchUri);

        var others = (await new UninstallRegistryScanner(reg).ScanAsync(default)).Games;
        Assert.Equal(2, others.Count);
        var val = others.Single(g => g.Title == "VALORANT");
        Assert.Equal(GameSources.Riot, val.Source);
        Assert.Equal(@"C:\Riot Games\Riot Client\RiotClientServices.exe", val.ExecutablePath);
        Assert.Equal("--launch-product=valorant --launch-patchline=live", val.LaunchArguments);
        Assert.Equal(@"C:\Overwatch\Overwatch Launcher.exe", others.Single(g => g.Title == "Overwatch").ExecutablePath);
    }

    [Fact]
    public void Xbox_config_is_parsed()
    {
        var g = XboxScanner.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="Microsoft.Forza5" Publisher="CN=Microsoft" Version="1.0.0.0" />
              <ExecutableList><Executable Name="ForzaHorizon5.exe" Id="Game" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Forza Horizon 5" />
            </Game>
            """, Path.Combine("X:", "XboxGames", "Forza Horizon 5", "Content"));
        Assert.Equal("Forza Horizon 5", g!.Title);
        Assert.EndsWith("ForzaHorizon5.exe", g.ExecutablePath);
        Assert.Equal("Microsoft.Forza5", g.SourceId);
    }

    [Fact]
    public void Import_adds_links_and_updates_without_duplicates()
    {
        using var env = new TestEnv();
        var sources = new GameSourceRepository(env.Db);
        var svc = new LibraryImportService(env.Games, sources, Array.Empty<IPlatformScanner>());
        // Jogo já cadastrado manualmente pelo EXE.
        env.Games.Add(new Game { Title = "The Witcher 3: Wild Hunt", ExecutablePath = @"C:\GOG\Witcher3\bin\x64\witcher3.exe", PlaytimeMinutes = 30 });

        var steamLocal = new PlatformScanResult(GameSources.Steam, new[]
        {
            new DetectedGame(GameSources.Steam, "570", "Dota 2", LaunchUri: "steam://rungameid/570"),
            new DetectedGame(GameSources.Steam, "292030", "The Witcher® 3: Wild Hunt", LaunchUri: "steam://rungameid/292030"),
        });
        var steamApi = new PlatformScanResult(GameSources.Steam, new[]
        {
            new DetectedGame(GameSources.Steam, "570", "Dota 2", IsInstalled: false, PlaytimeMinutes: 6000),
            new DetectedGame(GameSources.Steam, "620", "Portal 2", LaunchUri: "steam://rungameid/620", IsInstalled: false, PlaytimeMinutes: 0),
        });
        var gog = new PlatformScanResult(GameSources.Gog, new[]
        {
            new DetectedGame(GameSources.Gog, "1207658924", "The Witcher 3", ExecutablePath: @"C:\GOG\Witcher3\bin\x64\witcher3.exe"),
        });

        var report = svc.Apply(new[] { steamLocal, steamApi, gog });
        Assert.Equal(2, report.Added);           // Dota 2, Portal 2
        Assert.Equal(2, report.Linked);          // Witcher via Steam (nome) e via GOG (exe)
        Assert.Equal(3, env.Games.Count());

        var dota = env.Games.Query(new GameQuery(Search: "Dota")).Single();
        Assert.Equal(6000, dota.PlatformPlaytimeMinutes);
        Assert.Equal(6000, dota.TotalPlaytimeMinutes);
        Assert.Equal("steam://rungameid/570", dota.LaunchUri);
        Assert.True(sources.ForGame(dota.Id).Single().IsInstalled);

        var witcher = env.Games.Query(new GameQuery(Search: "Witcher")).Single();
        Assert.Equal(2, sources.ForGame(witcher.Id).Count);
        Assert.Equal(@"C:\GOG\Witcher3\bin\x64\witcher3.exe", witcher.ExecutablePath); // mantém o EXE escolhido
        Assert.Single(env.Games.Query(new GameQuery(Source: GameSources.Gog)));

        // Reimportar não duplica; jogo desinstalado é marcado, não apagado.
        var again = svc.Apply(new[] { new PlatformScanResult(GameSources.Steam, new[] { steamLocal.Games[1] }) });
        Assert.Equal(0, again.Added);
        Assert.Equal(3, env.Games.Count());
        Assert.False(sources.Get(GameSources.Steam, "570")!.IsInstalled);

        // Removido pelo usuário: não volta.
        env.Games.Remove(dota.Id);
        var third = svc.Apply(new[] { steamLocal });
        Assert.Equal(1, third.Ignored);
        Assert.Empty(env.Games.Query(new GameQuery(Search: "Dota")));
    }

    [Fact]
    public void Launcher_uses_allowed_protocols_only()
    {
        using var env = new TestEnv();
        var started = new List<ProcessStartInfo>();
        var starter = new ListStarter(started);
        var launcher = new GameLauncher(env.Games, starter);
        var id = env.Games.Add(new Game { Title = "Dota 2", LaunchUri = "steam://rungameid/570" });
        Assert.Equal(LaunchOutcome.Started, launcher.Launch(env.Games.Get(id)!).Outcome);
        Assert.Equal("steam://rungameid/570", started.Single().FileName);
        Assert.NotNull(env.Games.Get(id)!.LastPlayedAt);

        Assert.Equal(LaunchOutcome.Failed, launcher.Launch(new Game { Title = "x", LaunchUri = "https://malicioso.example/x.exe" }).Outcome);
        Assert.Equal(LaunchOutcome.Failed, launcher.Launch(new Game { Title = "x", LaunchUri = "file:///C:/Windows/System32/cmd.exe" }).Outcome);
        Assert.Single(started);
    }

    private sealed class ListStarter(List<ProcessStartInfo> list) : IProcessStarter
    {
        public ILaunchedProcess? Start(ProcessStartInfo info) { list.Add(info); return null; }
    }
}
