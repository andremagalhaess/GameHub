using System.Diagnostics;
using GameHub.Core.Data;
using GameHub.Core.Models;
using GameHub.Core.Security;

namespace GameHub.Core.Tests;

public class DatabaseTests
{
    private static Game NewGame(string title, string? exe = null) => new()
    {
        Title = title,
        ExecutablePath = exe,
        Genres = new() { "RPG", "Ação" },
        Platforms = new() { "PC (Windows)" },
        Developer = "Estúdio",
        ReleaseDate = "2020-12-10",
    };

    [Fact]
    public void Migrate_is_idempotent_and_versioned()
    {
        using var env = new TestEnv();
        env.Db.Migrate();
        env.Db.Migrate();
        Assert.Equal(4, env.Db.SchemaVersion);
    }

    [Fact]
    public void Add_get_update_remove_roundtrip()
    {
        using var env = new TestEnv();
        var g = NewGame("Cyberpunk 2077", @"C:\Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe");
        g.Tags = new() { "mundo aberto" };
        g.ExternalRating = 86; g.ExternalRatingSource = "Metacritic";
        var id = env.Games.Add(g);

        var loaded = env.Games.Get(id)!;
        Assert.Equal("Cyberpunk 2077", loaded.Title);
        Assert.Equal(new[] { "RPG", "Ação" }, loaded.Genres);
        Assert.Equal(2020, loaded.ReleaseYear);
        Assert.Equal(86, loaded.ExternalRating);

        loaded.Status = GameStatus.Playing;
        loaded.PersonalRating = 9.5;
        loaded.LaunchArguments = "-skipStartScreen";
        env.Games.Update(loaded);
        var again = env.Games.Get(id)!;
        Assert.Equal(GameStatus.Playing, again.Status);
        Assert.Equal(9.5, again.PersonalRating);
        Assert.Equal("-skipStartScreen", again.LaunchArguments);

        Assert.True(env.Games.Remove(id));
        Assert.Null(env.Games.Get(id));
    }

    [Fact]
    public void Remove_does_not_touch_game_files()
    {
        using var env = new TestEnv();
        var exe = env.Dir.File("Jogo.exe");
        File.WriteAllText(exe, "x");
        var id = env.Games.Add(NewGame("Jogo", exe));
        env.Games.Remove(id);
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void Rejects_invalid_rating_and_empty_title()
    {
        using var env = new TestEnv();
        Assert.Throws<ArgumentException>(() => env.Games.Add(new Game { Title = " " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => env.Games.Add(new Game { Title = "X", PersonalRating = 11 }));
    }

    [Fact]
    public void Detects_duplicates_by_exe_title_and_metadata()
    {
        using var env = new TestEnv();
        env.Games.Add(new Game { Title = "The Witcher 3: Wild Hunt", ExecutablePath = @"C:\G\witcher3.exe", MetadataProvider = "IGDB", MetadataId = "1942" });

        Assert.Equal("Mesmo executável", Assert.Single(env.Games.FindDuplicates(new Game { Title = "Outro", ExecutablePath = @"c:\g\WITCHER3.exe" })).Reason);
        Assert.Equal("Mesmo nome", Assert.Single(env.Games.FindDuplicates(new Game { Title = "Witcher III: Wild Hunt" })).Reason);
        Assert.Equal("Mesmo jogo nos metadados online", Assert.Single(env.Games.FindDuplicates(new Game { Title = "W3", MetadataProvider = "IGDB", MetadataId = "1942" })).Reason);
        Assert.Empty(env.Games.FindDuplicates(new Game { Title = "Hades" }));
    }

    [Fact]
    public void Query_filters_search_status_favorites_and_sorts()
    {
        using var env = new TestEnv();
        var a = env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, IsFavorite = true, Genres = new() { "Roguelike" } });
        env.Games.Add(new Game { Title = "Hollow Knight", Status = GameStatus.Playing });
        env.Games.Add(new Game { Title = "Celeste", Status = GameStatus.Backlog, Developer = "Maddy Makes Games" });

        Assert.Equal(new[] { "Celeste", "Hades", "Hollow Knight" }, env.Games.All().Select(g => g.Title));
        Assert.Equal("Hollow Knight", Assert.Single(env.Games.Query(new GameQuery(Search: "knight"))).Title);
        Assert.Equal("Celeste", Assert.Single(env.Games.Query(new GameQuery(Search: "maddy"))).Title);
        Assert.Equal("Hades", Assert.Single(env.Games.Query(new GameQuery(Search: "rogue"))).Title);
        Assert.Equal("Hades", Assert.Single(env.Games.Query(new GameQuery(FavoritesOnly: true))).Title);
        Assert.Equal("Hollow Knight", Assert.Single(env.Games.Query(new GameQuery(Status: GameStatus.Playing))).Title);
        Assert.Empty(env.Games.Query(new GameQuery(Search: "%")));

        env.Games.SetFavorite(a, false);
        Assert.Empty(env.Games.Query(new GameQuery(FavoritesOnly: true)));
    }

    [Fact]
    public void Sessions_add_playtime_and_stats_reflect_real_data()
    {
        using var env = new TestEnv();
        var id = env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, PersonalRating = 9, Genres = new() { "Roguelike" } });
        env.Games.Add(new Game { Title = "Celeste", Status = GameStatus.Completed100, PersonalRating = 10, Genres = new() { "Plataforma" }, IsFavorite = true });
        env.Games.Add(new Game { Title = "Novo", Status = GameStatus.Backlog });
        var start = new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
        env.Games.RecordSession(id, start, start.AddMinutes(95));

