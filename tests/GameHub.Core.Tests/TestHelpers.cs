using System.Net;
using System.Text;
using GameHub.Core;
using GameHub.Core.Data;
using GameHub.Core.Security;

namespace GameHub.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gamehub-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch { }
    }
}

public sealed class TestEnv : IDisposable
{
    public TempDir Dir { get; } = new();
    public AppPaths Paths { get; }
    public Database Db { get; }
    public GameRepository Games { get; }
    public TestEnv()
    {
        Paths = new AppPaths(Dir.File("data"));
        Db = new Database(Paths.DatabaseFile);
        Db.Migrate();
        Games = new GameRepository(Db);
    }
    public void Dispose() => Dir.Dispose();
}

/// <summary>Handler HTTP falso: responde de acordo com uma função sobre a requisição.</summary>
public sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        return _respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public HttpClient Client() => new(this);
}

/// <summary>"Criptografia" reversível só para testes (o app usa DPAPI).</summary>
public sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plaintext) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
    public string Unprotect(string protectedText) =>
        protectedText.StartsWith("enc:") ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[4..]))
        : throw new System.Security.Cryptography.CryptographicException("bad");
}

public static class Png
{
    // PNG 1x1 válido.
    public static readonly byte[] OnePixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
}
