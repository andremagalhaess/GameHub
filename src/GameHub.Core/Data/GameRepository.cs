using GameHub.Core.Models;
using GameHub.Core.Text;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

public enum GameSort { Title, RecentlyAdded, RecentlyPlayed, Playtime, ReleaseDate, Rating }

public sealed record GameQuery(
    string? Search = null,
    GameStatus? Status = null,
    bool FavoritesOnly = false,
    GameSort Sort = GameSort.Title,
    int? Limit = null,
    string? Genre = null,
    string? Source = null,
    double? MinRating = null,
    long? MinPlaytimeMinutes = null,
    long? MaxPlaytimeMinutes = null,
    int? ReleaseYear = null,
    bool RatedOnly = false);

/// <summary>Resultado da verificação de duplicados antes de adicionar um jogo.</summary>
public sealed record DuplicateMatch(Game Existing, string Reason);

public sealed class GameRepository
{
    private readonly Database _db;
    public GameRepository(Database db) => _db = db;

    public long Add(Game g)
    {
        g.AddedAt = g.UpdatedAt = DateTimeOffset.UtcNow;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"INSERT INTO games ({Columns}) VALUES ({Params}); SELECT last_insert_rowid();";
        Bind(cmd, g);
        g.Id = (long)cmd.ExecuteScalar()!;
        return g.Id;
    }

    public void Update(Game g)
    {
        if (g.Id <= 0) throw new ArgumentException("Jogo sem Id.", nameof(g));
        g.UpdatedAt = DateTimeOffset.UtcNow;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        var sets = string.Join(", ", ColumnList.Select(col => $"{col} = @{col}"));
        cmd.CommandText = $"UPDATE games SET {sets} WHERE id = @id;";
        Bind(cmd, g);
        cmd.Add("@id", g.Id);
        if (cmd.ExecuteNonQuery() == 0) throw new KeyNotFoundException($"Jogo {g.Id} não existe.");
    }

