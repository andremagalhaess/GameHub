using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace GameHub.App.Services;

/// <summary>Extrai o ícone do .exe pelo Shell do Windows (sem executar o arquivo).</summary>
public static class IconExtractor
{
    public static async Task<byte[]?> ExtractPngAsync(string exePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(exePath);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0) return null;
            var decoder = await BitmapDecoder.CreateAsync(thumb);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            output.Seek(0);
            using var stream = output.AsStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao extrair ícone de {exePath}", ex);
            return null;
        }
    }
}
