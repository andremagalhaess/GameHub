using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

/// <summary>
/// Acesso ao arquivo SQLite local. Cria/migra o esquema usando PRAGMA user_version,
/// para que atualizações do aplicativo nunca apaguem dados do usuário.
/// </summary>
public sealed class Database
{
    public string FilePath { get; }
    private readonly string _connectionString;

    public Database(string filePath)
    {
        FilePath = filePath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>Aplica migrações pendentes. Seguro para chamar a cada inicialização.</summary>
    public void Migrate()
    {
        using var c = Open();
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"));
        for (var i = version; i < Migrations.Length; i++)
        {
            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Migrations[i];
                cmd.ExecuteNonQuery();
            }
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"PRAGMA user_version = {i + 1};";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public int SchemaVersion
    {
        get { using var c = Open(); return Convert.ToInt32(Scalar(c, "PRAGMA user_version;")); }
    }

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>Cada item é uma migração. Nunca altere uma migração já publicada; acrescente outra.</summary>
    private static readonly string[] Migrations =
    {
        // v1 — esquema inicial (Prioridade 1)
        """
        CREATE TABLE games (
            id                      INTEGER PRIMARY KEY AUTOINCREMENT,
            title                   TEXT NOT NULL,
            normalized_title        TEXT NOT NULL,
            alternative_names       TEXT,
            short_description       TEXT,
            description             TEXT,
            developer               TEXT,
            publisher               TEXT,
            genres                  TEXT,
            tags                    TEXT,
            platforms               TEXT,
            release_date            TEXT,
            age_rating              TEXT,
            website                 TEXT,
            trailer_url             TEXT,
            external_rating         REAL,
            external_rating_source  TEXT,
            cover_path              TEXT,
            background_path         TEXT,
            logo_path               TEXT,
            icon_path               TEXT,
            executable_path         TEXT,
            launch_arguments        TEXT,
            working_directory       TEXT,
            source                  TEXT NOT NULL DEFAULT 'Manual',
            source_id               TEXT,
            metadata_provider       TEXT,
            metadata_id             TEXT,
            status                  INTEGER NOT NULL DEFAULT 0,
            is_favorite             INTEGER NOT NULL DEFAULT 0,
            personal_rating         REAL,
            playtime_minutes        INTEGER NOT NULL DEFAULT 0,
            last_played_at          TEXT,
            added_at                TEXT NOT NULL,
            updated_at              TEXT NOT NULL
        );
        CREATE INDEX ix_games_normalized_title ON games(normalized_title);
        CREATE INDEX ix_games_exe ON games(executable_path COLLATE NOCASE);
        CREATE INDEX ix_games_last_played ON games(last_played_at);
        CREATE INDEX ix_games_added ON games(added_at);

        CREATE TABLE play_sessions (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            game_id     INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            started_at  TEXT NOT NULL,
            ended_at    TEXT NOT NULL,
            minutes     INTEGER NOT NULL
        );
        CREATE INDEX ix_sessions_game ON play_sessions(game_id);

        CREATE TABLE profile (
            id              INTEGER PRIMARY KEY CHECK (id = 1),
            display_name    TEXT NOT NULL DEFAULT '',
            username        TEXT NOT NULL DEFAULT '',
            bio             TEXT,
            avatar_path     TEXT,
            banner_path     TEXT,
            accent_color    TEXT NOT NULL DEFAULT '#7C5CFF',
            current_game_id INTEGER REFERENCES games(id) ON DELETE SET NULL
        );
        INSERT INTO profile (id) VALUES (1);

        CREATE TABLE settings (
            key   TEXT PRIMARY KEY,
            value TEXT
        );
        """,
    };
}
