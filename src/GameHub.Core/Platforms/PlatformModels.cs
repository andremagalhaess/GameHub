namespace GameHub.Core.Platforms;

/// <summary>Um jogo encontrado em uma plataforma (instalado ou só na conta).</summary>
public sealed record DetectedGame(
    string Source,
    string SourceId,
    string Title,
    string? InstallDir = null,
    string? ExecutablePath = null,
    string? LaunchUri = null,
    string? LaunchArguments = null,
    bool IsInstalled = true,
    long? PlaytimeMinutes = null,
    DateTimeOffset? LastPlayedAt = null);

public sealed record PlatformScanResult(string Platform, IReadOnlyList<DetectedGame> Games, string? Error = null, bool Available = true);

/// <summary>Detecta jogos de uma plataforma. Só lê arquivos/registro locais ou APIs oficiais; nunca pede senha.</summary>
public interface IPlatformScanner
{
    string Platform { get; }
    Task<PlatformScanResult> ScanAsync(CancellationToken ct);
}

/// <summary>Acesso ao registro do Windows, abstraído para testes.</summary>
public interface IRegistryReader
{
    /// <summary>Valor (string) de HKLM/HKCU. <paramref name="hive"/>: "HKLM" ou "HKCU".</summary>
    string? GetValue(string hive, string key, string name);
    IReadOnlyList<string> GetSubKeys(string hive, string key);
}

public sealed class NullRegistry : IRegistryReader
{
    public string? GetValue(string hive, string key, string name) => null;
    public IReadOnlyList<string> GetSubKeys(string hive, string key) => Array.Empty<string>();
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistryReader
{
    private static Microsoft.Win32.RegistryKey Root(string hive) =>
        hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;

    public string? GetValue(string hive, string key, string name)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(key);
            return k?.GetValue(name)?.ToString();
        }
        catch (Exception) { return null; }
    }

    public IReadOnlyList<string> GetSubKeys(string hive, string key)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(key);
            return k?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }
}
