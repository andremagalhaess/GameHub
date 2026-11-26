using System.Globalization;

namespace GameHub.Core.Data;

/// <summary>Configurações chave/valor persistidas no SQLite.</summary>
public sealed class SettingsRepository
{
    private readonly Database _db;
    public SettingsRepository(Database db) => _db = db;

    public string? Get(string key)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @k;";
        cmd.Add("@k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string? value)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        if (value is null)
        {
            cmd.CommandText = "DELETE FROM settings WHERE key = @k;";
            cmd.Add("@k", key);
        }
        else
        {
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            cmd.Add("@k", key);
            cmd.Add("@v", value);
        }
        cmd.ExecuteNonQuery();
    }

    public string GetString(string key, string fallback) => Get(key) ?? fallback;
    public bool GetBool(string key, bool fallback) => Get(key) is { } v ? v == "1" : fallback;
    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");
    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    public void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
}

public static class SettingKeys
{
    public const string Theme = "ui.theme";               // "dark" | "light" | "system"
    public const string CoverSize = "ui.cover_size";      // largura em px
    public const string Animations = "ui.animations";     // "1" | "0"
    public const string Transparency = "ui.transparency"; // "1" | "0" (Mica)
    public const string RawgApiKey = "secret.rawg.api_key";
    public const string IgdbClientId = "secret.igdb.client_id";
    public const string IgdbClientSecret = "secret.igdb.client_secret";
    public const string SteamEnabled = "metadata.steam.enabled";
    public const string MetadataLanguage = "metadata.language"; // "pt-BR"
}
