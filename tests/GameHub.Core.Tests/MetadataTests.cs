using System.Net;
using GameHub.Core.Metadata;
using GameHub.Core.Models;

namespace GameHub.Core.Tests;

public class MetadataTests
{
    private const string SteamSearch = """
        {"total":3,"items":[
          {"type":"app","name":"Cyberpunk 2077","id":1091500,"tiny_image":"https://x/capsule_231x87.jpg","metascore":"86","platforms":{"windows":true}},
          {"type":"app","name":"Cyberpunk 2077: Phantom Liberty","id":2138330,"tiny_image":"https://x/a.jpg"},
          {"type":"sub","name":"Pacote","id":999}
        ]}
        """;

    private const string SteamDetails = """
        {"1091500":{"success":true,"data":{
          "type":"game","name":"Cyberpunk 2077","steam_appid":1091500,"required_age":"18",
          "detailed_description":"<p>Detalhada</p>",
          "about_the_game":"<h2>Sobre</h2><p>Cyberpunk 2077 é um RPG de ação e aventura em mundo aberto.</p><br><ul><li>Item 1</li></ul>",
          "short_description":"Cyberpunk 2077 &eacute; um RPG de a&ccedil;&atilde;o.",
          "header_image":"https://cdn/header.jpg","website":"https://www.cyberpunk.net",
          "developers":["CD PROJEKT RED"],"publishers":["CD PROJEKT RED"],
          "platforms":{"windows":true,"mac":false,"linux":false},
          "metacritic":{"score":86,"url":"https://mc"},
          "genres":[{"id":"1","description":"Ação"},{"id":"3","description":"RPG"}],
          "screenshots":[{"id":0,"path_thumbnail":"https://cdn/s0t.jpg","path_full":"https://cdn/s0.jpg"}],
          "movies":[{"id":1,"name":"Trailer","mp4":{"480":"https://v/480.mp4","max":"https://v/max.mp4"}}],
          "release_date":{"coming_soon":false,"date":"9 dez. 2020"},
          "background_raw":"https://cdn/page_bg_raw.jpg",
          "ratings":{"dejus":{"rating_generated":"1","rating":"18","required_age":"18"}}
        }}}
        """;

