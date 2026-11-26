using GameHub.Core;

namespace GameHub.App.Services;

/// <summary>Log mínimo em arquivo (só erros), em %LOCALAPPDATA%\GameHub\logs.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            var dir = AppServices.Paths?.LogsDir ?? Path.Combine(Path.GetTempPath(), "GameHub");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"erros-{DateTime.Now:yyyy-MM}.log");
            lock (Gate) File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n{ex}\n\n");
        }
        catch { /* log nunca derruba o app */ }
    }
}