        Assert.Equal(95, env.Games.Get(id)!.PlaytimeMinutes);
        Assert.Single(env.Games.Sessions(id));

        var s = env.Games.Stats();
        Assert.Equal(3, s.TotalGames);
        Assert.Equal(2, s.Finished);
        Assert.Equal(1, s.Backlog);
        Assert.Equal(1, s.Favorites);
        Assert.Equal(9.5, s.AverageRating);
        Assert.Equal("Celeste", s.BestRatedGame!.Title);
        Assert.Equal(95, s.TotalPlaytimeMinutes);
    }

    [Fact]
    public void Stats_on_empty_library_have_no_invented_values()
    {
        using var env = new TestEnv();
        var s = env.Games.Stats();
        Assert.Equal(0, s.TotalGames);
        Assert.Null(s.AverageRating);
        Assert.Null(s.BestRatedGame);
        Assert.Null(s.FavoriteGenre);
    }

    [Fact]
    public void Profile_is_saved_locally()
    {
        using var env = new TestEnv();
        var repo = new ProfileRepository(env.Db);
        Assert.False(repo.Get().IsCreated);
        var gid = env.Games.Add(new Game { Title = "Hades" });
        repo.Save(new PlayerProfile { DisplayName = "André", Username = "@andre", Bio = "Gamer", AccentColor = "#ff5500", CurrentGameId = gid });
        var p = repo.Get();
        Assert.True(p.IsCreated);
        Assert.Equal("andre", p.Username);
        Assert.Equal("#FF5500", p.AccentColor);
        Assert.Equal(gid, p.CurrentGameId);

        env.Games.Remove(gid);
        Assert.Null(repo.Get().CurrentGameId);
        Assert.Throws<ArgumentException>(() => repo.Save(new PlayerProfile { DisplayName = "x", AccentColor = "red" }));
    }

    [Fact]
    public void Secrets_are_never_stored_in_plaintext()
    {
        using var env = new TestEnv();
        var settings = new SettingsRepository(env.Db);
        var secrets = new SecretStore(settings, new FakeProtector());
        secrets.Set(SettingKeys.RawgApiKey, "minha-chave");
        Assert.NotEqual("minha-chave", settings.Get(SettingKeys.RawgApiKey));
        Assert.Equal("minha-chave", secrets.Get(SettingKeys.RawgApiKey));
        settings.Set(SettingKeys.RawgApiKey, "lixo");
        Assert.Null(secrets.Get(SettingKeys.RawgApiKey));
        secrets.Set(SettingKeys.RawgApiKey, "");
        Assert.Null(settings.Get(SettingKeys.RawgApiKey));
    }

    [Fact]
    public void Large_library_performance()
    {
        using var env = new TestEnv();
        var sw = Stopwatch.StartNew();
        using (var c = env.Db.Open())
        using (var tx = c.BeginTransaction())
        {
            for (var i = 0; i < 5000; i++)
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO games (title, normalized_title, genres, added_at, updated_at, status) VALUES ($t, $n, 'RPG|Ação', $d, $d, $s);";
                cmd.Parameters.AddWithValue("$t", $"Jogo {i}");
                cmd.Parameters.AddWithValue("$n", $"jogo {i}");
                cmd.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$s", i % 7);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        var insert = sw.Elapsed;

        sw.Restart();
        var all = env.Games.All();
        var loadAll = sw.Elapsed;
        sw.Restart();
        var search = env.Games.Query(new GameQuery(Search: "Jogo 49"));
        var searchTime = sw.Elapsed;
        sw.Restart();
        var stats = env.Games.Stats();
        var statsTime = sw.Elapsed;

        Assert.Equal(5000, all.Count);
        Assert.Equal(5000, stats.TotalGames);
        Assert.Contains(search, g => g.Title == "Jogo 4999");
        // Limites folgados para máquinas de CI; em um PC comum fica bem abaixo disso.
        Assert.True(loadAll < TimeSpan.FromSeconds(2), $"Carregar 5000 jogos levou {loadAll.TotalMilliseconds} ms");
        Assert.True(searchTime < TimeSpan.FromMilliseconds(500), $"Busca levou {searchTime.TotalMilliseconds} ms");
        Assert.True(statsTime < TimeSpan.FromSeconds(2), $"Estatísticas levaram {statsTime.TotalMilliseconds} ms");
        Console.WriteLine($"5000 jogos: insert {insert.TotalMilliseconds:F0} ms, load {loadAll.TotalMilliseconds:F0} ms, search {searchTime.TotalMilliseconds:F0} ms, stats {statsTime.TotalMilliseconds:F0} ms");
    }
}
