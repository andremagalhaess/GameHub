using GameHub.Core.Models;

namespace GameHub.Core.Data;

public sealed class ProfileRepository
{
    private readonly Database _db;
    public ProfileRepository(Database db) => _db = db;

    public PlayerProfile Get()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM profile WHERE id = 1;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new PlayerProfile();
        return new PlayerProfile
        {
            DisplayName = r.Str("display_name") ?? "",
            Username = r.Str("username") ?? "",
            Bio = r.Str("bio"),
            AvatarPath = r.Str("avatar_path"),
            BannerPath = r.Str("banner_path"),
            AccentColor = r.Str("accent_color") ?? "#7C5CFF",
            CurrentGameId = r.Long("current_game_id"),
        };
    }

    public void Save(PlayerProfile p)
    {
        if (!IsValidColor(p.AccentColor)) throw new ArgumentException("Cor de destaque inválida. Use o formato #RRGGBB.");
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE profile SET display_name = @dn, username = @un, bio = @bio, avatar_path = @av,
                banner_path = @bn, accent_color = @ac,
                current_game_id = (SELECT id FROM games WHERE id = @cg)
            WHERE id = 1;
            """;
        cmd.Add("@dn", (p.DisplayName ?? "").Trim());
        cmd.Add("@un", (p.Username ?? "").Trim().TrimStart('@'));
        cmd.Add("@bio", p.Bio.ToDb());
        cmd.Add("@av", p.AvatarPath.ToDb());
        cmd.Add("@bn", p.BannerPath.ToDb());
        cmd.Add("@ac", p.AccentColor.ToUpperInvariant());
        cmd.Add("@cg", p.CurrentGameId.ToDb());
        cmd.ExecuteNonQuery();
    }

    public static bool IsValidColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);
}
