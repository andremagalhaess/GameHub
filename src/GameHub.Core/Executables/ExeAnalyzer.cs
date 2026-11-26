using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GameHub.Core.Executables;

/// <summary>Informações lidas do recurso de versão de um executável.</summary>
public sealed record ExeVersionInfo(
    string? ProductName,
    string? FileDescription,
    string? CompanyName,
    string? OriginalFilename,
    string? FileVersion,
    string? ProductVersion);

public interface IExeVersionReader
{
    ExeVersionInfo Read(string path);
}

/// <summary>Lê o recurso VERSIONINFO via <see cref="FileVersionInfo"/> (completo no Windows).</summary>
public sealed class FileVersionInfoReader : IExeVersionReader
{
    public ExeVersionInfo Read(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            return new ExeVersionInfo(v.ProductName, v.FileDescription, v.CompanyName, v.OriginalFilename, v.FileVersion, v.ProductVersion);
        }
        catch (Exception)
        {
            return new ExeVersionInfo(null, null, null, null, null, null);
        }
    }
}

/// <summary>Resultado da análise: dados brutos + nome sugerido (sempre editável na interface).</summary>
public sealed record ExeAnalysis(
    string Path,
    string FileName,
    string FolderName,
    ExeVersionInfo Version,
    string SuggestedName,
    string SuggestionSource,
    IReadOnlyList<string> Candidates);

/// <summary>
/// Analisa um .EXE para sugerir o nome do jogo. NUNCA executa o arquivo: só lê metadados.
/// </summary>
public sealed class ExeAnalyzer
{
    private readonly IExeVersionReader _reader;
    public ExeAnalyzer(IExeVersionReader? reader = null) => _reader = reader ?? new FileVersionInfoReader();

    // Nomes que não identificam o jogo (motores, launchers, wrappers).
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "launcher", "launch", "start", "play", "setup", "install", "client", "app", "application",
        "main", "run", "shipping", "win64", "win32", "x64", "x86", "bin", "binaries", "binaries win64", "retail",
        "unityplayer", "unity player", "unreal engine", "ue4", "ue5", "ue4game", "unrealgame", "bootstrap",
        "bootstrappackagedgame", "bootstrap packaged game", "godot engine", "godot", "java", "javaw", "python",
        "electron", "nw", "nwjs", "renpy", "love", "steam", "common", "program files", "program files x86",
        "steamapps", "games folder", "data", "engine", "release", "debug", "build", "windows", "windowsnoeditor",
        "windows no editor", "pc", "dx11", "dx12", "vulkan", "the game",
    };

    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "bin32", "binaries", "win64", "win32", "x64", "x86", "retail", "game", "release",
        "shipping", "windows", "windowsnoeditor", "common", "steamapps", "games", "jogos", "program files",
        "program files (x86)", "epic games", "gog galaxy", "xboxgames", "content", "launcher", "client", "app",
    };

    public ExeAnalysis Analyze(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Caminho vazio.", nameof(path));
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selecione um arquivo .exe.", nameof(path));

        var version = _reader.Read(path);
        // Separa por \ e / para funcionar igual em qualquer SO (o caminho vem do Windows).
        var fileName = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Last();
        var folder = MeaningfulFolder(path);

        // Candidatos em ordem de confiança.
        var candidates = new List<(string Name, string Source)>();
        void AddCandidate(string? raw, string source)
        {
            var clean = CleanName(raw);
            if (clean is null || IsGeneric(clean)) return;
            if (!string.IsNullOrWhiteSpace(version.CompanyName) &&
                string.Equals(clean, CleanName(version.CompanyName), StringComparison.OrdinalIgnoreCase)) return;
            if (candidates.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase))) return;
            candidates.Add((clean, source));
        }

        AddCandidate(version.ProductName, "Product Name");
        AddCandidate(version.FileDescription, "File Description");
        AddCandidate(folder, "Pasta de instalação");
        AddCandidate(System.IO.Path.GetFileNameWithoutExtension(fileName), "Nome do arquivo");
        AddCandidate(version.OriginalFilename is { } of ? System.IO.Path.GetFileNameWithoutExtension(of) : null, "Original Filename");

        var best = candidates.FirstOrDefault();
        // Se Product Name e pasta concordam pouco, mas o nome do arquivo bate com a pasta, prefira a pasta (mais confiável que descrições longas).
        if (best.Name is not null && best.Source == "File Description" && best.Name.Split(' ').Length > 6)
            best = candidates.Skip(1).FirstOrDefault(c => c.Source != "File Description") is { Name: not null } alt ? alt : best;

        var suggested = best.Name ?? System.IO.Path.GetFileNameWithoutExtension(fileName);
        return new ExeAnalysis(path, fileName, folder ?? "", version, suggested, best.Source ?? "Nome do arquivo",
            candidates.Select(c => c.Name).ToList());
    }

    /// <summary>Primeira pasta acima do EXE que não seja genérica (bin, x64, Binaries…).</summary>
    public static string? MeaningfulFolder(string exePath)
    {
        var parts = exePath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            var p = parts[i];
            if (p.EndsWith(':')) break; // raiz do drive
            if (GenericFolders.Contains(p) || IsGeneric(CleanName(p) ?? "")) continue;
            return p;
        }
        return null;
    }

    private static bool IsGeneric(string name) => name.Length < 2 || GenericNames.Contains(name);

    private static readonly Regex Suffixes = new(
        @"([-_ .]?(win64|win32|x64|x86|shipping|dx11|dx12|vulkan|launcher|steam|epic|gog|retail|final|release|64bit|32bit))+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Limpa um nome bruto: remove extensão/sufixos técnicos, separa CamelCase e letras/números,
    /// troca _ - . por espaço e remove ™ ® ©. Ex.: "Cyberpunk2077" → "Cyberpunk 2077".
    /// </summary>
    public static string? CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        s = s.Replace("™", "").Replace("®", "").Replace("©", "");
        for (var i = 0; i < 3; i++) s = Suffixes.Replace(s, "");

        var hasSpaces = s.Contains(' ');
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is '_' or '.' || (ch == '-' && !hasSpaces)) { sb.Append(' '); continue; }
            if (i > 0 && !hasSpaces)
            {
                var prev = s[i - 1];
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                var split =
                    (char.IsLower(prev) && char.IsUpper(ch)) ||                          // camelCase
                    (char.IsUpper(prev) && char.IsUpper(ch) && char.IsLower(next)) ||    // HTMLParser → HTML Parser
                    (char.IsLetter(prev) && char.IsDigit(ch) && !IsVersionLike(s, i)) || // Cyberpunk2077
                    (char.IsDigit(prev) && char.IsLetter(ch) && !IsOrdinalOrUnit(s, i)); // 2077Game
                if (split) sb.Append(' ');
            }
            sb.Append(ch);
        }
        var result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim(' ', '-', ':');
        return result.Length == 0 ? null : result;
    }

    // "v1" / "x64" ficam juntos.
    private static bool IsVersionLike(string s, int digitIndex) =>
        digitIndex >= 1 && (s[digitIndex - 1] is 'v' or 'V') && (digitIndex == 1 || !char.IsLetter(s[digitIndex - 2]));

    // "3D", "2K", "4X" ficam juntos.
    private static bool IsOrdinalOrUnit(string s, int letterIndex)
    {
        var ch = char.ToUpperInvariant(s[letterIndex]);
        var nextIsLetter = letterIndex + 1 < s.Length && char.IsLower(s[letterIndex + 1]);
        return ch is 'D' or 'K' or 'X' && !nextIsLetter;
    }
}
