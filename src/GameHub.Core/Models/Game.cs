namespace GameHub.Core.Models;

/// <summary>Status de progresso pessoal de um jogo.</summary>
public enum GameStatus
{
    None = 0,
    Backlog = 1,
    Playing = 2,
    Beaten = 3,
    Completed100 = 4,
    Abandoned = 5,
    Paused = 6,
}

/// <summary>Um jogo da biblioteca. Todos os campos de metadados são editáveis pelo usuário.</summary>
public sealed class Game
{
    public long Id { get; set; }

    // Identificação
    public string Title { get; set; } = "";
    public string? AlternativeNames { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    /// <summary>Data de lançamento em ISO (yyyy-MM-dd) quando conhecida; senão texto livre (ex.: "2020").</summary>
    public string? ReleaseDate { get; set; }
    public string? AgeRating { get; set; }
    public string? Website { get; set; }
    public string? TrailerUrl { get; set; }
    public double? ExternalRating { get; set; }
    public string? ExternalRatingSource { get; set; }

    // Arte (caminhos locais em cache)
    public string? CoverPath { get; set; }
    public string? BackgroundPath { get; set; }
    public string? LogoPath { get; set; }
    public string? IconPath { get; set; }

    // Execução
    public string? ExecutablePath { get; set; }
    public string? LaunchArguments { get; set; }
    public string? WorkingDirectory { get; set; }

    // Origem
    /// <summary>Loja/launcher de origem (ex.: "Manual", "Steam").</summary>
    public string Source { get; set; } = GameSources.Manual;
    public string? SourceId { get; set; }
    public string? MetadataProvider { get; set; }
    public string? MetadataId { get; set; }

    // Dados pessoais
    public GameStatus Status { get; set; } = GameStatus.None;
    public bool IsFavorite { get; set; }
    /// <summary>Nota pessoal de 0 a 10 (null = sem nota).</summary>
    public double? PersonalRating { get; set; }
    /// <summary>Minutos jogados (sessões medidas pelo GameHub + ajustes manuais).</summary>
    public long PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? ReleaseYear =>
        ReleaseDate is { Length: >= 4 } d && int.TryParse(d.AsSpan(0, 4), out var y) ? y : null;

    public double PlaytimeHours => Math.Round(PlaytimeMinutes / 60.0, 1);
}

public static class GameSources
{
    public const string Manual = "Manual";
    public const string Steam = "Steam";
}

public static class GameStatusText
{
    public static string ToDisplay(this GameStatus s) => s switch
    {
        GameStatus.None => "Sem status",
        GameStatus.Backlog => "Backlog",
        GameStatus.Playing => "Jogando",
        GameStatus.Beaten => "Zerado",
        GameStatus.Completed100 => "100%",
        GameStatus.Abandoned => "Abandonado",
        GameStatus.Paused => "Pausado",
        _ => s.ToString(),
    };

    public static IReadOnlyList<GameStatus> All { get; } = Enum.GetValues<GameStatus>();
}
