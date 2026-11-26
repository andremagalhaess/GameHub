using GameHub.Core.Data;
using GameHub.Core.Journey;
using GameHub.Core.Models;

namespace GameHub.Core.Tests;

public class JourneyTests
{
    private static (TestEnv env, ReviewRepository reviews, MemoryRepository memories, JourneyService journey) Setup()
    {
        var env = new TestEnv();
        var reviews = new ReviewRepository(env.Db);
        var memories = new MemoryRepository(env.Db);
        return (env, reviews, memories, new JourneyService(env.Games, reviews, memories));
    }

    private static DateTimeOffset Local(int y, int m, int d, int h = 20) =>
        new(new DateTime(y, m, d, h, 0, 0), TimeZoneInfo.Local.GetUtcOffset(new DateTime(y, m, d, h, 0, 0)));

    [Fact]
    public void Memories_crud_and_validation()
    {
        var (env, _, memories, _) = Setup();
        using var _env = env;
        var g = env.Games.Add(new Game { Title = "Hades" });
        Assert.Throws<ArgumentException>(() => memories.Add(new GameMemory { GameId = g }));

        var id = memories.Add(new GameMemory { GameId = g, Kind = MemoryKind.Moment, Title = "Primeira fuga", Text = "Finalmente!", HappenedAt = "2024-02-10" });
        memories.Add(new GameMemory { GameId = g, Kind = MemoryKind.Screenshot, ImagePath = @"C:\shots\a.png" });
        Assert.Equal(2, memories.ForGame(g).Count);

        var m = memories.ForGame(g).Single(x => x.Id == id);
        Assert.Equal(MemoryKind.Moment, m.Kind);
        m.Text = "Finalmente saí do Tártaro.";
        memories.Update(m);
        Assert.Equal("Finalmente saí do Tártaro.", memories.ForGame(g).Single(x => x.Id == id).Text);

        memories.Delete(id);
        Assert.Single(memories.All());
        env.Games.Remove(g);
        Assert.Empty(memories.All()); // apagadas junto com o jogo
    }

    [Fact]
    public void Rankings_seeded_add_limit_reorder_and_custom()
    {
        using var env = new TestEnv();
        var rankings = new RankingRepository(env.Db, env.Games);
        var all = rankings.All();
        Assert.Equal(7, all.Count);
        Assert.All(all, r => Assert.True(r.IsBuiltIn));
        var top10 = all.Single(r => r.Name == "Top 10 jogos favoritos");
        Assert.Equal(10, top10.MaxItems);

        var ids = Enumerable.Range(1, 11).Select(i => env.Games.Add(new Game { Title = $"Jogo {i}" })).ToList();
        for (var i = 0; i < 10; i++) Assert.True(rankings.AddGame(top10.Id, ids[i]));
        Assert.False(rankings.AddGame(top10.Id, ids[10]));   // limite
        Assert.False(rankings.AddGame(top10.Id, ids[0]));    // repetido

        rankings.SetOrder(top10.Id, new[] { ids[2], ids[0], ids[1] }.Concat(ids.Skip(3).Take(7)).ToList());
        var e = rankings.Entries(top10.Id);
        Assert.Equal(new[] { "Jogo 3", "Jogo 1", "Jogo 2" }, e.Take(3).Select(x => x.Game.Title));
        Assert.Equal(Enumerable.Range(1, 10), e.Select(x => x.Position));

        rankings.RemoveGame(top10.Id, ids[0]);
        Assert.Equal(new[] { "Jogo 3", "Jogo 2" }, rankings.Entries(top10.Id).Take(2).Select(x => x.Game.Title));

        env.Games.Remove(ids[2]);
        Assert.Equal("Jogo 2", rankings.Entries(top10.Id)[0].Game.Title); // jogo removido sai do ranking

        Assert.Throws<ArgumentException>(() => rankings.Create(" "));
        var custom = rankings.Create("Melhores chefões", "Os mais difíceis");
        var r = rankings.Get(custom)!;
        Assert.False(r.IsBuiltIn);
        Assert.Equal(8, rankings.All().Count);
        r.Year = 2024;
        rankings.Update(r);
        Assert.Equal(2024, rankings.Get(custom)!.Year);
        rankings.Delete(custom);
        Assert.Null(rankings.Get(custom));
    }

