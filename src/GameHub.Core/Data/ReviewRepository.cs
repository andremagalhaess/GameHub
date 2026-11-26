using GameHub.Core.Models;

namespace GameHub.Core.Data;

public sealed class ReviewRepository
{
    private readonly Database _db;
    public ReviewRepository(Database db) => _db = db;

    public GameReview? Get(long gameId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM reviews WHERE game_id = @g;";
        cmd.Add("@g", gameId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>Salva (ou apaga, se vazia) a review. A nota é gravada no jogo.</summary>
    public void Save(GameReview review)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        if (review.IsEmpty)
        {
            cmd.CommandText = "DELETE FROM reviews WHERE game_id = @g;";
            cmd.Add("@g", review.GameId);
            cmd.ExecuteNonQuery();
            return;
        }
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = """
            INSERT INTO reviews (game_id, review, pros, cons, comments, moments, created_at, updated_at)
            VALUES (@g, @r, @p, @c, @co, @m, @now, @now)
            ON CONFLICT(game_id) DO UPDATE SET review = excluded.review, pros = excluded.pros, cons = excluded.cons,
                comments = excluded.comments, moments = excluded.moments, updated_at = excluded.updated_at;
            """;
        cmd.Add("@g", review.GameId);
        cmd.Add("@r", review.Review.ToDb());
        cmd.Add("@p", review.Pros.ToDb());
        cmd.Add("@c", review.Cons.ToDb());
        cmd.Add("@co", review.Comments.ToDb());
        cmd.Add("@m", review.Moments.ToDb());
        cmd.Add("@now", now.ToDb());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Jogos avaliados mais recentemente (nota e/ou review), para a Home.</summary>
    public List<(Game Game, GameReview? Review)> Latest(GameRepository games, int limit)
    {
        var rated = games.Query(new GameQuery(RatedOnly: true));
        var reviews = All().ToDictionary(r => r.GameId);
        return rated
            .Select(g => (Game: g, Review: reviews.GetValueOrDefault(g.Id)))
            .Concat(reviews.Values.Where(r => rated.All(g => g.Id != r.GameId))
                .Select(r => (Game: games.Get(r.GameId)!, Review: (GameReview?)r)).Where(x => x.Game is not null))
            .OrderByDescending(x => x.Review?.UpdatedAt ?? x.Game.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    public List<GameReview> All()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM reviews ORDER BY updated_at DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<GameReview>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    private static GameReview Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        GameId = r.GetInt64(r.GetOrdinal("game_id")),
        Review = r.Str("review"),
        Pros = r.Str("pros"),
        Cons = r.Str("cons"),
        Comments = r.Str("comments"),
        Moments = r.Str("moments"),
        CreatedAt = r.Date("created_at") ?? DateTimeOffset.UtcNow,
        UpdatedAt = r.Date("updated_at") ?? DateTimeOffset.UtcNow,
    };
}
