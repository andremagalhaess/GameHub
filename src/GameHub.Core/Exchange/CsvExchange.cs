using System.Globalization;
using GameHub.Core.Data;
using GameHub.Core.Models;
using GameHub.Core.Text;

namespace GameHub.Core.Exchange;

/// <summary>Resultado de uma importação, mostrado ao usuário.</summary>
public sealed class ImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Problems { get; } = new();
    public string Summary =>
        $"{Added} adicionado(s), {Updated} atualizado(s), {Skipped} ignorado(s)" + (Problems.Count > 0 ? $", {Problems.Count} aviso(s)" : "") + ".";
}

public enum RatingScale { Auto, ZeroToTen, ZeroToFive }

public sealed record GameImportOptions(RatingScale Scale = RatingScale.Auto, bool SkipWishlist = true, string? DefaultSource = null);

/// <summary>
/// Importação e exportação em CSV: jogos, reviews, jornada (datas, sessões e memórias) e rankings.
/// A importação reconhece colunas em português (formato do GameHub) e em inglês (ex.: exportação do Backloggd).
/// Nada é sincronizado automaticamente: o usuário escolhe o arquivo.
/// </summary>
public sealed class CsvExchange
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly GameRepository _games;
    private readonly ReviewRepository _reviews;
    private readonly MemoryRepository _memories;
    private readonly RankingRepository _rankings;

    public CsvExchange(GameRepository games, ReviewRepository reviews, MemoryRepository memories, RankingRepository rankings)
    {
        _games = games; _reviews = reviews; _memories = memories; _rankings = rankings;
    }

    // Nomes de colunas aceitos (o primeiro é o que o GameHub exporta).
    private static readonly string[] CTitle = { "jogo", "titulo", "nome", "game", "title", "name", "game name" };
    private static readonly string[] CStatus = { "status", "estado", "situacao", "play status" };
    private static readonly string[] CFavorite = { "favorito", "favorite", "liked", "like" };
    private static readonly string[] CRating = { "nota", "avaliacao", "rating", "score", "my rating" };
    private static readonly string[] CMinutes = { "minutos gamehub", "minutos jogados", "minutes played" };
    private static readonly string[] CHours = { "horas", "horas jogadas", "hours", "hours played", "time played", "playtime" };
    private static readonly string[] CPlatformMinutes = { "minutos plataforma" };
    private static readonly string[] CStarted = { "comecou em", "inicio", "data de inicio", "started", "start date", "started on", "date started" };
    private static readonly string[] CFinished = { "terminou em", "conclusao", "data de conclusao", "finished", "finish date", "finished on", "completed on", "date finished", "date completed" };
    private static readonly string[] CSource = { "origem", "loja", "source", "store" };
    private static readonly string[] CPlatforms = { "plataformas", "plataforma", "platforms", "platform" };
    private static readonly string[] CGenres = { "generos", "genero", "genres", "genre" };
    private static readonly string[] CTags = { "tags" };
    private static readonly string[] CDeveloper = { "desenvolvedora", "developer" };
    private static readonly string[] CPublisher = { "publicadora", "publisher" };
    private static readonly string[] CRelease = { "lancamento", "data de lancamento", "release date", "released" };
    private static readonly string[] CExe = { "executavel", "exe", "executable" };
    private static readonly string[] CArgs = { "argumentos", "arguments" };
    private static readonly string[] CWorkDir = { "pasta de trabalho", "working directory" };
    private static readonly string[] CLaunchUri = { "uri de inicializacao", "launch uri" };
    private static readonly string[] CHltbMain = { "hltb historia principal min", "hltb main" };
    private static readonly string[] CHltbExtra = { "hltb com extras min", "hltb extra" };
    private static readonly string[] CHltbComplete = { "hltb completo min", "hltb completionist" };
    private static readonly string[] CReview = { "review", "resenha", "review text", "comentario" };
    private static readonly string[] CPros = { "pontos positivos", "pros" };
    private static readonly string[] CCons = { "pontos negativos", "cons" };
    private static readonly string[] CMoments = { "momentos memoraveis", "moments" };
    private static readonly string[] CComments = { "comentarios", "comments", "notes" };
    private static readonly string[] CWishlist = { "wishlist", "lista de desejos" };
    private static readonly string[] CBacklog = { "backlog", "backlogged" };
    private static readonly string[] CPlaying = { "playing", "jogando agora" };

    // =============== Exportação ===============

    public IReadOnlyList<string> ExportAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var files = new[]
        {
            Path.Combine(folder, "GameHub-jogos.csv"),
            Path.Combine(folder, "GameHub-reviews.csv"),
            Path.Combine(folder, "GameHub-jornada.csv"),
            Path.Combine(folder, "GameHub-rankings.csv"),
        };
        Csv.WriteFile(files[0], GameRows());
        Csv.WriteFile(files[1], ReviewRows());
        Csv.WriteFile(files[2], JourneyRows());
        Csv.WriteFile(files[3], RankingRows());
        return files;
    }

    public IEnumerable<IReadOnlyList<string?>> GameRows()
    {
        yield return new[]
        {
            "Jogo", "Status", "Favorito", "Nota", "Minutos GameHub", "Minutos plataforma", "Começou em", "Terminou em",
            "Origem", "Plataformas", "Gêneros", "Tags", "Desenvolvedora", "Publicadora", "Lançamento",
            "Executável", "Argumentos", "Pasta de trabalho", "URI de inicialização",
            "HLTB história principal (min)", "HLTB com extras (min)", "HLTB completo (min)",
        };
        foreach (var g in _games.All())
            yield return new[]
            {
                g.Title, g.Status == GameStatus.None ? "" : g.Status.ToDisplay(), g.IsFavorite ? "sim" : "", Num(g.PersonalRating),
                g.PlaytimeMinutes.ToString(Inv), g.PlatformPlaytimeMinutes.ToString(Inv), g.StartedAt, g.FinishedAt,
                g.Source, string.Join(", ", g.Platforms), string.Join(", ", g.Genres), string.Join(", ", g.Tags),
                g.Developer, g.Publisher, g.ReleaseDate,
                g.ExecutablePath, g.LaunchArguments, g.WorkingDirectory, g.LaunchUri,
                g.HltbMainMinutes?.ToString(Inv), g.HltbExtraMinutes?.ToString(Inv), g.HltbCompleteMinutes?.ToString(Inv),
            };
    }

    public IEnumerable<IReadOnlyList<string?>> ReviewRows()
    {
        yield return new[] { "Jogo", "Nota", "Status", "Review", "Pontos positivos", "Pontos negativos", "Momentos memoráveis", "Comentários", "Criada em", "Atualizada em" };
        var byId = _games.All().ToDictionary(g => g.Id);
        foreach (var r in _reviews.All())
            if (byId.TryGetValue(r.GameId, out var g))
                yield return new[]
                {
                    g.Title, Num(g.PersonalRating), g.Status == GameStatus.None ? "" : g.Status.ToDisplay(),
                    r.Review, r.Pros, r.Cons, r.Moments, r.Comments,
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm", Inv), r.UpdatedAt.ToString("yyyy-MM-dd HH:mm", Inv),
                };
    }

    /// <summary>Jornada: uma linha por evento (início, conclusão, sessão medida, memória).</summary>
    public IEnumerable<IReadOnlyList<string?>> JourneyRows()
    {
        yield return new[] { "Tipo", "Jogo", "Data", "Fim", "Minutos", "Categoria", "Título", "Texto", "Imagem" };
        var games = _games.All();
        var byId = games.ToDictionary(g => g.Id);
        foreach (var g in games)
        {
            if (g.StartedAt is { } s) yield return new[] { "inicio", g.Title, s, null, null, null, null, null, null };
            if (g.FinishedAt is { } f) yield return new[] { "conclusao", g.Title, f, null, null, null, null, null, null };
        }
        foreach (var s in _games.SessionsBetween(DateTimeOffset.MinValue.AddYears(1), DateTimeOffset.MaxValue.AddYears(-1)))
            if (byId.TryGetValue(s.GameId, out var g))
                yield return new[] { "sessao", g.Title, s.StartedAt.ToString("O", Inv), s.EndedAt.ToString("O", Inv), s.Minutes.ToString(Inv), null, null, null, null };
        foreach (var m in _memories.All())
            if (byId.TryGetValue(m.GameId, out var g))
                yield return new[] { "memoria", g.Title, m.HappenedAt, null, null, m.Kind.ToString(), m.Title, m.Text, m.ImagePath };
    }

    public IEnumerable<IReadOnlyList<string?>> RankingRows()
    {
        yield return new[] { "Ranking", "Descrição", "Ano", "Limite", "Posição", "Jogo" };
        foreach (var r in _rankings.All())
        {
            var entries = _rankings.Entries(r.Id);
            if (entries.Count == 0)
                yield return new[] { r.Name, r.Description, r.Year?.ToString(Inv), r.MaxItems?.ToString(Inv), null, null };
            foreach (var e in entries)
                yield return new[] { r.Name, r.Description, r.Year?.ToString(Inv), r.MaxItems?.ToString(Inv), e.Position.ToString(Inv), e.Game.Title };
        }
    }

    /// <summary>
    /// CSV no formato mais comum de planilhas de diário (colunas em inglês, nota de 0,5 a 5 estrelas),
    /// para levar avaliações e histórico ao Backloggd ou a outra ferramenta. O Backloggd não tem API pública
    /// de escrita: este arquivo é para uso manual.
    /// </summary>
    public IEnumerable<IReadOnlyList<string?>> BackloggdStyleRows()
    {
        yield return new[] { "Game", "Platform", "Status", "Rating", "Review", "Started", "Finished", "Hours Played", "Liked" };
        var reviews = _reviews.All().ToDictionary(r => r.GameId);
        foreach (var g in _games.All().Where(g => g.Status != GameStatus.None || g.PersonalRating is not null || reviews.ContainsKey(g.Id)))
            yield return new[]
            {
                g.Title, g.Platforms.FirstOrDefault() ?? "PC", EnglishStatus(g.Status),
                g.PersonalRating is { } r ? (Math.Round(r) / 2.0).ToString("0.#", Inv) : null,
                reviews.TryGetValue(g.Id, out var rv) ? rv.Review : null,
                g.StartedAt, g.FinishedAt,
                g.TotalPlaytimeMinutes > 0 ? (g.TotalPlaytimeMinutes / 60.0).ToString("0.#", Inv) : null,
                g.IsFavorite ? "true" : null,
            };
    }

    private static string EnglishStatus(GameStatus s) => s switch
    {
        GameStatus.Backlog => "Backlog",
        GameStatus.Playing => "Playing",
        GameStatus.Beaten => "Completed",
        GameStatus.Completed100 => "Mastered",
        GameStatus.Abandoned => "Abandoned",
        GameStatus.Paused => "Shelved",
        _ => "Played",
    };

    private static string? Num(double? d) => d?.ToString("0.#", Inv);

    // =============== Importação ===============

    private Dictionary<string, Game> TitleIndex()
    {
        var map = new Dictionary<string, Game>();
        foreach (var g in _games.All())
        {
            map.TryAdd(TitleNormalizer.Normalize(g.Title), g);
            map.TryAdd(g.Title.Trim().ToLowerInvariant(), g);
        }
        return map;
    }

    private static Game? Find(Dictionary<string, Game> idx, string title) =>
        idx.TryGetValue(title.Trim().ToLowerInvariant(), out var g) ? g
        : idx.TryGetValue(TitleNormalizer.Normalize(title), out g) ? g : null;

    /// <summary>
    /// Importa jogos (formato do GameHub, Backloggd ou planilha própria). Jogos com o mesmo nome são atualizados:
    /// campos preenchidos no arquivo substituem os do GameHub; horas só aumentam. Nada é executado.
    /// </summary>
    public ImportResult ImportGames(string csvText, GameImportOptions? options = null)
    {
        options ??= new GameImportOptions();
        var t = new CsvTable(csvText);
        if (!t.Has(CTitle)) throw new InvalidDataException("O arquivo não tem uma coluna com o nome do jogo (ex.: \"Jogo\", \"Game\" ou \"Title\").");
        var result = new ImportResult();
        var idx = TitleIndex();
        var fiveScale = options.Scale == RatingScale.ZeroToFive
            || options.Scale == RatingScale.Auto && LooksLikeFiveStars(t);

        for (var line = 0; line < t.Rows.Count; line++)
        {
            var row = t.Rows[line];
            var lineNo = line + 2;
            var title = t.Get(row, CTitle);
            if (title is null) { result.Skipped++; continue; }
            try
            {
                if (options.SkipWishlist && (IsTrue(t.Get(row, CWishlist)) || string.Equals(t.Get(row, CStatus), "wishlist", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Skipped++;
                    continue;
                }
                var existing = Find(idx, title);
                var g = existing ?? new Game { Title = title, Source = options.DefaultSource ?? GameSources.Manual };

                if (ParseStatus(t.Get(row, CStatus)) is { } st) g.Status = st;
                else if (IsTrue(t.Get(row, CPlaying))) g.Status = GameStatus.Playing;
                else if (IsTrue(t.Get(row, CBacklog)) && g.Status == GameStatus.None) g.Status = GameStatus.Backlog;
                if (t.Get(row, CFavorite) is { } fav) g.IsFavorite = IsTrue(fav);
                if (ParseNumber(t.Get(row, CRating)) is { } rating)
                {
                    var r = fiveScale ? rating * 2 : rating;
                    if (r is < 0 or > 10) result.Problems.Add($"Linha {lineNo}: nota {rating} fora da escala, ignorada.");
                    else g.PersonalRating = Math.Round(r * 2, MidpointRounding.AwayFromZero) / 2;
                }
                if (ParseNumber(t.Get(row, CMinutes)) is { } min) g.PlaytimeMinutes = Math.Max(g.PlaytimeMinutes, (long)min);
                else if (ParseNumber(t.Get(row, CHours)) is { } h) g.PlatformPlaytimeMinutes = Math.Max(g.PlatformPlaytimeMinutes, (long)Math.Round(h * 60));
                if (ParseNumber(t.Get(row, CPlatformMinutes)) is { } pm) g.PlatformPlaytimeMinutes = Math.Max(g.PlatformPlaytimeMinutes, (long)pm);
                if (ParseDay(t.Get(row, CStarted)) is { } sd) g.StartedAt = sd;
                if (ParseDay(t.Get(row, CFinished)) is { } fd) g.FinishedAt = fd;

                if (existing is null)
                {
                    if (t.Get(row, CSource) is { } src) g.Source = src;
                    g.Platforms = Fmt(t.Get(row, CPlatforms));
                    g.Genres = Fmt(t.Get(row, CGenres));
                    g.Tags = Fmt(t.Get(row, CTags));
                    g.Developer = t.Get(row, CDeveloper);
                    g.Publisher = t.Get(row, CPublisher);
                    g.ReleaseDate = DateParsing.ToIso(t.Get(row, CRelease));
                    // Caminhos são só texto: nada é executado na importação.
                    g.ExecutablePath = t.Get(row, CExe);
                    g.LaunchArguments = t.Get(row, CArgs);
                    g.WorkingDirectory = t.Get(row, CWorkDir);
                    if (t.Get(row, CLaunchUri) is { } uri && GameHub.Core.Launching.GameLauncher.IsAllowedLaunchUri(uri)) g.LaunchUri = uri;
                }
                if (ParseNumber(t.Get(row, CHltbMain)) is { } hm) g.HltbMainMinutes = (long)hm;
                if (ParseNumber(t.Get(row, CHltbExtra)) is { } he) g.HltbExtraMinutes = (long)he;
                if (ParseNumber(t.Get(row, CHltbComplete)) is { } hc) g.HltbCompleteMinutes = (long)hc;

                if (existing is null)
                {
                    _games.Add(g);
                    idx.TryAdd(TitleNormalizer.Normalize(g.Title), g);
                    idx.TryAdd(g.Title.Trim().ToLowerInvariant(), g);
                    result.Added++;
                }
                else
                {
                    _games.Update(g);
                    result.Updated++;
                }

                // Planilhas de diário costumam trazer a review na mesma linha.
                if (t.Get(row, CReview) is { } text && !t.Has("pontos positivos"))
                {
                    var rv = _reviews.Get(g.Id) ?? new GameReview { GameId = g.Id };
                    rv.Review = text;
                    _reviews.Save(rv);
                }
            }
            catch (Exception ex)
            {
                result.Problems.Add($"Linha {lineNo} ({title}): {ex.Message}");
                result.Skipped++;
            }
        }
        return result;
    }

    public ImportResult ImportReviews(string csvText)
    {
        var t = new CsvTable(csvText);
        if (!t.Has(CTitle)) throw new InvalidDataException("O arquivo não tem a coluna \"Jogo\".");
        var result = new ImportResult();
        var idx = TitleIndex();
        for (var line = 0; line < t.Rows.Count; line++)
        {
            var row = t.Rows[line];
            var title = t.Get(row, CTitle);
            if (title is null) { result.Skipped++; continue; }
            var g = Find(idx, title);
            if (g is null)
            {
                result.Problems.Add($"Linha {line + 2}: \"{title}\" não está na biblioteca (importe os jogos antes).");
                result.Skipped++;
                continue;
            }
            var rv = _reviews.Get(g.Id);
            var isNew = rv is null;
            rv ??= new GameReview { GameId = g.Id };
            rv.Review = t.Get(row, CReview) ?? rv.Review;
            rv.Pros = t.Get(row, CPros) ?? rv.Pros;
            rv.Cons = t.Get(row, CCons) ?? rv.Cons;
            rv.Moments = t.Get(row, CMoments) ?? rv.Moments;
            rv.Comments = t.Get(row, CComments) ?? rv.Comments;
            var changedGame = false;
            if (ParseNumber(t.Get(row, CRating)) is { } r and >= 0 and <= 10) { g.PersonalRating = r; changedGame = true; }
            if (ParseStatus(t.Get(row, CStatus)) is { } st) { g.Status = st; changedGame = true; }
            if (changedGame) _games.Update(g);
            if (rv.IsEmpty) { result.Skipped++; continue; }
            _reviews.Save(rv);
            if (isNew) result.Added++; else result.Updated++;
        }
        return result;
    }

    public ImportResult ImportJourney(string csvText)
    {
        var t = new CsvTable(csvText);
        if (!t.Has("tipo") || !t.Has(CTitle)) throw new InvalidDataException("Este não parece um arquivo de jornada do GameHub (faltam as colunas \"Tipo\" e \"Jogo\").");
        var result = new ImportResult();
        var idx = TitleIndex();
        var memoryKeys = _memories.All().Select(MemoryKey).ToHashSet();
        for (var line = 0; line < t.Rows.Count; line++)
        {
            var row = t.Rows[line];
            var lineNo = line + 2;
            var title = t.Get(row, CTitle);
            var kind = t.Get(row, "tipo")?.ToLowerInvariant();
            var g = title is null ? null : Find(idx, title);
            if (g is null)
            {
                if (title is not null) result.Problems.Add($"Linha {lineNo}: \"{title}\" não está na biblioteca.");
                result.Skipped++;
                continue;
            }
            try
            {
                switch (kind)
                {
                    case "inicio" or "conclusao":
                    {
                        var day = ParseDay(t.Get(row, "data"));
                        if (day is null) { result.Skipped++; break; }
                        var current = kind == "inicio" ? g.StartedAt : g.FinishedAt;
                        if (current == day) { result.Skipped++; break; }
                        if (kind == "inicio") g.StartedAt = day; else g.FinishedAt = day;
                        _games.Update(g);
                        result.Updated++;
                        break;
                    }
                    case "sessao":
                    {
                        if (!DateTimeOffset.TryParse(t.Get(row, "data"), Inv, DateTimeStyles.AssumeUniversal, out var start)
                            || !DateTimeOffset.TryParse(t.Get(row, "fim"), Inv, DateTimeStyles.AssumeUniversal, out var end))
                        {
                            result.Problems.Add($"Linha {lineNo}: sessão com data inválida.");
                            result.Skipped++;
                            break;
                        }
                        var minutes = (long)(ParseNumber(t.Get(row, "minutos")) ?? Math.Floor((end - start).TotalMinutes));
                        if (_games.ImportSession(g.Id, start, end, minutes)) { g.PlaytimeMinutes = _games.Get(g.Id)!.PlaytimeMinutes; result.Added++; }
                        else result.Skipped++;
                        break;
                    }
                    case "memoria":
                    {
                        var m = new GameMemory
                        {
                            GameId = g.Id,
                            Kind = Enum.TryParse<MemoryKind>(t.Get(row, "categoria"), true, out var mk) ? mk : MemoryKind.Text,
                            HappenedAt = ParseDay(t.Get(row, "data")),
                            Title = t.Get(row, "titulo"),
                            Text = t.Get(row, "texto"),
                            ImagePath = t.Get(row, "imagem") is { } img && File.Exists(img) ? img : null,
                        };
                        if (!memoryKeys.Add(MemoryKey(m))) { result.Skipped++; break; }
                        _memories.Add(m);
                        result.Added++;
                        break;
                    }
                    default:
                        result.Problems.Add($"Linha {lineNo}: tipo \"{kind}\" desconhecido.");
                        result.Skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Problems.Add($"Linha {lineNo}: {ex.Message}");
                result.Skipped++;
            }
        }
        return result;
    }

    private static string MemoryKey(GameMemory m) => $"{m.GameId}|{m.Kind}|{m.HappenedAt}|{m.Title}|{m.Text}";

    public ImportResult ImportRankings(string csvText)
    {
        var t = new CsvTable(csvText);
        if (!t.Has("ranking")) throw new InvalidDataException("O arquivo não tem a coluna \"Ranking\".");
        var result = new ImportResult();
        var idx = TitleIndex();
        var rankings = _rankings.All();
        var rows = t.Rows.Select((row, i) => (row, line: i + 2))
            .Where(x => t.Get(x.row, "ranking") is not null)
            .GroupBy(x => t.Get(x.row, "ranking")!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows)
        {
            var first = group.First().row;
            var r = rankings.FirstOrDefault(x => string.Equals(x.Name, group.Key, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                var id = _rankings.Create(group.Key, t.Get(first, "descricao"), (int?)ParseNumber(t.Get(first, "limite")));
                r = _rankings.Get(id)!;
                result.Added++;
            }
            if (ParseNumber(t.Get(first, "ano")) is { } year && r.Year is null) { r.Year = (int)year; _rankings.Update(r); }

            var ordered = group
                .Select(x => (x.line, title: t.Get(x.row, CTitle), pos: ParseNumber(t.Get(x.row, "posicao")) ?? double.MaxValue))
                .Where(x => x.title is not null)
                .OrderBy(x => x.pos);
            var ids = _rankings.Entries(r.Id).Select(e => e.Game.Id).ToList();
            var imported = new List<long>();
            foreach (var (line, title, _) in ordered)
            {
                var g = Find(idx, title!);
                if (g is null) { result.Problems.Add($"Linha {line}: \"{title}\" não está na biblioteca."); result.Skipped++; continue; }
                if (!imported.Contains(g.Id)) imported.Add(g.Id);
            }
            if (imported.Count == 0) continue;
            // A ordem do arquivo vale para os jogos dele; jogos que já estavam no ranking e não aparecem no arquivo ficam no fim.
            var final = imported.Concat(ids.Where(id => !imported.Contains(id))).ToList();
            if (r.MaxItems is { } max && final.Count > max)
            {
                result.Problems.Add($"\"{r.Name}\" aceita {max} jogos; {final.Count - max} ficaram de fora.");
                final = final.Take(max).ToList();
            }
            _rankings.SetOrder(r.Id, final);
            result.Updated++;
        }
        return result;
    }

    // =============== Conversões ===============

    private static bool LooksLikeFiveStars(CsvTable t)
    {
        var values = t.Rows.Select(r => ParseNumber(t.Get(r, CRating))).OfType<double>().ToList();
        // Exportações do Backloggd usam colunas em inglês e notas de 0,5 a 5.
        return values.Count > 0 && values.All(v => v <= 5) && t.Has("game", "rating") && !t.Has("jogo", "nota");
    }

    private static List<string> Fmt(string? list) =>
        (list ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsTrue(string? v) => v is not null &&
        new[] { "sim", "s", "yes", "y", "true", "1", "x", "♥" }.Contains(v.Trim().ToLowerInvariant());

    public static double? ParseNumber(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().Replace(',', '.');
        return double.TryParse(v, NumberStyles.Float, Inv, out var d) ? d : null;
    }

    /// <summary>Aceita yyyy-MM-dd, dd/MM/yyyy, datas por extenso (pt/en) e data-hora ISO.</summary>
    public static string? ParseDay(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        if (DateTime.TryParseExact(v, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd" }, Inv, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd", Inv);
        if (DateTimeOffset.TryParse(v, Inv, DateTimeStyles.AssumeUniversal, out var dto) && v.Contains('T'))
            return dto.ToString("yyyy-MM-dd", Inv);
        var iso = DateParsing.ToIso(v);
        return iso is { Length: 10 } ? iso : null;
    }

    public static GameStatus? ParseStatus(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var k = TitleNormalizer.StripAccents(v).Trim().ToLowerInvariant();
        return k switch
        {
            "sem status" or "none" => GameStatus.None,
            "backlog" or "backlogged" or "quero jogar" => GameStatus.Backlog,
            "jogando" or "playing" => GameStatus.Playing,
            "zerado" or "completed" or "beaten" or "finished" or "terminado" or "concluido" or "retired" => GameStatus.Beaten,
            "100%" or "100" or "mastered" or "completionist" or "platinado" => GameStatus.Completed100,
            "abandonado" or "abandoned" or "dropped" => GameStatus.Abandoned,
            "pausado" or "shelved" or "paused" or "on hold" => GameStatus.Paused,
            "played" or "jogado" => null, // jogou, mas sem informar se terminou: mantém o status atual
            _ => null,
        };
    }
}
