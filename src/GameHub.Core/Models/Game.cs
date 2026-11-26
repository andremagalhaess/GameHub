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
    /// <summary>Atalho de launcher (ex.: steam://rungameid/570). Usado quando não há executável.</summary>
    public string? LaunchUri { get; set; }

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
    /// <summary>Maior tempo informado por uma plataforma confiável (ex.: Steam Web API).</summary>
    public long PlatformPlaytimeMinutes { get; set; }
    /// <summary>Data em que comecei a jogar (yyyy-MM-dd), informada pelo usuário.</summary>
    public string? StartedAt { get; set; }
    /// <summary>Data de conclusão (yyyy-MM-dd), informada pelo usuário.</summary>
    public string? FinishedAt { get; set; }
    /// <summary>Estimativas do HowLongToBeat informadas pelo usuário (minutos). Null = não informado.</summary>
    public long? HltbMainMinutes { get; set; }
    public long? HltbExtraMinutes { get; set; }
    public long? HltbCompleteMinutes { get; set; }
    public bool HasHltb => HltbMainMinutes is > 0 || HltbExtraMinutes is > 0 || HltbCompleteMinutes is > 0;
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? ReleaseYear =>
        ReleaseDate is { Length: >= 4 } d && int.TryParse(d.AsSpan(0, 4), out var y) ? y : null;

    /// <summary>
    /// Tempo exibido: o maior entre o medido/registrado no GameHub e o da plataforma
    /// (não soma, para não contar a mesma sessão duas vezes).
    /// </summary>
    public long TotalPlaytimeMinutes => Math.Max(PlaytimeMinutes, PlatformPlaytimeMinutes);
    public double PlaytimeHours => Math.Round(TotalPlaytimeMinutes / 60.0, 1);
    public bool CanLaunch => !string.IsNullOrWhiteSpace(ExecutablePath) || !string.IsNullOrWhiteSpace(LaunchUri);
}

public static class GameSources
{
    public const string Manual = "Manual";
    public const string Steam = "Steam";
    public const string Epic = "Epic Games";
    public const string Gog = "GOG";
    public const string Ubisoft = "Ubisoft Connect";
    public const string Ea = "EA app";
    public const string BattleNet = "Battle.net";
    public const string Riot = "Riot";
    public const string Rockstar = "Rockstar";
    public const string Amazon = "Amazon Games";
    public const string Xbox = "Xbox / PC Game Pass";
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
