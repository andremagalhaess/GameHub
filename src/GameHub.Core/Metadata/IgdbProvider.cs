using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameHub.Core.Text;

namespace GameHub.Core.Metadata;

/// <summary>
/// IGDB (https://api-docs.igdb.com). Exige um aplicativo registrado no Twitch Developer Console
/// (Client ID + Client Secret). O token de acesso (client credentials) é obtido e renovado automaticamente
/// e mantido só em memória.
/// </summary>
public sealed class IgdbProvider : IMetadataProvider
{
    public const string ProviderName = "IGDB";
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string ApiBase = "https://api.igdb.com/v4";
    private const string ImageBase = "https://images.igdb.com/igdb/image/upload";

    private readonly HttpClient _http;
    private readonly Func<string?> _clientId;
    private readonly Func<string?> _clientSecret;
    private string? _token;
    private DateTimeOffset _tokenExpires;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public IgdbProvider(HttpClient http, Func<string?> clientId, Func<string?> clientSecret)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public string Name => ProviderName;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId()) && !string.IsNullOrWhiteSpace(_clientSecret());

    private const string SearchFields =
        "fields name,first_release_date,cover.image_id,summary,involved_companies.company.name,involved_companies.developer;";

    public async Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        var body = $"search \"{EscapeApicalypse(query)}\"; {SearchFields} limit 10;";
        using var doc = await PostAsync("/games", body, ct);
        var list = new List<GameSearchResult>();
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            var id = g.Long("id");
            var name = g.Str("name");
            if (id is null || name is null) continue;
            list.Add(new GameSearchResult
            {
                Provider = ProviderName,
                ProviderId = id.Value.ToString(),
                Name = name,
                Year = g.Long("first_release_date") is { } ts ? DateTimeOffset.FromUnixTimeSeconds(ts).Year : null,
                CoverUrl = g.Prop("cover")?.Str("image_id") is { } img ? $"{ImageBase}/t_cover_big/{img}.jpg" : null,
                Developer = g.Arr("involved_companies").Where(c => c.Bool("developer")).Select(c => c.Prop("company")?.Str("name")).OfType<string>().FirstOrDefault(),
                ShortDescription = HtmlText.Summarize(g.Str("summary"), 200),
            });
        }
        return list;
    }

    public async Task<GameMetadata?> GetDetailsAsync(string providerId, CancellationToken ct)
    {
        if (!long.TryParse(providerId, out var id)) return null;
        var body = "fields name,alternative_names.name,summary,storyline,first_release_date,genres.name,platforms.name," +
                   "involved_companies.company.name,involved_companies.developer,involved_companies.publisher,total_rating," +
                   "cover.image_id,artworks.image_id,screenshots.image_id,videos.video_id,websites.url,websites.type,url; " +
                   $"where id = {id};";
        using var doc = await PostAsync("/games", body, ct);
        var g = doc.RootElement.EnumerateArray().FirstOrDefault();
        if (g.ValueKind != JsonValueKind.Object) return null;

        var companies = g.Arr("involved_companies").ToList();
        string? Companies(string role)
        {
            var s = string.Join(", ", companies.Where(c => c.Bool(role)).Select(c => c.Prop("company")?.Str("name")).OfType<string>().Distinct());
            return s.Length == 0 ? null : s;
        }

        var md = new GameMetadata
        {
            Provider = ProviderName,
            ProviderId = providerId,
            Name = g.Str("name") ?? "",
            AlternativeNames = g.Arr("alternative_names").Select(a => a.Str("name")).OfType<string>().ToList(),
            Description = string.Join("\n\n", new[] { g.Str("summary"), g.Str("storyline") }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Developer = Companies("developer"),
            Publisher = Companies("publisher"),
            Genres = g.Arr("genres").Select(x => x.Str("name")).OfType<string>().ToList(),
            Platforms = g.Arr("platforms").Select(x => x.Str("name")).OfType<string>().ToList(),
            ReleaseDate = g.Long("first_release_date") is { } ts ? DateParsing.FromUnixSeconds(ts) : null,
        };
        if (string.IsNullOrEmpty(md.Description)) md.Description = null;
        md.ShortDescription = HtmlText.Summarize(g.Str("summary"));
        if (g.Dbl("total_rating") is { } tr) { md.UserRating = Math.Round(tr); md.UserRatingSource = "IGDB"; }
        // websites.type 1 = site oficial
        md.Website = g.Arr("websites").Where(w => w.Long("type") == 1).Select(w => w.Str("url")).FirstOrDefault();
        if (g.Arr("videos").Select(v => v.Str("video_id")).FirstOrDefault() is { } yt) md.TrailerUrl = $"https://www.youtube.com/watch?v={yt}";

        if (g.Prop("cover")?.Str("image_id") is { } cover)
            md.Images.Add(new ImageCandidate($"{ImageBase}/t_cover_big_2x/{cover}.jpg", ImageKind.Cover, ProviderName, 528, 748));
        foreach (var a in g.Arr("artworks").Take(6))
            if (a.Str("image_id") is { } img) md.Images.Add(new ImageCandidate($"{ImageBase}/t_1080p/{img}.jpg", ImageKind.Background, ProviderName, 1920, 1080));
        foreach (var s in g.Arr("screenshots").Take(6))
            if (s.Str("image_id") is { } img) md.Images.Add(new ImageCandidate($"{ImageBase}/t_1080p/{img}.jpg", ImageKind.Screenshot, ProviderName, 1920, 1080));
        return md;
    }

    private static string EscapeApicalypse(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private async Task<JsonDocument> PostAsync(string path, string body, CancellationToken ct, bool retried = false)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
        req.Headers.Add("Client-ID", _clientId());
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            _token = null;
            return await PostAsync(path, body, ct, retried: true);
        }
        if ((int)resp.StatusCode == 429) throw new MetadataProviderException(ProviderName, "O IGDB limitou as requisições (4 por segundo). Tente novamente.");
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;
            var id = _clientId();
            var secret = _clientSecret();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
                throw new MetadataProviderException(ProviderName, "Credenciais do IGDB/Twitch não configuradas.");
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = id, ["client_secret"] = secret, ["grant_type"] = "client_credentials",
            });
            using var resp = await _http.PostAsync(TokenUrl, content, ct);
            if (resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new MetadataProviderException(ProviderName, "O Twitch recusou o Client ID/Secret do IGDB. Confira em Configurações › Integrações.");
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            _token = doc.RootElement.Str("access_token") ?? throw new MetadataProviderException(ProviderName, "Resposta de token inválida.");
            var expires = doc.RootElement.Long("expires_in") ?? 3600;
            _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expires - 300));
            return _token;
        }
        finally { _tokenLock.Release(); }
    }
}
