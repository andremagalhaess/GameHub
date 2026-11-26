using GameHub.Core.Data;
using GameHub.Core.Models;

namespace GameHub.Core.Tests;

public class ReviewTests
{
    [Fact]
    public void Review_roundtrip_edit_and_delete_when_empty()
    {
        using var env = new TestEnv();
        var reviews = new ReviewRepository(env.Db);
        var id = env.Games.Add(new Game { Title = "Hades" });
        Assert.Null(reviews.Get(id));

        reviews.Save(new GameReview { GameId = id, Review = "Obra-prima.", Pros = "Combate", Cons = "Nenhum", Moments = "Final verdadeiro" });
        var r = reviews.Get(id)!;
        Assert.Equal("Obra-prima.", r.Review);
        Assert.Equal("Final verdadeiro", r.Moments);

        r.Review = "Obra-prima absoluta.";
        reviews.Save(r);
        Assert.Equal("Obra-prima absoluta.", reviews.Get(id)!.Review);
        Assert.Single(reviews.All());

        reviews.Save(new GameReview { GameId = id });
        Assert.Null(reviews.Get(id));

        reviews.Save(new GameReview { GameId = id, Review = "x" });
        env.Games.Remove(id);
        Assert.Empty(reviews.All()); // apagada junto com o jogo
    }

    [Fact]
    public void Ratings_dates_and_filters_persist()
    {
        using var env = new TestEnv();
        var a = env.Games.Add(new Game { Title = "Hades", PersonalRating = 9.5, Genres = new() { "Roguelike", "Ação" }, StartedAt = "2024-01-02", FinishedAt = "2024-02-10", Status = GameStatus.Beaten, PlaytimeMinutes = 3000, ReleaseDate = "2020-09-17" });
        env.Games.Add(new Game { Title = "Celeste", PersonalRating = 7, Genres = new() { "Plataforma" }, PlatformPlaytimeMinutes = 600 });
        env.Games.Add(new Game { Title = "Sem nota", Genres = new() { "Ação RPG" } });

        var h = env.Games.Get(a)!;
        Assert.Equal("2024-02-10", h.FinishedAt);
        Assert.Equal(new[] { "Hades" }, env.Games.Query(new GameQuery(Genre: "Ação")).Select(g => g.Title)); // não confunde com "Ação RPG"
        Assert.Equal(new[] { "Hades" }, env.Games.Query(new GameQuery(MinRating: 8)).Select(g => g.Title));
        Assert.Equal(2, env.Games.Query(new GameQuery(RatedOnly: true)).Count);
        Assert.Equal(new[] { "Hades", "Celeste" }, env.Games.Query(new GameQuery(Sort: GameSort.Rating, RatedOnly: true)).Select(g => g.Title));
        Assert.Equal(new[] { "Celeste" }, env.Games.Query(new GameQuery(MinPlaytimeMinutes: 300, MaxPlaytimeMinutes: 1000)).Select(g => g.Title));
        Assert.Equal(new[] { "Hades" }, env.Games.Query(new GameQuery(ReleaseYear: 2020)).Select(g => g.Title));
        Assert.Contains("Plataforma", env.Games.DistinctGenres());

        var latest = new ReviewRepository(env.Db).Latest(env.Games, 5);
        Assert.Equal(2, latest.Count);
    }
}