    [Fact]
    public void Dashboard_counts_only_registered_data()
    {
        var (env, _, _, journey) = Setup();
        using var _env = env;
        env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, FinishedAt = "2023-05-01", Genres = new() { "Roguelike" }, Source = GameSources.Steam, PlatformPlaytimeMinutes = 3000 });
        env.Games.Add(new Game { Title = "Celeste", Status = GameStatus.Completed100, FinishedAt = "2024-01-10", Genres = new() { "Plataforma" }, Source = GameSources.Steam });
        env.Games.Add(new Game { Title = "Zerado sem data", Status = GameStatus.Beaten });
        env.Games.Add(new Game { Title = "Só cadastrado", Genres = new() { "Estratégia" } });

        var d = journey.Dashboard();
        Assert.Equal(new[] { "2023", "2024" }, d.FinishedPerYear.Select(c => c.Label));
        Assert.DoesNotContain(d.TopGenres, c => c.Label == "Estratégia"); // nunca jogado não entra
        Assert.Equal(GameSources.Steam, d.TopPlatform);
        Assert.Equal(2, d.StatusBreakdown.Single(c => c.Label == "Zerado").Value);
    }

    [Fact]
    public void Timeline_dates_and_filters()
    {
        var (env, reviews, _, journey) = Setup();
        using var _env = env;
        var a = env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, StartedAt = "2024-01-02", FinishedAt = "2024-02-10", PersonalRating = 9.5, Genres = new() { "Roguelike" } });
        var b = env.Games.Add(new Game { Title = "Celeste", Status = GameStatus.Beaten, FinishedAt = "2023-07-01", PersonalRating = 7 });
        var c = env.Games.Add(new Game { Title = "Outer Wilds" });
        env.Games.RecordSession(c, Local(2022, 3, 5), Local(2022, 3, 5).AddMinutes(90));
        env.Games.Add(new Game { Title = "Jogando sem data", Status = GameStatus.Playing });
        env.Games.Add(new Game { Title = "Backlog", Status = GameStatus.Backlog });
        reviews.Save(new GameReview { GameId = a, Review = "Obra-prima." });

        var t = journey.Timeline();
        Assert.Equal(new[] { "Hades", "Celeste", "Outer Wilds", "Jogando sem data" }, t.Select(x => x.Game.Title));
        Assert.Equal("Início", t[0].DateSource);
        Assert.Equal("Obra-prima.", t[0].ReviewExcerpt);
        Assert.Equal("Conclusão", t[1].DateSource);
        Assert.Equal(new DateOnly(2022, 3, 5), t[2].Date);
        Assert.Null(t[3].Date);

        var y2024 = journey.Timeline(new(From: new DateOnly(2024, 1, 1), To: new DateOnly(2024, 12, 31)));
        Assert.Equal(new[] { "Hades" }, y2024.Select(x => x.Game.Title));
        var feb = journey.Timeline(new(From: new DateOnly(2024, 2, 1), To: new DateOnly(2024, 2, 29)));
        Assert.Equal(new[] { "Hades" }, feb.Select(x => x.Game.Title)); // terminou em fevereiro
        Assert.Equal(new[] { "Hades" }, journey.Timeline(new(MinRating: 8)).Select(x => x.Game.Title));
        Assert.Equal(new[] { "Hades" }, journey.Timeline(new(Genre: "roguelike")).Select(x => x.Game.Title));
        Assert.Equal(3, journey.Timeline(new(IncludeUndated: false)).Count);
        Assert.Equal(new[] { "Outer Wilds" }, journey.Timeline(new(MinMinutes: 60, MaxMinutes: 120)).Select(x => x.Game.Title));
        Assert.DoesNotContain(journey.Timeline(new(Status: GameStatus.Beaten)), x => x.Game.Id == c);
        _ = b;
    }

    [Fact]
    public void Year_in_review_and_years_with_activity()
    {
        var (env, reviews, _, journey) = Setup();
        using var _env = env;
        var a = env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, StartedAt = "2024-01-02", FinishedAt = "2024-02-10", PersonalRating = 9.5, Genres = new() { "Roguelike" }, IsFavorite = true, Source = GameSources.Steam });
        env.Games.Add(new Game { Title = "Ruim", Status = GameStatus.Abandoned, StartedAt = "2024-06-01", PersonalRating = 3, Genres = new() { "Roguelike" }, Source = GameSources.Manual });
        var o = env.Games.Add(new Game { Title = "Antigo", Status = GameStatus.Beaten, FinishedAt = "2021-01-01", PersonalRating = 10 });
        env.Games.RecordSession(a, Local(2024, 3, 1), Local(2024, 3, 1).AddMinutes(120));
        env.Games.RecordSession(a, Local(2023, 12, 31), Local(2023, 12, 31).AddMinutes(30));
        reviews.Save(new GameReview { GameId = a, Review = "Obra-prima." });

        var y = journey.YearInReview(2024);
        Assert.Equal(2, y.Started.Count);
        Assert.Equal(new[] { "Hades" }, y.Finished.Select(g => g.Title));
        Assert.Equal(120, y.TrackedMinutes);
        Assert.Equal("Hades", y.Best!.Title);
        Assert.Equal("Ruim", y.Worst!.Title);
        Assert.DoesNotContain(y.Played, g => g.Id == o);
        Assert.Equal("Roguelike", y.TopGenre);
        Assert.Single(y.Favorites);
        Assert.Equal(1, y.FinishedPerMonth[1].Value); // fevereiro
        Assert.Equal(12, y.FinishedPerMonth.Count);

        Assert.True(journey.YearInReview(2010).IsEmpty);
        var years = journey.YearsWithActivity();
        Assert.Contains(2024, years);
        Assert.Contains(2023, years);
        Assert.Contains(2021, years);
        Assert.Equal(years.OrderByDescending(x => x), years);
    }

    [Fact]
    public void Calendar_lists_starts_finishes_sessions_and_memories()
    {
        var (env, _, memories, journey) = Setup();
        using var _env = env;
        var a = env.Games.Add(new Game { Title = "Hades", Status = GameStatus.Beaten, StartedAt = "2024-02-01", FinishedAt = "2024-02-10" });
        env.Games.RecordSession(a, Local(2024, 2, 5), Local(2024, 2, 5).AddMinutes(40));
        env.Games.RecordSession(a, Local(2024, 2, 5, 22), Local(2024, 2, 5, 22).AddMinutes(20));
        env.Games.RecordSession(a, Local(2024, 3, 5), Local(2024, 3, 5).AddMinutes(20));
        memories.Add(new GameMemory { GameId = a, Title = "Chefe final", HappenedAt = "2024-02-10" });

        var ev = journey.Calendar(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29));
        Assert.Equal(4, ev.Count);
        Assert.Equal(CalendarEventKind.Started, ev[0].Kind);
        var session = ev.Single(e => e.Kind == CalendarEventKind.Session);
        Assert.Equal(new DateOnly(2024, 2, 5), session.Date);
        Assert.Equal(60, session.Minutes); // duas sessões no mesmo dia somadas
        Assert.Equal(2, ev.Count(e => e.Date == new DateOnly(2024, 2, 10)));
    }
}
