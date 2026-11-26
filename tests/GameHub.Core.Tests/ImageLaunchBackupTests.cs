using System.Diagnostics;
using System.Net;
using GameHub.Core.Backup;
using GameHub.Core.Data;
using GameHub.Core.Images;
using GameHub.Core.Launching;
using GameHub.Core.Metadata;
using GameHub.Core.Models;

namespace GameHub.Core.Tests;

public class ImageCacheTests
{
    [Fact]
    public async Task Downloads_once_and_reuses_cache()
    {
        using var dir = new TempDir();
        var fake = new FakeHttp(_ => FakeHttp.Bytes(Png.OnePixel));
        var cache = new ImageCache(fake.Client(), dir.File("img"));
        var p1 = await cache.DownloadAsync("https://cdn/x/cover.jpg", ImageKind.Cover);
        var p2 = await cache.DownloadAsync("https://cdn/x/cover.jpg", ImageKind.Cover);
        Assert.Equal(p1, p2);
        Assert.EndsWith(".png", p1); // extensão vem do conteúdo, não da URL
        Assert.Single(fake.Requests);
        Assert.True(cache.SizeBytes() > 0);
    }

    [Fact]
    public async Task Rejects_non_images_and_bad_urls()
    {
        using var dir = new TempDir();
        var fake = new FakeHttp(_ => FakeHttp.Json("<html>erro</html>"));
        var cache = new ImageCache(fake.Client(), dir.File("img"));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.DownloadAsync("https://cdn/a.jpg", ImageKind.Cover));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.DownloadAsync("file:///C:/x.png", ImageKind.Cover));
        Assert.Empty(Directory.EnumerateFiles(dir.File("img"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Http_errors_propagate()
    {
        using var dir = new TempDir();
        var cache = new ImageCache(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).Client(), dir.File("img"));
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.DownloadAsync("https://cdn/404.jpg", ImageKind.Logo));
    }

    [Fact]
    public async Task Imports_local_file_and_removes_unused()
    {
        using var dir = new TempDir();
        var src = dir.File("minha-capa.png");
        await File.WriteAllBytesAsync(src, Png.OnePixel);
        var cache = new ImageCache(new FakeHttp(_ => FakeHttp.Bytes(Png.OnePixel)).Client(), dir.File("img"));
        var kept = await cache.ImportLocalAsync(src, ImageKind.Cover);
        var unused = await cache.DownloadAsync("https://cdn/other", ImageKind.Background);
        var freed = cache.RemoveUnused(new[] { kept });
        Assert.True(freed > 0);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(unused));
    }
}

public class LauncherTests
{
    private sealed class FakeStarter : IProcessStarter
    {
        public ProcessStartInfo? Last;
        public TaskCompletionSource Exit = new();
        public Exception? Throw;
        public ILaunchedProcess? Start(ProcessStartInfo info)
        {
            if (Throw is not null) throw Throw;
            Last = info;
            return new Proc(Exit.Task);
        }
        private sealed class Proc(Task t) : ILaunchedProcess { public Task? WaitForExitAsync() => t; }
    }

    [Fact]
    public async Task Launches_with_args_and_records_session()
    {
        using var env = new TestEnv();
        var exe = env.Dir.File("Jogo.exe");
        File.WriteAllText(exe, "");
        var id = env.Games.Add(new Game { Title = "Jogo", ExecutablePath = exe, LaunchArguments = "-windowed" });
        var now = new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero);
        var starter = new FakeStarter();
        var launcher = new GameLauncher(env.Games, starter, () => now);

        var result = launcher.Launch(env.Games.Get(id)!);
        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(exe, starter.Last!.FileName);
        Assert.Equal("-windowed", starter.Last.Arguments);
        Assert.Equal(env.Dir.Path, starter.Last.WorkingDirectory);
        Assert.True(starter.Last.UseShellExecute);
        Assert.NotNull(env.Games.Get(id)!.LastPlayedAt);

