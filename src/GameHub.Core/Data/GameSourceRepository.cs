using GameHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

/// <summary>Vínculos jogo ↔ loja/launcher (um jogo pode estar em várias plataformas).</summary>
public sealed class GameSourceRepository
{
    private readonly Database _db;
    public GameSourceRepository(Database db) => _db = db;

    public GameSourceLink? Get(string source, string sourceId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM game_sources WHERE source = @s AND source_id = @i;";
        cmd.Add("@s", source); cmd.Add("@i", sourceId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public List<GameSourceLink> ForGame(long gameId) => Select("SELECT * FROM game_sources WHERE game_id = @g ORDER BY source;", ("@g", gameId));
    public List<GameSourceLink> ForSource(string source) => Select("SELECT * FROM game_sources WHERE source = @s;", ("@s", source));

    public void Upsert(GameSourceLink l)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO game_sources (game_id, source, source_id, install_dir, executable_path, launch_uri, playtime_minutes, last_played_at, is_installed, detected_at)
            VALUES (@g, @s, @i, @d, @e, @u, @p, @lp, @inst, @now)
            ON CONFLICT(source, source_id) DO UPDATE SET game_id = excluded.game_id, install_dir = excluded.install_dir,
                executable_path = excluded.executable_path, launch_uri = excluded.launch_uri,
                playtime_minutes = COALESCE(excluded.playtime_minutes, game_sources.playtime_minutes),
                last_played_at = COALESCE(excluded.last_played_at, game_sources.last_played_at),
                is_installed = excluded.is_installed, detected_at = excluded.detected_at;
            """;
        cmd.Add("@g", l.GameId); cmd.Add("@s", l.Source); cmd.Add("@i", l.SourceId);
        cmd.Add("@d", l.InstallDir.ToDb()); cmd.Add("@e", l.ExecutablePath.ToDb()); cmd.Add("@u", l.LaunchUri.ToDb());
        cmd.Add("@p", l.PlaytimeMinutes.ToDb()); cmd.Add("@lp", l.LastPlayedAt.ToDb());
        cmd.Add("@inst", l.IsInstalled ? 1 : 0); cmd.Add("@now", DateTimeOffset.UtcNow.ToDb());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Desfaz um vínculo (a entrada da plataforma vira um jogo separado na próxima importação).</summary>
    public void Unlink(long linkId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM game_sources WHERE id = @id;";
        cmd.Add("@id", linkId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Move todos os vínculos de um jogo para outro (associar duas entradas como o mesmo jogo).</summary>
    public void MoveLinks(long fromGameId, long toGameId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE game_sources SET game_id = @to WHERE game_id = @from;";
        cmd.Add("@to", toGameId); cmd.Add("@from", fromGameId);
        cmd.ExecuteNonQuery();
    }

    public bool IsIgnored(string source, string sourceId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM ignored_sources WHERE source = @s AND source_id = @i;";
        cmd.Add("@s", source); cmd.Add("@i", sourceId);
        return cmd.ExecuteScalar() is not null;
    }

    public int IgnoredCount()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ignored_sources;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Permite reimportar jogos que foram removidos da biblioteca.</summary>
    public void ClearIgnored()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM ignored_sources;";
        cmd.ExecuteNonQuery();
    }

    private List<GameSourceLink> Select(string sql, params (string, object)[] args)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Add(k, v);
        using var r = cmd.ExecuteReader();
        var list = new List<GameSourceLink>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    private static GameSourceLink Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        GameId = r.GetInt64(r.GetOrdinal("game_id")),
        Source = r.Str("source") ?? "",
        SourceId = r.Str("source_id") ?? "",
        InstallDir = r.Str("install_dir"),
        ExecutablePath = r.Str("executable_path"),
        LaunchUri = r.Str("launch_uri"),
        PlaytimeMinutes = r.Long("playtime_minutes"),
        LastPlayedAt = r.Date("last_played_at"),
        IsInstalled = r.Long("is_installed") == 1,
    };
}