    /// <summary>
    /// Remove o jogo da biblioteca. NÃO desinstala nem apaga arquivos do jogo.
    /// As plataformas vinculadas entram na lista de ignorados para não voltarem na próxima importação.
    /// </summary>
    public bool Remove(long id)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using (var ign = c.CreateCommand())
        {
            ign.Transaction = tx;
            ign.CommandText = """
                INSERT OR IGNORE INTO ignored_sources (source, source_id)
                SELECT source, source_id FROM game_sources WHERE game_id = @id;
                """;
            ign.Add("@id", id);
            ign.ExecuteNonQuery();
        }
        int n;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM games WHERE id = @id;";
            cmd.Add("@id", id);
            n = cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return n > 0;
    }

    /// <summary>Gêneros distintos da biblioteca (para filtros).</summary>
    public List<string> DistinctGenres() =>
        All().SelectMany(g => g.Genres).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Plataformas/lojas distintas (origem principal + vínculos).</summary>
    public List<string> DistinctSources()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT source FROM games UNION SELECT source FROM game_sources ORDER BY 1;";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public Game? Get(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM games WHERE id = @id;";
        cmd.Add("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public List<Game> Query(GameQuery q)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var normalized = TitleNormalizer.Normalize(q.Search);
            where.Add("(title LIKE @s ESCAPE '\\' OR developer LIKE @s ESCAPE '\\' OR genres LIKE @s ESCAPE '\\'"
                + (normalized.Length > 0 ? " OR normalized_title LIKE @ns ESCAPE '\\'" : "") + ")");
            cmd.Add("@s", "%" + EscapeLike(q.Search.Trim()) + "%");
            if (normalized.Length > 0) cmd.Add("@ns", "%" + EscapeLike(normalized) + "%");
        }
        if (q.Status is { } st) { where.Add("status = @st"); cmd.Add("@st", (int)st); }
        if (q.FavoritesOnly) where.Add("is_favorite = 1");
        if (!string.IsNullOrWhiteSpace(q.Genre))
        {
            where.Add("('|' || genres || '|') LIKE @genre ESCAPE '\\'");
            cmd.Add("@genre", "%|" + EscapeLike(q.Genre.Trim()) + "|%");
        }
        if (!string.IsNullOrWhiteSpace(q.Source))
        {
            where.Add("(source = @src OR EXISTS (SELECT 1 FROM game_sources gs WHERE gs.game_id = games.id AND gs.source = @src))");
            cmd.Add("@src", q.Source);
        }
        if (q.MinRating is { } mr) { where.Add("personal_rating >= @mr"); cmd.Add("@mr", mr); }
        if (q.RatedOnly) where.Add("personal_rating IS NOT NULL");
        if (q.MinPlaytimeMinutes is { } minp) { where.Add("MAX(playtime_minutes, platform_playtime_minutes) >= @minp"); cmd.Add("@minp", minp); }
        if (q.MaxPlaytimeMinutes is { } maxp) { where.Add("MAX(playtime_minutes, platform_playtime_minutes) <= @maxp"); cmd.Add("@maxp", maxp); }
        if (q.ReleaseYear is { } ry) { where.Add("substr(release_date, 1, 4) = @ry"); cmd.Add("@ry", ry.ToString()); }
        var order = q.Sort switch
        {
            GameSort.RecentlyAdded => "added_at DESC",
            GameSort.RecentlyPlayed => "last_played_at IS NULL, last_played_at DESC",
            GameSort.Playtime => "MAX(playtime_minutes, platform_playtime_minutes) DESC",
            GameSort.Rating => "personal_rating IS NULL, personal_rating DESC",
            GameSort.ReleaseDate => "release_date IS NULL, release_date DESC",
            _ => "normalized_title COLLATE NOCASE ASC",
        };
        cmd.CommandText = "SELECT * FROM games"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + $" ORDER BY {order}, id"
            + (q.Limit is { } l ? $" LIMIT {Math.Max(0, l)}" : "")
            + ";";
        using var r = cmd.ExecuteReader();
        var list = new List<Game>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public List<Game> All() => Query(new GameQuery());

    public int Count()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Procura jogos que provavelmente são o mesmo: mesmo executável, mesma origem/ID,
    /// mesmo ID de metadados ou mesmo título normalizado.
    /// </summary>
    public List<DuplicateMatch> FindDuplicates(Game candidate)
    {
        var result = new List<DuplicateMatch>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM games WHERE id <> @id AND (
                (@exe IS NOT NULL AND executable_path = @exe COLLATE NOCASE)
             OR (@src_id IS NOT NULL AND source = @src AND source_id = @src_id)
             OR (@meta_id IS NOT NULL AND metadata_provider = @meta_p AND metadata_id = @meta_id)
             OR (normalized_title = @nt AND @nt <> ''));
            """;
        cmd.Add("@id", candidate.Id);
        cmd.Add("@exe", candidate.ExecutablePath is { } e ? NormalizePath(e) : null);
        cmd.Add("@src", candidate.Source);
        cmd.Add("@src_id", candidate.SourceId.ToDb());
        cmd.Add("@meta_p", candidate.MetadataProvider.ToDb());
        cmd.Add("@meta_id", candidate.MetadataId.ToDb());
        cmd.Add("@nt", TitleNormalizer.Normalize(candidate.Title));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var g = Read(r);
            string reason;
            if (candidate.ExecutablePath is { } exe && string.Equals(g.ExecutablePath, NormalizePath(exe), StringComparison.OrdinalIgnoreCase))
                reason = "Mesmo executável";
            else if (candidate.SourceId is not null && g.Source == candidate.Source && g.SourceId == candidate.SourceId)
                reason = $"Mesmo jogo da plataforma {g.Source}";
            else if (candidate.MetadataId is not null && g.MetadataProvider == candidate.MetadataProvider && g.MetadataId == candidate.MetadataId)
                reason = "Mesmo jogo nos metadados online";
            else
                reason = "Mesmo nome";
            result.Add(new DuplicateMatch(g, reason));
        }
        return result;
    }

    public void SetFavorite(long id, bool favorite) => Exec("UPDATE games SET is_favorite = @v, updated_at = @u WHERE id = @id;", id, favorite ? 1 : 0);
    public void SetStatus(long id, GameStatus status) => Exec("UPDATE games SET status = @v, updated_at = @u WHERE id = @id;", id, (int)status);

    /// <summary>Registra uma sessão de jogo medida e soma ao tempo total.</summary>
    public void RecordSession(long gameId, DateTimeOffset start, DateTimeOffset end)
    {
        var minutes = (long)Math.Floor((end - start).TotalMinutes);
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO play_sessions (game_id, started_at, ended_at, minutes) VALUES (@g, @s, @e, @m);";
            cmd.Add("@g", gameId); cmd.Add("@s", start.ToDb()); cmd.Add("@e", end.ToDb()); cmd.Add("@m", Math.Max(0, minutes));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE games SET playtime_minutes = playtime_minutes + @m, updated_at = @u WHERE id = @g;";
            cmd.Add("@g", gameId); cmd.Add("@m", Math.Max(0, minutes)); cmd.Add("@u", DateTimeOffset.UtcNow.ToDb());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void MarkLaunched(long gameId, DateTimeOffset when) =>
        Exec("UPDATE games SET last_played_at = @v, updated_at = @u WHERE id = @id;", gameId, when.ToDb());

    public List<PlaySession> Sessions(long gameId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM play_sessions WHERE game_id = @g ORDER BY started_at DESC;";
        cmd.Add("@g", gameId);
        using var r = cmd.ExecuteReader();
        var list = new List<PlaySession>();
        while (r.Read())
            list.Add(new PlaySession
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                GameId = gameId,
                StartedAt = r.Date("started_at")!.Value,
                EndedAt = r.Date("ended_at")!.Value,
                Minutes = r.GetInt64(r.GetOrdinal("minutes")),
            });
        return list;
    }

    /// <summary>Calcula estatísticas a partir dos dados reais do banco.</summary>
    public LibraryStats Stats()
    {
        var games = All();
        var rated = games.Where(g => g.PersonalRating.HasValue).ToList();
        var favoriteGenre = games
            .Where(g => g.Status is not GameStatus.None and not GameStatus.Backlog || g.TotalPlaytimeMinutes > 0)
            .SelectMany(g => g.Genres)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key)
            .Select(gr => gr.Key)
            .FirstOrDefault();
        return new LibraryStats
        {
            TotalGames = games.Count,
            Beaten = games.Count(g => g.Status == GameStatus.Beaten),
            Completed100 = games.Count(g => g.Status == GameStatus.Completed100),
            Playing = games.Count(g => g.Status == GameStatus.Playing),
            Backlog = games.Count(g => g.Status == GameStatus.Backlog),
            Abandoned = games.Count(g => g.Status == GameStatus.Abandoned),
            Paused = games.Count(g => g.Status == GameStatus.Paused),
            Favorites = games.Count(g => g.IsFavorite),
            TotalPlaytimeMinutes = games.Sum(g => g.TotalPlaytimeMinutes),
            RatedGames = rated.Count,
            AverageRating = rated.Count > 0 ? Math.Round(rated.Average(g => g.PersonalRating!.Value), 1) : null,
            BestRatedGame = rated.OrderByDescending(g => g.PersonalRating).ThenBy(g => g.Title).FirstOrDefault(),
            FavoriteGenre = favoriteGenre,
        };
    }

    private void Exec(string sql, long id, object value)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Add("@id", id); cmd.Add("@v", value); cmd.Add("@u", DateTimeOffset.UtcNow.ToDb());
        cmd.ExecuteNonQuery();
    }

    public static string NormalizePath(string path)
    {
        var p = path.Trim().Trim('"');
        // Caminho do Windows (C:\...): normaliza separadores sem depender do SO em que o código roda.
        if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/'))
            return p.Replace('/', '\\');
        try { return Path.GetFullPath(p); }
        catch { return p; }
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static readonly string[] ColumnList =
    {
        "title", "normalized_title", "alternative_names", "short_description", "description", "developer", "publisher",
        "genres", "tags", "platforms", "release_date", "age_rating", "website", "trailer_url", "external_rating",
        "external_rating_source", "cover_path", "background_path", "logo_path", "icon_path", "executable_path",
        "launch_arguments", "working_directory", "source", "source_id", "metadata_provider", "metadata_id", "status",
        "is_favorite", "personal_rating", "playtime_minutes", "last_played_at", "added_at", "updated_at",
        "launch_uri", "started_at", "finished_at", "platform_playtime_minutes",
    };
    private static readonly string Columns = string.Join(", ", ColumnList);
    private static readonly string Params = string.Join(", ", ColumnList.Select(c => "@" + c));

    private static void Bind(SqliteCommand cmd, Game g)
    {
        if (string.IsNullOrWhiteSpace(g.Title)) throw new ArgumentException("O nome do jogo é obrigatório.");
        if (g.PersonalRating is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(g.PersonalRating), "A nota deve ficar entre 0 e 10.");
        cmd.Add("@title", g.Title.Trim());
        cmd.Add("@normalized_title", TitleNormalizer.Normalize(g.Title));
        cmd.Add("@alternative_names", g.AlternativeNames.ToDb());
        cmd.Add("@short_description", g.ShortDescription.ToDb());
        cmd.Add("@description", g.Description.ToDb());
        cmd.Add("@developer", g.Developer.ToDb());
        cmd.Add("@publisher", g.Publisher.ToDb());
        cmd.Add("@genres", DbConvert.ListToDb(g.Genres));
        cmd.Add("@tags", DbConvert.ListToDb(g.Tags));
        cmd.Add("@platforms", DbConvert.ListToDb(g.Platforms));
        cmd.Add("@release_date", g.ReleaseDate.ToDb());
        cmd.Add("@age_rating", g.AgeRating.ToDb());
        cmd.Add("@website", g.Website.ToDb());
        cmd.Add("@trailer_url", g.TrailerUrl.ToDb());
        cmd.Add("@external_rating", g.ExternalRating.ToDb());
        cmd.Add("@external_rating_source", g.ExternalRatingSource.ToDb());
        cmd.Add("@cover_path", g.CoverPath.ToDb());
        cmd.Add("@background_path", g.BackgroundPath.ToDb());
        cmd.Add("@logo_path", g.LogoPath.ToDb());
        cmd.Add("@icon_path", g.IconPath.ToDb());
        cmd.Add("@executable_path", g.ExecutablePath is { Length: > 0 } e ? NormalizePath(e) : null);
        cmd.Add("@launch_arguments", g.LaunchArguments.ToDb());
        cmd.Add("@working_directory", g.WorkingDirectory.ToDb());
        cmd.Add("@source", string.IsNullOrWhiteSpace(g.Source) ? GameSources.Manual : g.Source);
        cmd.Add("@source_id", g.SourceId.ToDb());
        cmd.Add("@metadata_provider", g.MetadataProvider.ToDb());
        cmd.Add("@metadata_id", g.MetadataId.ToDb());
        cmd.Add("@status", (int)g.Status);
        cmd.Add("@is_favorite", g.IsFavorite ? 1 : 0);
        cmd.Add("@personal_rating", g.PersonalRating.ToDb());
        cmd.Add("@playtime_minutes", Math.Max(0, g.PlaytimeMinutes));
        cmd.Add("@last_played_at", g.LastPlayedAt.ToDb());
        cmd.Add("@added_at", g.AddedAt.ToDb());
        cmd.Add("@updated_at", g.UpdatedAt.ToDb());
        cmd.Add("@launch_uri", g.LaunchUri.ToDb());
        cmd.Add("@started_at", g.StartedAt.ToDb());
        cmd.Add("@finished_at", g.FinishedAt.ToDb());
        cmd.Add("@platform_playtime_minutes", Math.Max(0, g.PlatformPlaytimeMinutes));
    }

    private static Game Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Title = r.Str("title") ?? "",
        AlternativeNames = r.Str("alternative_names"),
        ShortDescription = r.Str("short_description"),
        Description = r.Str("description"),
        Developer = r.Str("developer"),
        Publisher = r.Str("publisher"),
        Genres = DbConvert.ListFromDb(r.Str("genres")),
        Tags = DbConvert.ListFromDb(r.Str("tags")),
        Platforms = DbConvert.ListFromDb(r.Str("platforms")),
        ReleaseDate = r.Str("release_date"),
        AgeRating = r.Str("age_rating"),
        Website = r.Str("website"),
        TrailerUrl = r.Str("trailer_url"),
        ExternalRating = r.Dbl("external_rating"),
        ExternalRatingSource = r.Str("external_rating_source"),
        CoverPath = r.Str("cover_path"),
        BackgroundPath = r.Str("background_path"),
        LogoPath = r.Str("logo_path"),
        IconPath = r.Str("icon_path"),
        ExecutablePath = r.Str("executable_path"),
        LaunchArguments = r.Str("launch_arguments"),
        WorkingDirectory = r.Str("working_directory"),
        Source = r.Str("source") ?? GameSources.Manual,
        SourceId = r.Str("source_id"),
        MetadataProvider = r.Str("metadata_provider"),
        MetadataId = r.Str("metadata_id"),
        Status = (GameStatus)(r.Long("status") ?? 0),
        IsFavorite = r.Long("is_favorite") == 1,
        PersonalRating = r.Dbl("personal_rating"),
        PlaytimeMinutes = r.Long("playtime_minutes") ?? 0,
        LastPlayedAt = r.Date("last_played_at"),
        AddedAt = r.Date("added_at") ?? DateTimeOffset.UtcNow,
        UpdatedAt = r.Date("updated_at") ?? DateTimeOffset.UtcNow,
        LaunchUri = r.Str("launch_uri"),
        StartedAt = r.Str("started_at"),
        FinishedAt = r.Str("finished_at"),
        PlatformPlaytimeMinutes = r.Long("platform_playtime_minutes") ?? 0,
    };
}
