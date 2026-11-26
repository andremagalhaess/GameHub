using System.Diagnostics;
using GameHub.Core.Data;
using GameHub.Core.Exchange;
using GameHub.Core.Journey;
using GameHub.Core.Models;
using GameHub.Core.Text;

namespace GameHub.Core.Tests;

public class ExchangeTests
{
    private sealed class Env : IDisposable
    {
        public TestEnv T { get; } = new();
        public ReviewRepository Reviews { get; }
        public MemoryRepository Memories { get; }
        public RankingRepository Rankings { get; }
        public CsvExchange X { get; }
        public GameRepository Games => T.Games;
        public Env()
        {
            Reviews = new ReviewRepository(T.Db);
            Memories = new MemoryRepository(T.Db);
            Rankings = new RankingRepository(T.Db, T.Games);
            X = new CsvExchange(T.Games, Reviews, Memories, Rankings);
        }
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Csv_roundtrip_with_quotes_newlines_and_formula_protection()
    {
        var rows = new[]
        {
            new[] { "Jogo", "Texto", "Nota" },
            new[] { "The Witcher 3; Wild Hunt", "Linha 1\nLinha \"2\"", "9.5" },
            new[] { "=cmd", " espaços ", "-1" },
        };
        var text = Csv.Write(rows);
        Assert.Contains("'=cmd", text);
        var back = Csv.Parse(text);
        Assert.Equal(3, back.Count);
        Assert.Equal(rows[1], back[1]);
        Assert.Equal("=cmd", back[2][0]);
        Assert.Equal(" espaços ", back[2][1]);
        Assert.Equal("-1", back[2][2]);
        Assert.Equal(',', Csv.DetectSeparator("Game,Rating,\"a;b\"\r\nx,1,2"));
        Assert.Equal(new[] { "a", "", "c" }, Csv.Parse("\uFEFFa,,c\n")[0]);
    }

    [Fact]
    public void Export_then_import_into_empty_library_keeps_everything()
    {
        var dir = new TempDir();
        using var _ = dir;
        string folder = dir.File("exp");
        using (var a = new Env())
        {
            var id = a.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, IsFavorite = true, PersonalRating = 9.5, StartedAt = "2024-01-02", FinishedAt = "2024-02-10", Genres = new() { "Roguelike", "Ação" }, Source = GameSources.Steam, ExecutablePath = @"C:\Games\Hades\Hades.exe", HltbMainMinutes = 1320 });
            var c = a.Games.Add(new Game { Title = "Celeste", Status = GameStatus.Playing });
            var start = new DateTimeOffset(2024, 1, 5, 20, 0, 0, TimeSpan.FromHours(-3));
            a.Games.RecordSession(id, start, start.AddMinutes(95));
            a.Reviews.Save(new GameReview { GameId = id, Review = "Obra-prima; \"perfeito\".", Pros = "Combate\nTrilha" });
            a.Memories.Add(new GameMemory { GameId = id, Kind = MemoryKind.Moment, Title = "Primeira fuga", HappenedAt = "2024-02-10" });
            var top = a.Rankings.All().First(r => r.MaxItems == 10);
            a.Rankings.AddGame(top.Id, c);
            a.Rankings.AddGame(top.Id, id);
            var custom = a.Rankings.Create("Chefões", null, null);
            a.Rankings.AddGame(custom, id);
            Assert.Equal(4, a.X.ExportAll(folder).Count);
        }

        using var b = new Env();
        var r1 = b.X.ImportGames(File.ReadAllText(Path.Combine(folder, "GameHub-jogos.csv")));
        Assert.Equal(2, r1.Added);
        var r2 = b.X.ImportReviews(File.ReadAllText(Path.Combine(folder, "GameHub-reviews.csv")));
        Assert.Equal(1, r2.Added);
        var r3 = b.X.ImportJourney(File.ReadAllText(Path.Combine(folder, "GameHub-jornada.csv")));
        Assert.Empty(r3.Problems);
        var r4 = b.X.ImportRankings(File.ReadAllText(Path.Combine(folder, "GameHub-rankings.csv")));
        Assert.Empty(r4.Problems);

        var h = b.Games.All().Single(g => g.Title == "Hades");
        Assert.Equal(GameStatus.Beaten, h.Status);
        Assert.True(h.IsFavorite);
        Assert.Equal(9.5, h.PersonalRating);
        Assert.Equal("2024-02-10", h.FinishedAt);
        Assert.Equal(new[] { "Roguelike", "Ação" }, h.Genres);
        Assert.Equal(@"C:\Games\Hades\Hades.exe", h.ExecutablePath);
        Assert.Equal(1320, h.HltbMainMinutes);
        Assert.Equal(95, h.PlaytimeMinutes); // sessão importada não soma duas vezes
        Assert.Single(b.Games.Sessions(h.Id));
        Assert.Equal("Obra-prima; \"perfeito\".", b.Reviews.Get(h.Id)!.Review);
        Assert.Equal("Combate\nTrilha", b.Reviews.Get(h.Id)!.Pros);
        Assert.Equal("Primeira fuga", b.Memories.ForGame(h.Id).Single().Title);
        var top10 = b.Rankings.All().First(r => r.MaxItems == 10);
        Assert.Equal(new[] { "Celeste", "Hades" }, b.Rankings.Entries(top10.Id).Select(e => e.Game.Title));
        Assert.Single(b.Rankings.All(), r => r.Name == "Chefões");

