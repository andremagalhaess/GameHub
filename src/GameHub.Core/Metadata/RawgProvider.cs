using System.Net;
using System.Text.Json;
using GameHub.Core.Text;

namespace GameHub.Core.Metadata;

/// <summary>
/// RAWG Video Games Database (https://rawg.io/apidocs). Exige chave de API gratuita.
/// Termos da RAWG: exibir a atribuição "dados fornecidos por RAWG" com link onde os dados aparecem.
/// </summary>
public sealed class RawgProvider : IMetadataProvider
{
    public const string ProviderName = "RAWG";
    private const string Base = "https://api.rawg.io/api";
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;

    public RawgProvider(HttpClient http, Func<string?> apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public string Name => ProviderName;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey());

    public async Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        using var doc = await GetAsync($"/games?search={Uri.EscapeDataString(query)}&page_size=10", ct);
        var list = new List<GameSearchResult>();
        foreach (var g in doc.RootElement.Arr("results"))
        {
            var id = g.Long("id");
            var name = g.Str("name");
            if (id is null || name is null) continue;
            list.Add(new GameSearchResult
            {
                Provider = ProviderName,
                ProviderId = id.Value.ToString(),
                Name = name,
                Year = DateParsing.ToIso(g.Str("released")) is { Length: >= 4 } iso ? int.Parse(iso[..4]) : null,
                CoverUrl = g.Str("background_image"),
            });
        }
        return list;
    }

    public async Task<GameMetadata?> GetDetailsAsync(string providerId, CancellationToken ct)
    {
        if (!long.TryParse(providerId, out _)) return null;
        JsonDocument doc;
        try { doc = await GetAsync($"/games/{providerId}", ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
        using (doc)
        {
            var d = doc.RootElement;
            var md = new GameMetadata
            {
                Provider = ProviderName,
                ProviderId = providerId,
                Name = d.Str("name") ?? "",
                AlternativeNames = d.Arr("alternative_names").Select(x => x.GetString()).OfType<string>().ToList(),
                Description = d.Str("description_raw") ?? HtmlText.ToPlain(d.Str("description")),
                Developer = Join(d.Arr("developers")),
                Publisher = Join(d.Arr("publishers")),
                Genres = d.Arr("genres").Select(x => x.Str("name")).OfType<string>().ToList(),
                Platforms = d.Arr("platforms").Select(x => x.Prop("platform")?.Str("name")).OfType<string>().ToList(),
                ReleaseDate = DateParsing.ToIso(d.Str("released")),
                Website = d.Str("website"),
                AgeRating = d.Prop("esrb_rating")?.Str("name") is { } esrb ? $"ESRB {esrb}" : null,
            };
            md.ShortDescription = HtmlText.Summarize(md.Description);
            if (d.Dbl("metacritic") is { } mc and > 0) { md.UserRating = mc; md.UserRatingSource = "Metacritic (via RAWG)"; }
            else if (d.Dbl("rating") is { } r and > 0) { md.UserRating = Math.Round(r * 20); md.UserRatingSource = "Usuários RAWG"; }

            if (d.Str("background_image") is { } bg)
            {
                // A RAWG não tem capa vertical oficial; a imagem principal serve como capa recortada e como fundo.
                md.Images.Add(new ImageCandidate(bg, ImageKind.Background, ProviderName));
                md.Images.Add(new ImageCandidate(bg, ImageKind.Cover, ProviderName));
            }
            if (d.Str("background_image_additional") is { } bg2) md.Images.Add(new ImageCandidate(bg2, ImageKind.Background, ProviderName));

            try
            {
                using var shots = await GetAsync($"/games/{providerId}/screenshots?page_size=8", ct);
                foreach (var s in shots.RootElement.Arr("results"))
                    if (s.Str("image") is { } img) md.Images.Add(new ImageCandidate(img, ImageKind.Screenshot, ProviderName));
            }
            catch (HttpRequestException) { /* screenshots são opcionais */ }

            return md;
        }
    }

    private static string? Join(IEnumerable<JsonElement> items)
    {
        var s = string.Join(", ", items.Select(x => x.Str("name")).OfType<string>());
        return s.Length == 0 ? null : s;
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        var key = _apiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new MetadataProviderException(ProviderName, "Chave da RAWG não configurada.");
        var sep = path.Contains('?') ? '&' : '?';
        using var resp = await _http.GetAsync($"{Base}{path}{sep}key={Uri.EscapeDataString(key)}", ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new MetadataProviderException(ProviderName, "A RAWG recusou a chave de API. Confira em Configurações › Integrações.");
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
