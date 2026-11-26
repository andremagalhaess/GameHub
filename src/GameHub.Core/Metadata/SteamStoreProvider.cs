using System.Text.Json;
using GameHub.Core.Text;

namespace GameHub.Core.Metadata;

/// <summary>
/// Metadados pela API pública da loja Steam (store.steampowered.com). Não exige chave.
/// Endpoints: /api/storesearch (busca) e /api/appdetails (detalhes).
/// Observação: a API é pública mas não documentada oficialmente; há limite de ~200 requisições a cada 5 minutos.
/// </summary>
public sealed class SteamStoreProvider : IMetadataProvider
{
    public const string ProviderName = "Steam";
    private const string StoreBase = "https://store.steampowered.com";
    private const string CdnBase = "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps";

    private readonly HttpClient _http;
    private readonly Func<bool> _enabled;

    public SteamStoreProvider(HttpClient http, Func<bool>? enabled = null)
    {
        _http = http;
        _enabled = enabled ?? (() => true);
    }

    public string Name => ProviderName;
    public bool IsConfigured => _enabled();

    public async Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"{StoreBase}/api/storesearch/?term={Uri.EscapeDataString(query)}&l=brazilian&cc=BR";
        using var doc = await GetJsonAsync(url, ct);
        var list = new List<GameSearchResult>();
        foreach (var item in doc.RootElement.Arr("items"))
        {
            if (item.Str("type") is { } type && type != "app") continue;
            var id = item.Long("id");
            var name = item.Str("name");
            if (id is null || name is null) continue;
            list.Add(new GameSearchResult
            {
                Provider = ProviderName,
                ProviderId = id.Value.ToString(),
                Name = name,
                CoverUrl = $"{CdnBase}/{id}/library_600x900.jpg",
            });
        }
        return list;
    }

    public async Task<GameMetadata?> GetDetailsAsync(string providerId, CancellationToken ct)
    {
        if (!long.TryParse(providerId, out var appId)) return null;
        var url = $"{StoreBase}/api/appdetails?appids={appId}&l=brazilian&cc=BR";
        using var doc = await GetJsonAsync(url, ct);
        if (doc.RootElement.Prop(appId.ToString()) is not { } entry || !entry.Bool("success") || entry.Prop("data") is not { } d)
            return null;

        var md = new GameMetadata
        {
            Provider = ProviderName,
            ProviderId = providerId,
            StoreId = providerId,
            Name = d.Str("name") ?? "",
            ShortDescription = HtmlText.ToPlain(d.Str("short_description")),
            Description = HtmlText.ToPlain(d.Str("about_the_game") ?? d.Str("detailed_description")),
            Developer = string.Join(", ", d.Arr("developers").Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))),
            Publisher = string.Join(", ", d.Arr("publishers").Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))),
            Genres = d.Arr("genres").Select(g => g.Str("description")).OfType<string>().ToList(),
            Website = d.Str("website"),
        };
        if (md.Developer?.Length == 0) md.Developer = null;
        if (md.Publisher?.Length == 0) md.Publisher = null;
        md.ShortDescription ??= HtmlText.Summarize(md.Description);

        if (d.Prop("platforms") is { } p)
        {
            if (p.Bool("windows")) md.Platforms.Add("PC (Windows)");
            if (p.Bool("mac")) md.Platforms.Add("macOS");
            if (p.Bool("linux")) md.Platforms.Add("Linux / SteamOS");
        }

        if (d.Prop("release_date") is { } rd)
            md.ReleaseDate = DateParsing.ToIso(rd.Str("date"));

        if (d.Prop("metacritic") is { } mc && mc.Dbl("score") is { } score)
        {
            md.UserRating = score;
            md.UserRatingSource = "Metacritic (via Steam)";
        }

        // Classificação indicativa brasileira (ClassInd/DEJUS) quando a Steam informa.
        if (d.Prop("ratings") is { } ratings && ratings.Prop("dejus") is { } dejus && dejus.Str("rating") is { } r)
            md.AgeRating = r.Equals("l", StringComparison.OrdinalIgnoreCase) ? "Livre (ClassInd)" : $"{r} anos (ClassInd)";
        else if (d.Long("required_age") is > 0 and var age)
            md.AgeRating = $"{age}+";

        // Trailer (mp4 quando disponível).
        foreach (var movie in d.Arr("movies"))
        {
            var mp4 = movie.Prop("mp4")?.Str("max") ?? movie.Prop("mp4")?.Str("480");
            var hls = movie.Str("hls_h264");
            if ((mp4 ?? hls) is { } t) { md.TrailerUrl = t; break; }
        }

        // Artes: capa vertical, hero, logo, header e screenshots.
        md.Images.Add(new ImageCandidate($"{CdnBase}/{appId}/library_600x900_2x.jpg", ImageKind.Cover, ProviderName, 1200, 1800));
        md.Images.Add(new ImageCandidate($"{CdnBase}/{appId}/library_600x900.jpg", ImageKind.Cover, ProviderName, 600, 900));
        md.Images.Add(new ImageCandidate($"{CdnBase}/{appId}/library_hero.jpg", ImageKind.Background, ProviderName, 3840, 1240));
        if (d.Str("background_raw") is { } bgRaw) md.Images.Add(new ImageCandidate(bgRaw, ImageKind.Background, ProviderName));
        md.Images.Add(new ImageCandidate($"{CdnBase}/{appId}/logo.png", ImageKind.Logo, ProviderName));
        if (d.Str("header_image") is { } header) md.Images.Add(new ImageCandidate(header, ImageKind.Cover, ProviderName, 460, 215));
        foreach (var s in d.Arr("screenshots").Take(8))
            if (s.Str("path_full") is { } full) md.Images.Add(new ImageCandidate(full, ImageKind.Background, ProviderName, 1920, 1080));

        return md;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        if ((int)resp.StatusCode == 429) throw new MetadataProviderException(ProviderName, "A Steam limitou as requisições. Tente de novo em alguns minutos.");
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}

public sealed class MetadataProviderException : Exception
{
    public string Provider { get; }
    public MetadataProviderException(string provider, string message, Exception? inner = null) : base(message, inner) => Provider = provider;
}
