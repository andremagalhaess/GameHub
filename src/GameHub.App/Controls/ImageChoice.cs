using GameHub.Core.Metadata;
using Microsoft.UI.Xaml.Media;

namespace GameHub.App.Controls;

/// <summary>Uma opção de arte na galeria: URL online, arquivo local ou "sem imagem" (Source nulo).</summary>
public sealed class ImageChoice
{
    public string? Source { get; init; }
    public string Label { get; init; } = "";
    public ImageKind Kind { get; init; }
    public bool IsRemote => Source?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true;
    public Stretch Stretch => Kind == ImageKind.Logo ? Stretch.Uniform : Stretch.UniformToFill;
}
