using GameHub.Core.Models;
using GameHub.Core.Text;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

public enum GameSort { Title, RecentlyAdded, RecentlyPlayed, Playtime, ReleaseDate }

public sealed record GameQuery(
    string? Search = null,
    GameStatus? Status = null,
    bool FavoritesOnly = false,
    GameSort Sort = GameSort.Title,
    int? Limit = null);

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

    /// <summary>Remove o jogo da biblioteca. NÃO desinstala nem apaga arquivos do jogo.</summary>
    public bool Remove(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM games WHERE id = @id;";
        cmd.Add("@id", id);
        return cmd.ExecuteNonQuery() > 0;
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
        var order = q.Sort switch
        {
            GameSort.RecentlyAdded => "added_at DESC",
            GameSort.RecentlyPlayed => "last_played_at IS NULL, last_played_at DESC",
            GameSort.Playtime => "playtime_minutes DESC",
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
            .Where(g => g.Status is not GameStatus.None and not GameStatus.Backlog || g.PlaytimeMinutes > 0)
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
            TotalPlaytimeMinutes = games.Sum(g => g.PlaytimeMinutes),
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
        try { return Path.GetFullPath(path.Trim().Trim('"')); }
        catch { return path.Trim(); }
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static readonly string[] ColumnList =
    {
        "title", "normalized_title", "alternative_names", "short_description", "description", "developer", "publisher",
        "genres", "tags", "platforms", "release_date", "age_rating", "website", "trailer_url", "external_rating",
        "external_rating_source", "cover_path", "background_path", "logo_path", "icon_path", "executable_path",
        "launch_arguments", "working_directory", "source", "source_id", "metadata_provider", "metadata_id", "status",
        "is_favorite", "personal_rating", "playtime_minutes", "last_played_at", "added_at", "updated_at",
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
    };
}
