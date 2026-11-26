using System.Globalization;
using GameHub.Core.Data;
using GameHub.Core.Models;

namespace GameHub.Core.Journey;

/// <summary>
/// "Minha Jornada": painel, linha do tempo, Meu Ano Gamer e calendário, calculados só com dados
/// registrados (datas que o usuário informou, sessões medidas, notas e reviews). Nada é inferido:
/// um jogo iniciado ou fechado não vira "zerado" sozinho.
/// </summary>
public sealed class JourneyService
{
    private readonly GameRepository _games;
    private readonly ReviewRepository _reviews;
    private readonly MemoryRepository _memories;

    public JourneyService(GameRepository games, ReviewRepository reviews, MemoryRepository memories)
    {
        _games = games;
        _reviews = reviews;
        _memories = memories;
    }

    /// <summary>Jogos que fazem parte da história do jogador (não só cadastrados).</summary>
    public static bool IsPartOfJourney(Game g) =>
        g.Status is not GameStatus.None and not GameStatus.Backlog
        || g.TotalPlaytimeMinutes > 0 || g.StartedAt is not null || g.FinishedAt is not null || g.PersonalRating is not null;

    public JourneyDashboard Dashboard()
    {
        var all = _games.All();
        var played = all.Where(IsPartOfJourney).ToList();
        return new JourneyDashboard
        {
            Library = _games.Stats(),
            TopPlatform = TopPlatform(played),
            FinishedPerYear = all
                .Where(g => g.Status is GameStatus.Beaten or GameStatus.Completed100 && ParseDate(g.FinishedAt) is not null)
                .GroupBy(g => ParseDate(g.FinishedAt)!.Value.Year)
                .OrderBy(gr => gr.Key)
                .Select(gr => new CountItem { Label = gr.Key.ToString(), Value = gr.Count() })
                .ToList(),
            TopGenres = played.SelectMany(g => g.Genres)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key)
                .Take(8)
                .Select(gr => new CountItem { Label = gr.Key, Value = gr.Count() })
                .ToList(),
            StatusBreakdown = GameStatusText.All.Where(s => s != GameStatus.None)
                .Select(s => new CountItem { Label = s.ToDisplay(), Value = all.Count(g => g.Status == s) })
                .Where(c => c.Value > 0)
                .ToList(),
        };
    }

    public sealed record TimelineFilter(
        DateOnly? From = null, DateOnly? To = null, GameStatus? Status = null, double? MinRating = null,
        string? Genre = null, string? Source = null, long? MinMinutes = null, long? MaxMinutes = null, bool IncludeUndated = true);

    /// <summary>Linha do tempo: um registro por jogo, datado pelo início, pela conclusão ou pela primeira sessão medida.</summary>
    public List<TimelineEntry> Timeline(TimelineFilter? f = null)
    {
        f ??= new TimelineFilter();
        var reviews = _reviews.All().ToDictionary(r => r.GameId);
        var firstSessions = _games.SessionsBetween(DateTimeOffset.MinValue.AddYears(1), DateTimeOffset.MaxValue.AddYears(-1))
            .GroupBy(s => s.GameId)
            .ToDictionary(g => g.Key, g => DateOnly.FromDateTime(g.Min(s => s.StartedAt).ToLocalTime().DateTime));

        var entries = new List<TimelineEntry>();
        foreach (var g in _games.All().Where(IsPartOfJourney))
        {
            if (f.Status is { } st && g.Status != st) continue;
            if (f.MinRating is { } mr && (g.PersonalRating ?? -1) < mr) continue;
            if (f.Genre is { } ge && !g.Genres.Contains(ge, StringComparer.OrdinalIgnoreCase)) continue;
            if (f.Source is { } so && g.Source != so) continue;
            if (f.MinMinutes is { } mn && g.TotalPlaytimeMinutes < mn) continue;
            if (f.MaxMinutes is { } mx && g.TotalPlaytimeMinutes > mx) continue;

            var started = ParseDate(g.StartedAt);
            var finished = ParseDate(g.FinishedAt);
            DateOnly? date; string source;
            if (started is not null) { date = started; source = "Início"; }
            else if (finished is not null) { date = finished; source = "Conclusão"; }
            else if (firstSessions.TryGetValue(g.Id, out var fs)) { date = fs; source = "Primeira sessão no GameHub"; }
            else { date = null; source = "Sem data"; }

            if (date is null && !f.IncludeUndated) continue;
            if (date is not null)
            {
                // O jogo entra no período se início, conclusão ou data principal caírem dentro dele.
                var inRange = new[] { date, started, finished }.Where(d => d is not null)
                    .Any(d => (f.From is null || d >= f.From) && (f.To is null || d <= f.To));
                if (!inRange) continue;
            }
            else if (f.From is not null || f.To is not null) continue;

            entries.Add(new TimelineEntry
            {
                Game = g,
                Date = date,
                DateSource = source,
                Started = started,
                Finished = finished,
                ReviewExcerpt = reviews.TryGetValue(g.Id, out var rv) ? Excerpt(rv.Review) : null,
            });
        }
        return entries.OrderByDescending(e => e.Date.HasValue).ThenByDescending(e => e.Date).ThenBy(e => e.Game.Title).ToList();
    }

    public List<int> YearsWithActivity()
    {
        var years = new HashSet<int>();
        foreach (var g in _games.All())
        {
            if (ParseDate(g.StartedAt) is { } s) years.Add(s.Year);
            if (ParseDate(g.FinishedAt) is { } f) years.Add(f.Year);
        }
        foreach (var s in _games.SessionsBetween(DateTimeOffset.MinValue.AddYears(1), DateTimeOffset.MaxValue.AddYears(-1)))
            years.Add(s.StartedAt.ToLocalTime().Year);
        foreach (var r in _reviews.All()) years.Add(r.CreatedAt.ToLocalTime().Year);
        return years.OrderByDescending(y => y).ToList();
    }

    public YearInReview YearInReview(int year)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        bool In(DateOnly? d) => d is not null && d >= from && d <= to;

        var all = _games.All();
        var started = all.Where(g => In(ParseDate(g.StartedAt))).ToList();
        var finished = all.Where(g => g.Status is GameStatus.Beaten or GameStatus.Completed100 && In(ParseDate(g.FinishedAt))).ToList();
        var localStart = new DateTimeOffset(new DateTime(year, 1, 1), TimeZoneInfo.Local.GetUtcOffset(new DateTime(year, 1, 1)));
        var localEnd = new DateTimeOffset(new DateTime(year + 1, 1, 1), TimeZoneInfo.Local.GetUtcOffset(new DateTime(year + 1, 1, 1)));
        var sessions = _games.SessionsBetween(localStart, localEnd);
        var sessionGames = sessions.Select(s => s.GameId).ToHashSet();
        var reviewsInYear = _reviews.All().Where(r => r.CreatedAt.ToLocalTime().Year == year || r.UpdatedAt.ToLocalTime().Year == year).ToList();

        var played = all.Where(g => started.Contains(g) || finished.Contains(g) || sessionGames.Contains(g.Id)
                                    || In(ParseDate(g.FinishedAt)) || (g.LastPlayedAt?.ToLocalTime().Year == year))
                        .ToList();
        var rated = played.Where(g => g.PersonalRating is not null).ToList();
        var byId = all.ToDictionary(g => g.Id);

        return new YearInReview
        {
            Year = year,
            Started = started,
            Finished = finished,
            Played = played,
            TrackedMinutes = sessions.Sum(s => s.Minutes),
            Best = rated.OrderByDescending(g => g.PersonalRating).ThenBy(g => g.Title).FirstOrDefault(),
            Worst = rated.Count > 1 ? rated.OrderBy(g => g.PersonalRating).ThenBy(g => g.Title).First() : null,
            TopGenre = played.SelectMany(g => g.Genres).GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key).Select(gr => gr.Key).FirstOrDefault(),
            TopPlatform = TopPlatform(played),
            Favorites = played.Where(g => g.IsFavorite).ToList(),
            Reviews = reviewsInYear.Where(r => byId.ContainsKey(r.GameId)).Select(r => (byId[r.GameId], r)).ToList(),
            FinishedPerMonth = Enumerable.Range(1, 12).Select(m => new CountItem
            {
                Label = CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.GetAbbreviatedMonthName(m).TrimEnd('.'),
                Value = finished.Count(g => ParseDate(g.FinishedAt)!.Value.Month == m),
            }).ToList(),
        };
    }

    /// <summary>Eventos do calendário no intervalo: inícios, conclusões, sessões medidas e memórias datadas.</summary>
    public List<CalendarEvent> Calendar(DateOnly from, DateOnly to)
    {
        var events = new List<CalendarEvent>();
        var all = _games.All();
        var byId = all.ToDictionary(g => g.Id);
        foreach (var g in all)
        {
            if (ParseDate(g.StartedAt) is { } s && s >= from && s <= to) events.Add(new CalendarEvent { Date = s, Kind = CalendarEventKind.Started, Game = g });
            if (ParseDate(g.FinishedAt) is { } f && f >= from && f <= to) events.Add(new CalendarEvent { Date = f, Kind = CalendarEventKind.Finished, Game = g });
        }
        var fromTs = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(from.ToDateTime(TimeOnly.MinValue)));
        var toTs = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(to.ToDateTime(TimeOnly.MinValue)));
        foreach (var day in _games.SessionsBetween(fromTs, toTs)
                     .GroupBy(s => (Day: DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime), s.GameId)))
            if (byId.TryGetValue(day.Key.GameId, out var g))
                events.Add(new CalendarEvent { Date = day.Key.Day, Kind = CalendarEventKind.Session, Game = g, Minutes = day.Sum(s => s.Minutes) });
        foreach (var m in _memories.All())
            if (ParseDate(m.HappenedAt) is { } d && d >= from && d <= to && byId.TryGetValue(m.GameId, out var g))
                events.Add(new CalendarEvent { Date = d, Kind = CalendarEventKind.Memory, Game = g });
        return events.OrderBy(e => e.Date).ThenBy(e => e.Kind).ToList();
    }

    private static string? TopPlatform(IEnumerable<Game> played) =>
        played.GroupBy(g => g.Source)
            .OrderByDescending(gr => gr.Sum(g => g.TotalPlaytimeMinutes)).ThenByDescending(gr => gr.Count()).ThenBy(gr => gr.Key)
            .Select(gr => gr.Key).FirstOrDefault();

    public static DateOnly? ParseDate(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string? Excerpt(string? text) => GameHub.Core.Text.HtmlText.Summarize(text, 180);
}
