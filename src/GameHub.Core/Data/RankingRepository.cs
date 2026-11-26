using GameHub.Core.Models;

namespace GameHub.Core.Data;

public sealed class RankingRepository
{
    private readonly Database _db;
    private readonly GameRepository _games;

    public RankingRepository(Database db, GameRepository games)
    {
        _db = db;
        _games = games;
    }

    public List<Ranking> All()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM rankings ORDER BY sort_order, id;";
        using var r = cmd.ExecuteReader();
        var list = new List<Ranking>();
        while (r.Read())
            list.Add(new Ranking
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                Name = r.Str("name") ?? "",
                Description = r.Str("description"),
                Year = (int?)r.Long("year"),
                MaxItems = (int?)r.Long("max_items"),
                IsBuiltIn = r.Long("is_builtin") == 1,
                SortOrder = (int)(r.Long("sort_order") ?? 0),
            });
        return list;
    }

    public Ranking? Get(long id) => All().FirstOrDefault(r => r.Id == id);

    public long Create(string name, string? description = null, int? maxItems = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Dê um nome ao ranking.");
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rankings (name, description, max_items, is_builtin, sort_order, created_at)
            VALUES (@n, @d, @m, 0, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM rankings), @c);
            SELECT last_insert_rowid();
            """;
        cmd.Add("@n", name.Trim()); cmd.Add("@d", description.ToDb()); cmd.Add("@m", maxItems.HasValue ? maxItems.Value : DBNull.Value);
        cmd.Add("@c", DateTimeOffset.UtcNow.ToDb());
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(Ranking r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ArgumentException("Dê um nome ao ranking.");
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE rankings SET name=@n, description=@d, year=@y, max_items=@m WHERE id=@id;";
        cmd.Add("@n", r.Name.Trim()); cmd.Add("@d", r.Description.ToDb());
        cmd.Add("@y", r.Year.HasValue ? r.Year.Value : DBNull.Value);
        cmd.Add("@m", r.MaxItems.HasValue ? r.MaxItems.Value : DBNull.Value);
        cmd.Add("@id", r.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM rankings WHERE id = @id;";
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<RankingEntry> Entries(long rankingId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT game_id, position, note FROM ranking_items WHERE ranking_id = @r ORDER BY position;";
        cmd.Add("@r", rankingId);
        using var rd = cmd.ExecuteReader();
        var raw = new List<(long, string?)>();
        while (rd.Read()) raw.Add((rd.GetInt64(0), rd.IsDBNull(2) ? null : rd.GetString(2)));
        var list = new List<RankingEntry>();
        foreach (var (gameId, note) in raw)
            if (_games.Get(gameId) is { } g) list.Add(new RankingEntry { Position = list.Count + 1, Game = g, Note = note });
        return list;
    }

    /// <summary>Adiciona no fim. Retorna false se já estiver no ranking ou se o limite foi atingido.</summary>
    public bool AddGame(long rankingId, long gameId)
    {
        var entries = Entries(rankingId);
        if (entries.Any(e => e.Game.Id == gameId)) return false;
        if (Get(rankingId)?.MaxItems is { } max && entries.Count >= max) return false;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO ranking_items (ranking_id, game_id, position) VALUES (@r, @g, @p);";
        cmd.Add("@r", rankingId); cmd.Add("@g", gameId); cmd.Add("@p", entries.Count + 1);
        cmd.ExecuteNonQuery();
        return true;
    }

    public void RemoveGame(long rankingId, long gameId)
    {
        var order = Entries(rankingId).Select(e => e.Game.Id).Where(id => id != gameId).ToList();
        SetOrder(rankingId, order);
    }

    /// <summary>Regrava a ordem inteira (usado depois de arrastar e soltar).</summary>
    public void SetOrder(long rankingId, IReadOnlyList<long> gameIds)
    {
        var notes = Entries(rankingId).ToDictionary(e => e.Game.Id, e => e.Note);
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM ranking_items WHERE ranking_id = @r;";
            del.Add("@r", rankingId);
            del.ExecuteNonQuery();
        }
        var pos = 1;
        foreach (var id in gameIds.Distinct())
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO ranking_items (ranking_id, game_id, position, note) VALUES (@r, @g, @p, @n);";
            ins.Add("@r", rankingId); ins.Add("@g", id); ins.Add("@p", pos++); ins.Add("@n", notes.GetValueOrDefault(id).ToDb());
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
