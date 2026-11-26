using System.Collections.Concurrent;
using GameHub.Core.Models;
using GameHub.Core.Text;

namespace GameHub.Core.Metadata;

/// <summary>
/// Pesquisa em todos os provedores configurados em paralelo. Só roda quando o usuário pede
/// (nada de buscas em segundo plano). Nunca escolhe um resultado sozinho.
/// </summary>
public sealed class MetadataService
{
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly ConcurrentDictionary<(string, string), GameMetadata> _detailsCache = new();
    /// <summary>Quantos resultados de provedores "só nome" (Steam) são complementados com ano/desenvolvedora/descrição.</summary>
    public int EnrichTop { get; init; } = 5;

    public MetadataService(IEnumerable<IMetadataProvider> providers) => _providers = providers.ToList();

    public IEnumerable<IMetadataProvider> ConfiguredProviders => _providers.Where(p => p.IsConfigured);
    public IEnumerable<IMetadataProvider> AllProviders => _providers;

    public async Task<MetadataSearchResponse> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return new MetadataSearchResponse(Array.Empty<GameSearchResult>(), Array.Empty<ProviderError>());

        var active = ConfiguredProviders.ToList();
        var errors = new ConcurrentBag<ProviderError>();
        var tasks = active.Select(async p =>
        {
            try
            {
                var results = await p.SearchAsync(query, ct);
                await EnrichAsync(p, results, ct);
                return results;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add(new ProviderError(p.Name, Describe(ex)));
                return (IReadOnlyList<GameSearchResult>)Array.Empty<GameSearchResult>();
            }
        });
        var all = (await Task.WhenAll(tasks)).SelectMany(r => r).ToList();
        foreach (var r in all) r.Score = TitleNormalizer.Similarity(query, r.Name);
        var ordered = all
            .OrderByDescending(r => Math.Round(r.Score, 2))
            .ThenBy(r => ProviderRank(r.Provider))
            .ToList();
        return new MetadataSearchResponse(ordered, errors.OrderBy(e => e.Provider).ToList());
    }

    public async Task<GameMetadata?> GetDetailsAsync(GameSearchResult result, CancellationToken ct = default)
    {
        if (_detailsCache.TryGetValue((result.Provider, result.ProviderId), out var cached)) return cached;
        var provider = _providers.FirstOrDefault(p => p.Name == result.Provider)
            ?? throw new InvalidOperationException($"Provedor {result.Provider} indisponível.");
        var md = await provider.GetDetailsAsync(result.ProviderId, ct);
        if (md is not null) _detailsCache[(result.Provider, result.ProviderId)] = md;
        return md;
    }

    /// <summary>Completa resultados que vieram só com nome (busca da Steam não traz ano nem desenvolvedora).</summary>
    private async Task EnrichAsync(IMetadataProvider p, IReadOnlyList<GameSearchResult> results, CancellationToken ct)
    {
        var missing = results.Where(r => r.Year is null && r.Developer is null && r.ShortDescription is null).Take(EnrichTop).ToList();
        if (missing.Count == 0) return;
        await Task.WhenAll(missing.Select(async r =>
        {
            try
            {
                var md = await p.GetDetailsAsync(r.ProviderId, ct);
                if (md is null) return;
                _detailsCache[(r.Provider, r.ProviderId)] = md;
                r.Year = md.ReleaseDate is { Length: >= 4 } d && int.TryParse(d[..4], out var y) ? y : null;
                r.Developer = md.Developer;
                r.ShortDescription = HtmlText.Summarize(md.ShortDescription ?? md.Description, 200);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* o resultado continua listado, só sem os extras */ }
        }));
    }

    /// <summary>Preenche um <see cref="Game"/> com os metadados (sem tocar em dados pessoais nem no executável).</summary>
    public static void ApplyTo(GameMetadata md, Game g)
    {
        g.Title = string.IsNullOrWhiteSpace(md.Name) ? g.Title : md.Name;
        g.AlternativeNames = md.AlternativeNames.Count > 0 ? string.Join(", ", md.AlternativeNames.Take(10)) : g.AlternativeNames;
        g.Description = md.Description ?? g.Description;
        g.ShortDescription = md.ShortDescription ?? g.ShortDescription;
        g.Developer = md.Developer ?? g.Developer;
        g.Publisher = md.Publisher ?? g.Publisher;
        if (md.Genres.Count > 0) g.Genres = md.Genres.ToList();
        if (md.Platforms.Count > 0) g.Platforms = md.Platforms.ToList();
        g.ReleaseDate = md.ReleaseDate ?? g.ReleaseDate;
        g.AgeRating = md.AgeRating ?? g.AgeRating;
        g.Website = md.Website ?? g.Website;
        g.TrailerUrl = md.TrailerUrl ?? g.TrailerUrl;
        if (md.UserRating is not null) { g.ExternalRating = md.UserRating; g.ExternalRatingSource = md.UserRatingSource; }
        g.MetadataProvider = md.Provider;
        g.MetadataId = md.ProviderId;
    }

    private static int ProviderRank(string provider) => provider switch
    {
        IgdbProvider.ProviderName => 0,
        RawgProvider.ProviderName => 1,
        SteamStoreProvider.ProviderName => 2,
        _ => 3,
    };

    private static string Describe(Exception ex) => ex switch
    {
        MetadataProviderException m => m.Message,
        HttpRequestException { StatusCode: null } => "Sem conexão com a internet ou serviço fora do ar.",
        HttpRequestException h => $"O serviço respondeu com erro ({(int)h.StatusCode!}).",
        TaskCanceledException => "O serviço demorou demais para responder.",
        System.Text.Json.JsonException => "Resposta inesperada do serviço.",
        _ => ex.Message,
    };
}
