using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace GameHub.App.Helpers;

/// <summary>Exporta um elemento da tela como PNG (Meu Ano Gamer, rankings).</summary>
public static class ImageExport
{
    /// <summary>Pergunta onde salvar e grava o PNG. Retorna o caminho salvo ou null se o usuário cancelar.</summary>
    public static async Task<string?> SavePngAsync(UIElement element, string suggestedName)
    {
        var path = await Pickers.SaveFileAsync(Sanitize(suggestedName), "Imagem PNG", ".png");
        if (path is null) return null;

        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        if (rtb.PixelWidth == 0 || rtb.PixelHeight == 0) throw new InvalidOperationException("Não foi possível gerar a imagem (o conteúdo está vazio ou oculto).");
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        var dpi = 96.0 * (element.XamlRoot?.RasterizationScale ?? 1.0);

        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, pixels);
            await encoder.FlushAsync();
        }
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        return name.Trim();
    }
}
