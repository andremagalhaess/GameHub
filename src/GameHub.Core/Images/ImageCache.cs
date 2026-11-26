using System.Security.Cryptography;
using System.Text;
using GameHub.Core.Metadata;

namespace GameHub.Core.Images;

/// <summary>
/// Baixa e guarda imagens localmente (funcionamento offline). Só baixa o que o usuário escolheu.
/// Arquivos são nomeados pelo hash da URL/conteúdo, então a mesma arte nunca é duplicada.
/// </summary>
public sealed class ImageCache
{
    public const long MaxBytes = 25 * 1024 * 1024;
    private readonly HttpClient _http;
    public string Root { get; }

    public ImageCache(HttpClient http, string root)
    {
        _http = http;
        Root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>Baixa a URL (se ainda não estiver em cache) e retorna o caminho local.</summary>
    public async Task<string> DownloadAsync(string url, ImageKind kind, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("URL de imagem inválida.", nameof(url));

        var dir = Path.Combine(Root, kind.ToString().ToLowerInvariant());
        Directory.CreateDirectory(dir);
        var hash = Hash(Encoding.UTF8.GetBytes(url));
        var existing = Directory.EnumerateFiles(dir, hash + ".*").FirstOrDefault();
        if (existing is not null) return existing;

        using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("Imagem grande demais.");
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > MaxBytes) throw new InvalidDataException("Imagem grande demais.");
        }
        var bytes = ms.ToArray();
        var ext = DetectExtension(bytes) ?? throw new InvalidDataException("O arquivo baixado não é uma imagem suportada.");
        var path = Path.Combine(dir, hash + ext);
        await WriteAtomicAsync(path, bytes, ct);
        return path;
    }

    /// <summary>Copia uma imagem escolhida do disco para o cache (o original pode ser apagado depois).</summary>
    public async Task<string> ImportLocalAsync(string sourcePath, ImageKind kind, CancellationToken ct = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("Imagem não encontrada.", sourcePath);
        if (info.Length > MaxBytes) throw new InvalidDataException("Imagem grande demais (máx. 25 MB).");
        var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
        var ext = DetectExtension(bytes) ?? throw new InvalidDataException("Formato de imagem não suportado. Use PNG, JPG, WEBP, GIF, BMP ou ICO.");
        var dir = Path.Combine(Root, kind.ToString().ToLowerInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Hash(bytes) + ext);
        if (!File.Exists(path)) await WriteAtomicAsync(path, bytes, ct);
        return path;
    }

    /// <summary>Grava bytes já gerados (ex.: ícone extraído do EXE).</summary>
    public async Task<string> StoreAsync(byte[] bytes, ImageKind kind, CancellationToken ct = default)
    {
        var ext = DetectExtension(bytes) ?? throw new InvalidDataException("Formato de imagem não suportado.");
        var dir = Path.Combine(Root, kind.ToString().ToLowerInvariant());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Hash(bytes) + ext);
        if (!File.Exists(path)) await WriteAtomicAsync(path, bytes, ct);
        return path;
    }

    public long SizeBytes() =>
        Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    /// <summary>Apaga imagens em cache que nenhum jogo/perfil usa mais. Retorna bytes liberados.</summary>
    public long RemoveUnused(IEnumerable<string?> referencedPaths)
    {
        var keep = new HashSet<string>(referencedPaths.OfType<string>().Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        long freed = 0;
        foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (keep.Contains(Path.GetFullPath(f))) continue;
            try { var len = new FileInfo(f).Length; File.Delete(f); freed += len; } catch (IOException) { }
        }
        return freed;
    }

    public static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        if (b.Length >= 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ".gif";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return ".bmp";
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return ".ico";
        return null;
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant();

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
    }
}
