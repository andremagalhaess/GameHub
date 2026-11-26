namespace GameHub.Core.Models;

/// <summary>Perfil local do jogador (não existe conta online).</summary>
public sealed class PlayerProfile
{
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Bio { get; set; }
    public string? AvatarPath { get; set; }
    public string? BannerPath { get; set; }
    /// <summary>Cor de destaque em #RRGGBB.</summary>
    public string AccentColor { get; set; } = "#7C5CFF";
    public long? CurrentGameId { get; set; }

    public bool IsCreated => !string.IsNullOrWhiteSpace(DisplayName);
}

/// <summary>Review completa de um jogo (a nota fica em <see cref="Game.PersonalRating"/>).</summary>
public sealed class GameReview
{
    public long GameId { get; set; }
    public string? Review { get; set; }
    public string? Pros { get; set; }
    public string? Cons { get; set; }
    public string? Comments { get; set; }
    public string? Moments { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsEmpty => new[] { Review, Pros, Cons, Comments, Moments }.All(string.IsNullOrWhiteSpace);
}

/// <summary>Um vínculo do jogo com uma loja/launcher.</summary>
public sealed class GameSourceLink
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string? InstallDir { get; set; }
    public string? ExecutablePath { get; set; }
    public string? LaunchUri { get; set; }
    public long? PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public bool IsInstalled { get; set; }
}

public sealed class PlaySession
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public long Minutes { get; set; }
}

/// <summary>Estatísticas agregadas calculadas a partir do banco (sem valores inventados).</summary>
public sealed class LibraryStats
{
    public int TotalGames { get; init; }
    public int Beaten { get; init; }
    public int Completed100 { get; init; }
    public int Playing { get; init; }
    public int Backlog { get; init; }
    public int Abandoned { get; init; }
    public int Paused { get; init; }
    public int Favorites { get; init; }
    public long TotalPlaytimeMinutes { get; init; }
    public int RatedGames { get; init; }
    public double? AverageRating { get; init; }
    public Game? BestRatedGame { get; init; }
    public string? FavoriteGenre { get; init; }

    public double TotalPlaytimeHours => Math.Round(TotalPlaytimeMinutes / 60.0, 1);
    /// <summary>Zerados + 100%.</summary>
    public int Finished => Beaten + Completed100;
}
