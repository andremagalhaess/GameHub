using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameHub.App.Helpers;

/// <summary>
/// Carrega imagens locais/remotas sob demanda, já decodificadas no tamanho de exibição
/// (DecodePixelWidth) para economizar memória.
/// </summary>
public static class ImageHelper
{
    public static ImageSource? Local(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return new BitmapImage
        {
            DecodePixelWidth = decodeWidth,
            DecodePixelType = DecodePixelType.Logical,
            UriSource = new Uri(path),
        };
    }

    public static ImageSource? Any(string? pathOrUrl, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;
        if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical, UriSource = new Uri(pathOrUrl) };
        return Local(pathOrUrl, decodeWidth);
    }

    // Atalhos usados por x:Bind nos templates.
    public static ImageSource? Cover(string? path) => Local(path, 300);
    public static ImageSource? Thumb(string? path) => Local(path, 120);
    public static ImageSource? Remote(string? url) => Any(url, 160);
    public static ImageSource? Gallery(string? pathOrUrl) => Any(pathOrUrl, 240);
}
