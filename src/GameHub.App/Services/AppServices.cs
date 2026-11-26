using System.Net;
using GameHub.Core;
using GameHub.Core.Backup;
using GameHub.Core.Data;
using GameHub.Core.Images;
using GameHub.Core.Journey;
using GameHub.Core.Launching;
using GameHub.Core.Metadata;
using GameHub.Core.Platforms;
using GameHub.Core.Security;

namespace GameHub.App.Services;

/// <summary>Composição dos serviços (um único HttpClient, um único banco).</summary>
public static class AppServices
{
    public static AppPaths Paths { get; private set; } = null!;
    public static Database Db { get; private set; } = null!;
    public static GameRepository Games { get; private set; } = null!;
    public static GameSourceRepository Sources { get; private set; } = null!;
    public static ReviewRepository Reviews { get; private set; } = null!;
    public static MemoryRepository Memories { get; private set; } = null!;
    public static RankingRepository Rankings { get; private set; } = null!;
    public static JourneyService Journey { get; private set; } = null!;
    public static LibraryImportService Importer { get; private set; } = null!;
    public static ProfileRepository Profiles { get; private set; } = null!;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static SecretStore Secrets { get; private set; } = null!;
    public static MetadataService Metadata { get; private set; } = null!;
    public static ImageCache Images { get; private set; } = null!;
    public static GameLauncher Launcher { get; private set; } = null!;
    public static BackupService Backup { get; private set; } = null!;
    public static HttpClient Http { get; private set; } = null!;

    /// <summary>Nome exibido de cada varredura (a Web API da Steam aparece separada da detecção local).</summary>
    public static string ScannerName(IPlatformScanner s) => s is SteamWebApiScanner ? "Steam (conta, via Web API)" : s is SteamLocalScanner ? "Steam (jogos instalados)" : s.Platform;

    public static bool IsScannerEnabled(IPlatformScanner s) =>
        s is SteamWebApiScanner web ? web.IsConfigured : Settings.GetBool(SettingKeys.PlatformEnabledPrefix + ScannerName(s), true);

    public static void Initialize()
    {
        Paths = new AppPaths();
        Db = new Database(Paths.DatabaseFile);
        Db.Migrate();
        Games = new GameRepository(Db);
        Sources = new GameSourceRepository(Db);
        Reviews = new ReviewRepository(Db);
        Memories = new MemoryRepository(Db);
        Rankings = new RankingRepository(Db, Games);
        Journey = new JourneyService(Games, Reviews, Memories);
        Profiles = new ProfileRepository(Db);
        Settings = new SettingsRepository(Db);
        Secrets = new SecretStore(Settings, new DpapiSecretProtector());

        Http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30), // não segura conexões ociosas
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GameHub/0.1");

        Metadata = new MetadataService(new IMetadataProvider[]
        {
            new IgdbProvider(Http, () => Secrets.Get(SettingKeys.IgdbClientId), () => Secrets.Get(SettingKeys.IgdbClientSecret)),
            new RawgProvider(Http, () => Secrets.Get(SettingKeys.RawgApiKey)),
            new SteamStoreProvider(Http, () => Settings.GetBool(SettingKeys.SteamEnabled, true)),
        });
        Images = new ImageCache(Http, Paths.ImagesDir);
        Launcher = new GameLauncher(Games);
        Backup = new BackupService(Paths, Db);

        var registry = new WindowsRegistry();
        Importer = new LibraryImportService(Games, Sources, new IPlatformScanner[]
        {
            new SteamLocalScanner(registry),
            new SteamWebApiScanner(Http, () => Secrets.Get(SettingKeys.SteamWebApiKey), () => Settings.Get(SettingKeys.SteamId)),
            new EpicScanner(),
            new GogScanner(registry),
            new UbisoftScanner(registry),
            new UninstallRegistryScanner(registry),
            new XboxScanner(),
        });
    }
}
