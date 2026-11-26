using GameHub.Core.Data;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GameHub.App.Services;

public static class ThemeService
{
    public static Color ParseColor(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return Color.FromArgb(255, r, g, b);
        return Color.FromArgb(255, 0x7C, 0x5C, 0xFF);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Define as cores de destaque do sistema antes da criação da janela (controles nativos).</summary>
    public static void ApplyAccentAtStartup(string? hex)
    {
        var c = ParseColor(hex);
        var res = Application.Current.Resources;
        res["SystemAccentColor"] = c;
        res["SystemAccentColorLight1"] = Blend(c, Colors.White, 0.2);
        res["SystemAccentColorLight2"] = Blend(c, Colors.White, 0.4);
        res["SystemAccentColorLight3"] = Blend(c, Colors.White, 0.6);
        res["SystemAccentColorDark1"] = Blend(c, Colors.Black, 0.2);
        res["SystemAccentColorDark2"] = Blend(c, Colors.Black, 0.4);
        res["SystemAccentColorDark3"] = Blend(c, Colors.Black, 0.6);
        ApplyAccentLive(hex);
    }

    /// <summary>Atualiza a cor de destaque do GameHub na hora (pincéis próprios).</summary>
    public static void ApplyAccentLive(string? hex)
    {
        var c = ParseColor(hex);
        if (Application.Current.Resources["GhAccentBrush"] is SolidColorBrush b) b.Color = c;
        if (Application.Current.Resources["GhAccentSoftBrush"] is SolidColorBrush s) s.Color = c;
    }

    public static ElementTheme CurrentTheme => AppServices.Settings.GetString(SettingKeys.Theme, "dark") switch
    {
        "light" => ElementTheme.Light,
        "system" => ElementTheme.Default,
        _ => ElementTheme.Dark, // escuro é o padrão
    };

    public static bool TransparencyEnabled => AppServices.Settings.GetBool(SettingKeys.Transparency, true);
    public static bool AnimationsEnabled => AppServices.Settings.GetBool(SettingKeys.Animations, true);
    public static int CoverWidth => Math.Clamp(AppServices.Settings.GetInt(SettingKeys.CoverSize, 170), 110, 280);

    public static void ApplyBackdrop(Window w)
    {
        if (TransparencyEnabled && MicaController.IsSupported())
            w.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        else
            w.SystemBackdrop = null;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
