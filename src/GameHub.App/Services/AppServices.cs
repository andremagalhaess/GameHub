using System.Net;
using GameHub.Core;
using GameHub.Core.Backup;
using GameHub.Core.Data;
using GameHub.Core.Images;
using GameHub.Core.Launching;
using GameHub.Core.Metadata;
using GameHub.Core.Security;

namespace GameHub.App.Services;

/// <summary>Composição dos serviços (um único HttpClient, um único banco).</summary>
public static class AppServices
{
    public static AppPaths Paths { get; private set; } = null!;
    public static Database Db { get; private set; } = null!;
    public static GameRepository Games { get; private set; } = null!;
    public static ProfileRepository Profiles { get; private set; } = null!;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static SecretStore Secrets { get; private set; } = null!;
    public static MetadataService Metadata { get; private set; } = null!;
    public static ImageCache Images { get; private set; } = null!;
    public static GameLauncher Launcher { get; private set; } = null!;
    public static BackupService Backup { get; private set; } = null!;
    public static HttpClient Http { get; private set; } = null!;

    public static void Initialize()
    {
        Paths = new AppPaths();
        Db = new Database(Paths.DatabaseFile);
        Db.Migrate();
        Games = new GameRepository(Db);
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
    }
}
