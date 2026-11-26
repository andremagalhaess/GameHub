using GameHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

public sealed class MemoryRepository
{
    private readonly Database _db;
    public MemoryRepository(Database db) => _db = db;

    public long Add(GameMemory m)
    {
        Validate(m);
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memories (game_id, kind, title, text, image_path, happened_at, created_at)
            VALUES (@g, @k, @t, @x, @i, @h, @c); SELECT last_insert_rowid();
            """;
        Bind(cmd, m);
        m.Id = (long)cmd.ExecuteScalar()!;
        return m.Id;
    }

    public void Update(GameMemory m)
    {
        Validate(m);
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE memories SET game_id=@g, kind=@k, title=@t, text=@x, image_path=@i, happened_at=@h WHERE id=@id;";
        Bind(cmd, m);
        cmd.Add("@id", m.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE id = @id;";
        cmd.Add("@id", id);
        cmd.ExecuteNonQuery();
    }

    public List<GameMemory> ForGame(long gameId) =>
        Select("SELECT * FROM memories WHERE game_id = @g ORDER BY COALESCE(happened_at, created_at) DESC, id DESC;", ("@g", gameId));

    public List<GameMemory> All() => Select("SELECT * FROM memories ORDER BY COALESCE(happened_at, created_at) DESC, id DESC;");

    private static void Validate(GameMemory m)
    {
        if (string.IsNullOrWhiteSpace(m.Title) && string.IsNullOrWhiteSpace(m.Text) && string.IsNullOrWhiteSpace(m.ImagePath))
            throw new ArgumentException("A memória precisa de um título, texto ou imagem.");
    }

    private static void Bind(SqliteCommand cmd, GameMemory m)
    {
        cmd.Add("@g", m.GameId);
        cmd.Add("@k", (int)m.Kind);
        cmd.Add("@t", m.Title.ToDb());
        cmd.Add("@x", m.Text.ToDb());
        cmd.Add("@i", m.ImagePath.ToDb());
        cmd.Add("@h", m.HappenedAt.ToDb());
        cmd.Add("@c", m.CreatedAt.ToDb());
    }

    private List<GameMemory> Select(string sql, params (string, object)[] args)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Add(k, v);
        using var r = cmd.ExecuteReader();
        var list = new List<GameMemory>();
        while (r.Read())
            list.Add(new GameMemory
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                GameId = r.GetInt64(r.GetOrdinal("game_id")),
                Kind = (MemoryKind)(r.Long("kind") ?? 0),
                Title = r.Str("title"),
                Text = r.Str("text"),
                ImagePath = r.Str("image_path"),
                HappenedAt = r.Str("happened_at"),
                CreatedAt = r.Date("created_at") ?? DateTimeOffset.UtcNow,
            });
        return list;
    }
}