        // Importar de novo não duplica nada.
        b.X.ImportGames(File.ReadAllText(Path.Combine(folder, "GameHub-jogos.csv")));
        var again = b.X.ImportJourney(File.ReadAllText(Path.Combine(folder, "GameHub-jornada.csv")));
        b.X.ImportRankings(File.ReadAllText(Path.Combine(folder, "GameHub-rankings.csv")));
        Assert.Equal(0, again.Added);
        Assert.Equal(2, b.Games.Count());
        Assert.Single(b.Games.Sessions(h.Id));
        Assert.Single(b.Memories.All());
        Assert.Equal(95, b.Games.Get(h.Id)!.PlaytimeMinutes);
        Assert.Single(b.Rankings.All(), r => r.Name == "Chefões");
    }

    [Fact]
    public void Backloggd_style_csv_is_imported_with_star_scale_and_status_mapping()
    {
        using var e = new Env();
        e.Games.Add(new Game { Title = "Hollow Knight", PersonalRating = 6 });
        var csv = """
            Game,Platform,Status,Rating,Review,Started,Finished,Wishlist
            Hollow Knight,PC,Completed,4.5,"Lindo, difícil",2023-03-01,2023-04-20,
            Elden Ring,PC,Mastered,5,,,,
            Some Wishlist Game,PC,,,,,,true
            Outer Wilds,PC,Shelved,3.5,,,,
            Persona 5,PS4,Played,,,,,
            """;
        var r = e.X.ImportGames(csv, new GameImportOptions(DefaultSource: "Backloggd"));
        Assert.Equal(3, r.Added);
        Assert.Equal(1, r.Updated);
        Assert.Equal(1, r.Skipped); // wishlist
        var hk = e.Games.All().Single(g => g.Title == "Hollow Knight");
        Assert.Equal(9, hk.PersonalRating);
        Assert.Equal(GameStatus.Beaten, hk.Status);
        Assert.Equal("2023-04-20", hk.FinishedAt);
        Assert.Equal("Lindo, difícil", e.Reviews.Get(hk.Id)!.Review);
        Assert.Equal(GameStatus.Completed100, e.Games.All().Single(g => g.Title == "Elden Ring").Status);
        Assert.Equal(GameStatus.Paused, e.Games.All().Single(g => g.Title == "Outer Wilds").Status);
        var p5 = e.Games.All().Single(g => g.Title == "Persona 5");
        Assert.Equal(GameStatus.None, p5.Status);
        Assert.Equal("Backloggd", p5.Source);

        var export = Csv.Write(e.X.BackloggdStyleRows());
        Assert.Contains("Hollow Knight,PC,Completed,4.5", export.Replace(';', ','));
    }

    [Fact]
    public void Import_reports_bad_lines_and_requires_title_column()
    {
        using var e = new Env();
        Assert.Throws<InvalidDataException>(() => e.X.ImportGames("Foo;Bar\n1;2"));
        var r = e.X.ImportGames("Jogo;Nota\nA;11\n;5\nB;7,5");
        Assert.Equal(2, r.Added);
        Assert.Equal(1, r.Skipped);
        Assert.Single(r.Problems);
        Assert.Equal(7.5, e.Games.All().Single(g => g.Title == "B").PersonalRating);
        Assert.Null(e.Games.All().Single(g => g.Title == "A").PersonalRating);
        var rv = e.X.ImportReviews("Jogo;Review\nNão existe;oi");
        Assert.Single(rv.Problems);
    }

    [Fact]
    public void Large_library_journey_and_export_stay_fast()
    {
        using var e = new Env();
        var rnd = new Random(42);
        var genres = new[] { "Ação", "RPG", "Aventura", "Estratégia", "Indie", "Roguelike" };
        for (var i = 0; i < 3000; i++)
        {
            var started = new DateOnly(2015 + rnd.Next(10), 1 + rnd.Next(12), 1 + rnd.Next(28));
            var g = new Game
            {
                Title = $"Jogo {i}",
                Genres = new() { genres[i % genres.Length] },
                Status = (GameStatus)(i % 7),
                PersonalRating = i % 3 == 0 ? rnd.Next(0, 21) / 2.0 : null,
                StartedAt = i % 2 == 0 ? started.ToString("yyyy-MM-dd") : null,
                FinishedAt = i % 4 == 0 ? started.AddDays(rnd.Next(1, 90)).ToString("yyyy-MM-dd") : null,
                PlatformPlaytimeMinutes = rnd.Next(0, 6000),
            };
            e.Games.Add(g);
        }
        var journey = new JourneyService(e.Games, e.Reviews, e.Memories);
        var sw = Stopwatch.StartNew();
        journey.Dashboard();
        journey.Timeline(new JourneyService.TimelineFilter(Genre: "RPG"));
        journey.YearInReview(2020);
        journey.Calendar(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));
        var rows = e.X.GameRows().Count();
        sw.Stop();
        Assert.Equal(3001, rows);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Levou {sw.ElapsedMilliseconds} ms");

        var csv = Csv.Write(e.X.GameRows());
        using var fresh = new Env();
        sw.Restart();
        var r = fresh.X.ImportGames(csv);
        sw.Stop();
        Assert.Equal(3000, r.Added);
        Assert.True(sw.ElapsedMilliseconds < 30000, $"Importação levou {sw.ElapsedMilliseconds} ms");
    }
}