    private static HttpClient SteamHttp(out FakeHttp fake)
    {
        fake = new FakeHttp(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/storesearch/" => FakeHttp.Json(SteamSearch),
            "/api/appdetails" when req.RequestUri.Query.Contains("1091500") => FakeHttp.Json(SteamDetails),
            "/api/appdetails" => FakeHttp.Json("""{"2138330":{"success":false}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        return fake.Client();
    }

    [Fact]
    public async Task Steam_search_returns_only_apps()
    {
        var p = new SteamStoreProvider(SteamHttp(out var fake));
        var r = await p.SearchAsync("Cyberpunk 2077", default);
        Assert.Equal(2, r.Count);
        Assert.Equal("1091500", r[0].ProviderId);
        Assert.Contains("term=Cyberpunk%202077", fake.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Steam_details_are_parsed()
    {
        var md = (await new SteamStoreProvider(SteamHttp(out _)).GetDetailsAsync("1091500", default))!;
        Assert.Equal("Cyberpunk 2077", md.Name);
        Assert.Equal("CD PROJEKT RED", md.Developer);
        Assert.Equal(new[] { "Ação", "RPG" }, md.Genres);
        Assert.Equal("2020-12-09", md.ReleaseDate);
        Assert.Equal("18 anos (ClassInd)", md.AgeRating);
        Assert.Equal(86, md.UserRating);
        Assert.Equal("https://v/max.mp4", md.TrailerUrl);
        Assert.Equal("Cyberpunk 2077 é um RPG de ação.", md.ShortDescription);
        Assert.StartsWith("Sobre\nCyberpunk 2077 é um RPG", md.Description);
        Assert.Contains(md.Images, i => i.Kind == ImageKind.Cover && i.Url.EndsWith("/1091500/library_600x900_2x.jpg"));
        Assert.Contains(md.Images, i => i.Kind == ImageKind.Logo);
        Assert.Contains(md.Images, i => i.Kind == ImageKind.Background && i.Url == "https://cdn/s0.jpg");
        Assert.Equal(new[] { "PC (Windows)" }, md.Platforms);
    }

    [Fact]
    public async Task Steam_unsuccessful_details_return_null() =>
        Assert.Null(await new SteamStoreProvider(SteamHttp(out _)).GetDetailsAsync("2138330", default));

    [Fact]
    public async Task Rawg_requires_key_and_parses()
    {
        var fake = new FakeHttp(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (!req.RequestUri.Query.Contains("key=K")) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return path switch
            {
                "/api/games" => FakeHttp.Json("""{"results":[{"id":41494,"name":"Cyberpunk 2077","released":"2020-12-10","background_image":"https://m/bg.jpg"}]}"""),
                "/api/games/41494" => FakeHttp.Json("""
                    {"id":41494,"name":"Cyberpunk 2077","description_raw":"Texto longo.","released":"2020-12-10","website":"https://cp",
                     "developers":[{"name":"CD PROJEKT RED"}],"publishers":[{"name":"CD PROJEKT"}],"genres":[{"name":"RPG"}],
                     "platforms":[{"platform":{"name":"PC"}}],"esrb_rating":{"name":"Mature"},"metacritic":86,"rating":4.2,
                     "alternative_names":["CP2077"],"background_image":"https://m/bg.jpg"}
                    """),
                "/api/games/41494/screenshots" => FakeHttp.Json("""{"results":[{"image":"https://m/s1.jpg"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        string? key = null;
        var p = new RawgProvider(fake.Client(), () => key);
        Assert.False(p.IsConfigured);
        key = "K";
        Assert.True(p.IsConfigured);
        var r = Assert.Single(await p.SearchAsync("cyberpunk", default));
        Assert.Equal(2020, r.Year);
        var md = (await p.GetDetailsAsync("41494", default))!;
        Assert.Equal("CD PROJEKT", md.Publisher);
        Assert.Equal("ESRB Mature", md.AgeRating);
        Assert.Equal(new[] { "CP2077" }, md.AlternativeNames);
        Assert.Contains(md.Images, i => i.Kind == ImageKind.Screenshot);

        key = "errada";
        var ex = await Assert.ThrowsAsync<MetadataProviderException>(() => p.SearchAsync("x", default));
        Assert.Contains("recusou", ex.Message);
    }

    [Fact]
    public async Task Igdb_gets_token_then_queries_with_headers()
    {
        var fake = new FakeHttp(req => req.RequestUri!.Host switch
        {
            "id.twitch.tv" => FakeHttp.Json("""{"access_token":"TOKEN","expires_in":5000000,"token_type":"bearer"}"""),
            "api.igdb.com" => FakeHttp.Json("""
                [{"id":1877,"name":"Cyberpunk 2077","first_release_date":1607558400,"summary":"Resumo.","storyline":"História.",
                  "cover":{"id":1,"image_id":"co1abc"},"artworks":[{"id":2,"image_id":"ar1"}],
                  "genres":[{"id":12,"name":"Role-playing (RPG)"}],"platforms":[{"id":6,"name":"PC (Microsoft Windows)"}],
                  "involved_companies":[{"company":{"name":"CD PROJEKT RED"},"developer":true,"publisher":false},{"company":{"name":"CD PROJEKT"},"developer":false,"publisher":true}],
                  "total_rating":78.6,"videos":[{"video_id":"abc123"}],"websites":[{"url":"https://cyberpunk.net","type":1},{"url":"https://x","type":13}]}]
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var p = new IgdbProvider(fake.Client(), () => "CID", () => "SECRET");
        var r = Assert.Single(await p.SearchAsync("Cyber \"punk\"", default));
        Assert.Equal(2020, r.Year);
        Assert.Equal("CD PROJEKT RED", r.Developer);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg", r.CoverUrl);

        var md = (await p.GetDetailsAsync("1877", default))!;
        Assert.Equal("2020-12-10", md.ReleaseDate);
        Assert.Equal("CD PROJEKT", md.Publisher);
        Assert.Equal("https://cyberpunk.net", md.Website);
        Assert.Equal("https://www.youtube.com/watch?v=abc123", md.TrailerUrl);
        Assert.Equal(79, md.UserRating);
        Assert.Equal("Resumo.\n\nHistória.", md.Description);

        Assert.Equal(1, fake.Requests.Count(x => x.RequestUri!.Host == "id.twitch.tv")); // token reaproveitado
        var api = fake.Requests.First(x => x.RequestUri!.Host == "api.igdb.com");
        Assert.Equal("CID", api.Headers.GetValues("Client-ID").Single());
        Assert.Equal("Bearer TOKEN", api.Headers.Authorization!.ToString());
        Assert.Contains("search \"Cyber \\\"punk\\\"\";", fake.Bodies[1]);
    }

    private sealed class FailingProvider : IMetadataProvider
    {
        public string Name => "Falha";
        public bool IsConfigured => true;
        public Task<IReadOnlyList<GameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new HttpRequestException("offline");
        public Task<GameMetadata?> GetDetailsAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnconfiguredProvider : IMetadataProvider
    {
        public string Name => "SemChave";
        public bool IsConfigured => false;
        public Task<IReadOnlyList<GameSearchResult>> SearchAsync(string q, CancellationToken ct) => throw new InvalidOperationException("não deveria ser chamado");
        public Task<GameMetadata?> GetDetailsAsync(string id, CancellationToken ct) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Service_aggregates_enriches_ranks_and_reports_errors()
    {
        var svc = new MetadataService(new IMetadataProvider[] { new SteamStoreProvider(SteamHttp(out _)), new FailingProvider(), new UnconfiguredProvider() });
        var resp = await svc.SearchAsync("cyberpunk 2077");

        Assert.Equal(2, resp.Results.Count);
        var top = resp.Results[0];
        Assert.Equal("Cyberpunk 2077", top.Name);
        Assert.Equal(2020, top.Year);                 // complementado via appdetails
        Assert.Equal("CD PROJEKT RED", top.Developer);
        Assert.True(top.Score > resp.Results[1].Score);
        var err = Assert.Single(resp.Errors);
        Assert.Equal("Falha", err.Provider);
        Assert.Contains("Sem conexão", err.Message);
    }

    [Fact]
    public async Task Service_with_no_network_returns_errors_not_fake_results()
    {
        var offline = new FakeHttp(_ => throw new HttpRequestException("No such host"));
        var svc = new MetadataService(new IMetadataProvider[] { new SteamStoreProvider(offline.Client()) });
        var resp = await svc.SearchAsync("Hades");
        Assert.Empty(resp.Results);
        Assert.Single(resp.Errors);
    }

    [Fact]
    public void ApplyTo_fills_metadata_but_keeps_personal_data_and_exe()
    {
        var g = new Game { Title = "cyberpunk", ExecutablePath = @"C:\a.exe", Status = GameStatus.Playing, PersonalRating = 8, PlaytimeMinutes = 120 };
        var md = new GameMetadata { Provider = "Steam", ProviderId = "1091500", Name = "Cyberpunk 2077", Developer = "CDPR", Genres = new() { "RPG" }, UserRating = 86, UserRatingSource = "Metacritic" };
        MetadataService.ApplyTo(md, g);
        Assert.Equal("Cyberpunk 2077", g.Title);
        Assert.Equal("CDPR", g.Developer);
        Assert.Equal(@"C:\a.exe", g.ExecutablePath);
        Assert.Equal(GameStatus.Playing, g.Status);
        Assert.Equal(8, g.PersonalRating);
        Assert.Equal(120, g.PlaytimeMinutes);
        Assert.Equal("1091500", g.MetadataId);
    }
}
