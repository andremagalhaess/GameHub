using GameHub.Core.Data;
using GameHub.Core.Models;
using GameHub.Core.Text;

namespace GameHub.Core.Platforms;

public sealed class ImportReport
{
    public int Added { get; set; }
    public int Linked { get; set; }
    public int Updated { get; set; }
    public int Ignored { get; set; }
    public int MarkedUninstalled { get; set; }
    public List<string> AddedTitles { get; } = new();
    public List<(string Platform, string Message)> Problems { get; } = new();
    public List<string> PlatformsFound { get; } = new();
}

/// <summary>
/// Junta as detecções de várias plataformas na biblioteca, sem duplicar:
/// 1) mesmo vínculo (plataforma + ID) → atualiza; 2) mesmo executável ou mesmo nome normalizado → associa
/// à entrada existente; 3) senão cria um jogo novo. Jogos removidos pelo usuário não voltam.
/// Só roda quando o usuário pede ("Atualizar biblioteca").
/// </summary>
public sealed class LibraryImportService
{
    private readonly GameRepository _games;
    private readonly GameSourceRepository _sources;
    private readonly IReadOnlyList<IPlatformScanner> _scanners;

    public LibraryImportService(GameRepository games, GameSourceRepository sources, IEnumerable<IPlatformScanner> scanners)
    {
        _games = games;
        _sources = sources;
        _scanners = scanners.ToList();
    }

    public IReadOnlyList<IPlatformScanner> Scanners => _scanners;

    public async Task<ImportReport> ImportAsync(Func<IPlatformScanner, bool>? enabled = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<PlatformScanResult>();
        foreach (var s in _scanners.Where(s => enabled?.Invoke(s) ?? true))
        {
            progress?.Report($"Procurando jogos: {s.Platform}…");
            try { results.Add(await s.ScanAsync(ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { results.Add(new PlatformScanResult(s.Platform, Array.Empty<DetectedGame>(), ex.Message)); }
        }
        progress?.Report("Atualizando a biblioteca…");
        return Apply(results);
    }

    /// <summary>Aplica resultados de varredura (separado para testes).</summary>
    public ImportReport Apply(IEnumerable<PlatformScanResult> results)
    {
        var report = new ImportReport();
        var list = results.ToList();
        foreach (var r in list)
        {
            if (r.Error is not null && r.Available) report.Problems.Add((r.Platform, r.Error));
            if (r.Available && r.Games.Count > 0) report.PlatformsFound.Add(r.Platform);
        }

        // Combina detecções da mesma entrada (ex.: Steam local + Steam Web API).
        var merged = list.SelectMany(r => r.Games)
            .GroupBy(d => (d.Source, d.SourceId))
            .Select(g => g.Aggregate((a, b) => a with
            {
                InstallDir = a.InstallDir ?? b.InstallDir,
                ExecutablePath = a.ExecutablePath ?? b.ExecutablePath,
                LaunchUri = a.LaunchUri ?? b.LaunchUri,
                LaunchArguments = a.LaunchArguments ?? b.LaunchArguments,
                IsInstalled = a.IsInstalled || b.IsInstalled,
                PlaytimeMinutes = Max(a.PlaytimeMinutes, b.PlaytimeMinutes),
                LastPlayedAt = Max(a.LastPlayedAt, b.LastPlayedAt),
            }))
            .ToList();

        var existing = _games.All();
        var byTitle = existing.GroupBy(g => TitleNormalizer.Normalize(g.Title)).ToDictionary(g => g.Key, g => g.First());

        foreach (var d in merged)
        {
            if (_sources.IsIgnored(d.Source, d.SourceId)) { report.Ignored++; continue; }

            var link = _sources.Get(d.Source, d.SourceId);
            Game? game = link is not null ? _games.Get(link.GameId) : null;
            if (game is not null)
            {
                report.Updated++;
            }
            else
            {
                game = (d.ExecutablePath is not null
                        ? existing.FirstOrDefault(g => string.Equals(g.ExecutablePath, GameRepository.NormalizePath(d.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                        : null)
                    ?? byTitle.GetValueOrDefault(TitleNormalizer.Normalize(d.Title));
                if (game is not null) report.Linked++;
            }

            if (game is null)
            {
                game = new Game
                {
                    Title = d.Title,
                    Source = d.Source,
                    SourceId = d.SourceId,
                    // Lojas que exigem o launcher (Steam, Epic, Ubisoft) iniciam pelo protocolo; as demais pelo EXE.
                    LaunchUri = d.LaunchUri,
                    ExecutablePath = d.LaunchUri is null ? d.ExecutablePath : null,
                    LaunchArguments = d.LaunchUri is null ? d.LaunchArguments : null,
                    PlatformPlaytimeMinutes = d.PlaytimeMinutes ?? 0,
                    LastPlayedAt = d.LastPlayedAt,
                    Platforms = new() { "PC (Windows)" },
                };
                _games.Add(game);
                existing.Add(game);
                byTitle.TryAdd(TitleNormalizer.Normalize(game.Title), game);
                report.Added++;
                report.AddedTitles.Add(game.Title);
            }

            _sources.Upsert(new GameSourceLink
            {
                GameId = game.Id,
                Source = d.Source,
                SourceId = d.SourceId,
                InstallDir = d.InstallDir,
                ExecutablePath = d.ExecutablePath,
                LaunchUri = d.LaunchUri,
                PlaytimeMinutes = d.PlaytimeMinutes,
                LastPlayedAt = d.LastPlayedAt,
                IsInstalled = d.IsInstalled,
            });
            RefreshFromLinks(game);
        }

        // Jogos que sumiram de uma plataforma varrida com sucesso: marcados como não instalados (nunca apagados).
        foreach (var r in list.Where(r => r.Available && r.Error is null))
        {
            var seen = r.Games.Select(g => (g.Source, g.SourceId)).ToHashSet();
            foreach (var source in r.Games.Select(g => g.Source).Append(r.Platform).Distinct())
            foreach (var l in _sources.ForSource(source).Where(l => l.IsInstalled && !seen.Contains((l.Source, l.SourceId))))
            {
                if (r.Platform == GameSources.Steam && merged.Any(m => m.Source == l.Source && m.SourceId == l.SourceId)) continue;
                l.IsInstalled = false;
                _sources.Upsert(l);
                report.MarkedUninstalled++;
            }
        }
        return report;
    }

    /// <summary>Atualiza horas da plataforma e forma de iniciar a partir dos vínculos do jogo.</summary>
    private void RefreshFromLinks(Game game)
    {
        var links = _sources.ForGame(game.Id);
        var changed = false;
        var platformMinutes = links.Max(l => l.PlaytimeMinutes ?? 0);
        if (platformMinutes != game.PlatformPlaytimeMinutes) { game.PlatformPlaytimeMinutes = platformMinutes; changed = true; }
        var lastPlayed = links.Select(l => l.LastPlayedAt).Append(game.LastPlayedAt).Max();
        if (lastPlayed != game.LastPlayedAt) { game.LastPlayedAt = lastPlayed; changed = true; }
        if (!game.CanLaunch && links.FirstOrDefault(l => l.IsInstalled && (l.LaunchUri ?? l.ExecutablePath) is not null) is { } best)
        {
            game.LaunchUri = best.LaunchUri;
            game.ExecutablePath = best.LaunchUri is null ? best.ExecutablePath : null;
            changed = true;
        }
        if (changed) _games.Update(game);
    }

    private static long? Max(long? a, long? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : (a > b ? a : b);
}
