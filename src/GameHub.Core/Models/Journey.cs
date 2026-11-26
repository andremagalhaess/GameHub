namespace GameHub.Core.Models;

public enum MemoryKind { Text = 0, Screenshot = 1, Image = 2, Moment = 3, Scene = 4, Note = 5 }

/// <summary>Uma memória de um jogo: screenshot, imagem, texto, momento favorito, cena marcante ou observação.</summary>
public sealed class GameMemory
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public MemoryKind Kind { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? ImagePath { get; set; }
    /// <summary>yyyy-MM-dd (opcional).</summary>
    public string? HappenedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class MemoryKindText
{
    public static string ToDisplay(this MemoryKind k) => k switch
    {
        MemoryKind.Screenshot => "Screenshot",
        MemoryKind.Image => "Imagem",
        MemoryKind.Moment => "Momento favorito",
        MemoryKind.Scene => "Cena marcante",
        MemoryKind.Note => "Observação pessoal",
        _ => "Texto",
    };
    public static IReadOnlyList<MemoryKind> All { get; } = Enum.GetValues<MemoryKind>();
}

public sealed class Ranking
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? Year { get; set; }
    public int? MaxItems { get; set; }
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RankingEntry
{
    public int Position { get; set; }
    public Game Game { get; set; } = null!;
    public string? Note { get; set; }
}

/// <summary>Um registro da linha do tempo (só com datas reais; jogos sem data ficam em "Sem data").</summary>
public sealed class TimelineEntry
{
    public Game Game { get; init; } = null!;
    /// <summary>Data usada para ordenar/agrupar: início, conclusão ou primeira sessão registrada.</summary>
    public DateOnly? Date { get; init; }
    public string DateSource { get; init; } = "";
    public DateOnly? Started { get; init; }
    public DateOnly? Finished { get; init; }
    public string? ReviewExcerpt { get; init; }
}

public sealed class CountItem
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
}

public sealed class JourneyDashboard
{
    public LibraryStats Library { get; init; } = new();
    public string? TopPlatform { get; init; }
    public IReadOnlyList<CountItem> FinishedPerYear { get; init; } = Array.Empty<CountItem>();
    public IReadOnlyList<CountItem> TopGenres { get; init; } = Array.Empty<CountItem>();
    public IReadOnlyList<CountItem> StatusBreakdown { get; init; } = Array.Empty<CountItem>();
}

/// <summary>Retrospectiva "Meu Ano Gamer".</summary>
public sealed class YearInReview
{
    public int Year { get; init; }
    public IReadOnlyList<Game> Started { get; init; } = Array.Empty<Game>();
    public IReadOnlyList<Game> Finished { get; init; } = Array.Empty<Game>();
    public IReadOnlyList<Game> Played { get; init; } = Array.Empty<Game>();
    /// <summary>Minutos medidos pelo GameHub em sessões daquele ano (as plataformas só informam o total).</summary>
    public long TrackedMinutes { get; init; }
    public Game? Best { get; init; }
    public Game? Worst { get; init; }
    public string? TopGenre { get; init; }
    public string? TopPlatform { get; init; }
    public IReadOnlyList<Game> Favorites { get; init; } = Array.Empty<Game>();
    public IReadOnlyList<(Game Game, GameReview Review)> Reviews { get; init; } = Array.Empty<(Game, GameReview)>();
    public IReadOnlyList<CountItem> FinishedPerMonth { get; init; } = Array.Empty<CountItem>();
    public bool IsEmpty => Started.Count == 0 && Finished.Count == 0 && Played.Count == 0 && Reviews.Count == 0;
}

public enum CalendarEventKind { Started, Finished, Session, Memory }

public sealed class CalendarEvent
{
    public DateOnly Date { get; init; }
    public CalendarEventKind Kind { get; init; }
    public Game Game { get; init; } = null!;
    public long Minutes { get; init; }
    public string Label => Kind switch
    {
        CalendarEventKind.Started => "Comecei",
        CalendarEventKind.Finished => "Terminei",
        CalendarEventKind.Memory => "Memória",
        _ => Minutes > 0 ? $"Joguei {Minutes} min" : "Joguei",
    };
}