        now = now.AddMinutes(42);
        starter.Exit.SetResult();
        Assert.Equal(42, await result.SessionMinutes!);
        Assert.Equal(42, env.Games.Get(id)!.PlaytimeMinutes);
    }

    [Fact]
    public async Task Short_lived_launcher_stub_is_not_counted()
    {
        using var env = new TestEnv();
        var exe = env.Dir.File("Stub.exe");
        File.WriteAllText(exe, "");
        var id = env.Games.Add(new Game { Title = "Stub", ExecutablePath = exe });
        var now = DateTimeOffset.UtcNow;
        var starter = new FakeStarter();
        var result = new GameLauncher(env.Games, starter, () => now).Launch(env.Games.Get(id)!);
        now = now.AddSeconds(5);
        starter.Exit.SetResult();
        Assert.Null(await result.SessionMinutes!);
        Assert.Equal(0, env.Games.Get(id)!.PlaytimeMinutes);
        Assert.Empty(env.Games.Sessions(id));
    }

    [Fact]
    public void Missing_or_unset_executable_is_reported()
    {
        using var env = new TestEnv();
        var launcher = new GameLauncher(env.Games, new FakeStarter());
        Assert.Equal(LaunchOutcome.NoExecutableConfigured, launcher.Launch(new Game { Title = "x" }).Outcome);
        Assert.Equal(LaunchOutcome.MissingExecutable, launcher.Launch(new Game { Title = "x", ExecutablePath = env.Dir.File("nao.exe") }).Outcome);
    }

    [Fact]
    public void Uac_cancel_is_reported()
    {
        using var env = new TestEnv();
        var exe = env.Dir.File("Admin.exe");
        File.WriteAllText(exe, "");
        var starter = new FakeStarter { Throw = new System.ComponentModel.Win32Exception(1223) };
        var r = new GameLauncher(env.Games, starter).Launch(new Game { Id = 1, Title = "x", ExecutablePath = exe });
        Assert.Equal(LaunchOutcome.Cancelled, r.Outcome);
    }
}

public class BackupTests
{
    [Fact]
    public async Task Backup_and_restore_roundtrip_with_path_rewrite()
    {
        using var env = new TestEnv();
        var cache = new ImageCache(new FakeHttp(_ => FakeHttp.Bytes(Png.OnePixel)).Client(), env.Paths.ImagesDir);
        var cover = await cache.DownloadAsync("https://cdn/c.jpg", ImageKind.Cover);
        var id = env.Games.Add(new Game { Title = "Hades", CoverPath = cover, Status = GameStatus.Beaten, PersonalRating = 10 });
        new ProfileRepository(env.Db).Save(new PlayerProfile { DisplayName = "André", AvatarPath = cover });

        var zip = env.Dir.File("backup.zip");
        await new BackupService(env.Paths, env.Db).CreateAsync(zip);
        Assert.True(File.Exists(zip));

        // Restaura em outra instalação (outra pasta, outro "PC").
        var otherPaths = new AppPaths(env.Dir.File("outro-pc"));
        var otherDb = new Database(otherPaths.DatabaseFile);
        otherDb.Migrate();
        new GameRepository(otherDb).Add(new Game { Title = "Será substituído" });
        var safety = await new BackupService(otherPaths, otherDb).RestoreAsync(zip);

        Assert.True(File.Exists(safety));
        var games = new GameRepository(otherDb).All();
        var g = Assert.Single(games);
        Assert.Equal("Hades", g.Title);
        Assert.Equal(10, g.PersonalRating);
        Assert.StartsWith(otherPaths.ImagesDir, g.CoverPath);
        Assert.True(File.Exists(g.CoverPath));
        Assert.StartsWith(otherPaths.ImagesDir, new ProfileRepository(otherDb).Get().AvatarPath);
    }

    [Fact]
    public async Task Rejects_non_backup_zip()
    {
        using var env = new TestEnv();
        var zip = env.Dir.File("x.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create)) z.CreateEntry("a.txt");
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(env.Paths, env.Db).RestoreAsync(zip));
        Assert.Equal(0, env.Games.Count());
    }
}
