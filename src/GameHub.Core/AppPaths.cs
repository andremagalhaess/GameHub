namespace GameHub.Core;

/// <summary>Pastas de dados do GameHub. Padrão: %LOCALAPPDATA%\GameHub.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string DatabaseFile => Path.Combine(Root, "gamehub.db");
    public string ImagesDir => Path.Combine(Root, "images");
    public string BackupsDir => Path.Combine(Root, "backups");
    public string LogsDir => Path.Combine(Root, "logs");

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameHub");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ImagesDir);
    }
}
