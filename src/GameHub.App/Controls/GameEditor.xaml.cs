using System.Collections.ObjectModel;
using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Images;
using GameHub.Core.Metadata;
using GameHub.Core.Models;
using GameHub.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameHub.App.Controls;

/// <summary>
/// Formulário com TODOS os campos editáveis de um jogo + galerias de artes.
/// Usado na tela de confirmação (adicionar) e na edição.
/// </summary>
public sealed partial class GameEditor : UserControl
{
    private readonly ObservableCollection<ImageChoice> _covers = new();
    private readonly ObservableCollection<ImageChoice> _backgrounds = new();
    private readonly ObservableCollection<ImageChoice> _logos = new();
    private Game _game = new();
    private string? _iconPath;

    public GameEditor()
    {
        InitializeComponent();
        CoverGallery.ItemsSource = _covers;
        BackgroundGallery.ItemsSource = _backgrounds;
        LogoGallery.ItemsSource = _logos;
        foreach (var s in GameStatusText.All) StatusBox.Items.Add(new ComboBoxItem { Content = s.ToDisplay(), Tag = s });
    }

    /// <summary>Preenche o formulário. <paramref name="metadata"/> traz as artes encontradas online.</summary>
    public void Load(Game game, GameMetadata? metadata = null)
    {
        _game = game;
        TitleBox.Text = game.Title;
        AltNamesBox.Text = game.AlternativeNames ?? "";
        DeveloperBox.Text = game.Developer ?? "";
        PublisherBox.Text = game.Publisher ?? "";
        GenresBox.Text = string.Join(", ", game.Genres);
        TagsBox.Text = string.Join(", ", game.Tags);
        PlatformsBox.Text = string.Join(", ", game.Platforms);
        ReleaseBox.Text = game.ReleaseDate ?? "";
        AgeRatingBox.Text = game.AgeRating ?? "";
        WebsiteBox.Text = game.Website ?? "";
        TrailerBox.Text = game.TrailerUrl ?? "";
        PlatformSourceBox.Text = game.Source;
        ShortDescBox.Text = game.ShortDescription ?? "";
        DescriptionBox.Text = game.Description ?? "";
        StatusBox.SelectedIndex = GameStatusText.All.ToList().IndexOf(game.Status);
        HoursBox.Value = Math.Round(game.PlaytimeMinutes / 60.0, 1);
        FavoriteBox.IsChecked = game.IsFavorite;
        ExeBox.Text = game.ExecutablePath ?? "";
        ArgsBox.Text = game.LaunchArguments ?? "";
        WorkDirBox.Text = game.WorkingDirectory ?? "";
        SetIcon(game.IconPath);

        FillGallery(CoverGallery, _covers, ImageKind.Cover, game.CoverPath, metadata?.ImagesOf(ImageKind.Cover));
        FillGallery(BackgroundGallery, _backgrounds, ImageKind.Background, game.BackgroundPath,
            metadata?.ImagesOf(ImageKind.Background).Concat(metadata.ImagesOf(ImageKind.Screenshot)));
        FillGallery(LogoGallery, _logos, ImageKind.Logo, game.LogoPath, metadata?.ImagesOf(ImageKind.Logo));
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static void FillGallery(GridView view, ObservableCollection<ImageChoice> items, ImageKind kind, string? current, IEnumerable<ImageCandidate>? online)
    {
        items.Clear();
        items.Add(new ImageChoice { Source = null, Label = "Sem imagem", Kind = kind });
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
            items.Add(new ImageChoice { Source = current, Label = "Atual", Kind = kind });
        foreach (var c in online ?? Enumerable.Empty<ImageCandidate>())
            if (items.All(i => i.Source != c.Url))
                items.Add(new ImageChoice { Source = c.Url, Label = c.Provider, Kind = kind });
        // Seleção padrão: a arte atual; senão a primeira encontrada online; senão "Sem imagem".
        view.SelectedIndex = items.Count > 1 ? 1 : 0;
    }

    private void Thumb_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // Arte que não existe no servidor (ex.: jogo sem logo na Steam): some da galeria.
        if (sender is FrameworkElement { DataContext: ImageChoice choice })
        {
            var (view, list) = Gallery(choice.Kind);
            var wasSelected = view.SelectedItem == choice;
            list.Remove(choice);
            if (wasSelected) view.SelectedIndex = list.Count > 1 ? 1 : 0;
        }
    }

    private (GridView, ObservableCollection<ImageChoice>) Gallery(ImageKind kind) => kind switch
    {
        ImageKind.Cover => (CoverGallery, _covers),
        ImageKind.Logo => (LogoGallery, _logos),
        _ => (BackgroundGallery, _backgrounds),
    };

    private async void PickLocalImage_Click(object sender, RoutedEventArgs e)
    {
        var kind = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "Cover" => ImageKind.Cover,
            "Logo" => ImageKind.Logo,
            _ => ImageKind.Background,
        };
        var file = await Pickers.PickImageAsync();
        if (file is null) return;
        var (view, list) = Gallery(kind);
        var choice = new ImageChoice { Source = file, Label = "Do computador", Kind = kind };
        list.Insert(1, choice);
        view.SelectedItem = choice;
    }

    private async void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var file = await Pickers.PickExeAsync();
        if (file is not null) ExeBox.Text = file;
    }

    private async void BrowseWorkDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = await Pickers.PickFolderAsync();
        if (dir is not null) WorkDirBox.Text = dir;
    }

    private async void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        var file = await Pickers.PickImageAsync();
        if (file is null) return;
        try { SetIcon(await AppServices.Images.ImportLocalAsync(file, ImageKind.Icon)); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ExeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(ExeBox.Text)) { ShowError("Escolha um executável válido primeiro."); return; }
        var png = await IconExtractor.ExtractPngAsync(ExeBox.Text);
        if (png is null) { ShowError("Não foi possível ler o ícone desse executável."); return; }
        SetIcon(await AppServices.Images.StoreAsync(png, ImageKind.Icon));
    }

    private void SetIcon(string? path)
    {
        _iconPath = path;
        IconPreview.Source = ImageHelper.Local(path, 80);
    }

    /// <summary>Valida o formulário. Retorna a mensagem de erro, ou null se estiver tudo certo.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) return "Informe o nome do jogo.";
        var exe = ExeBox.Text.Trim().Trim('"');
        if (exe.Length > 0 && !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "O executável precisa ser um arquivo .exe.";
        if (exe.Length > 0 && !File.Exists(exe)) return "O executável informado não existe.";
        if (WorkDirBox.Text.Trim().Length > 0 && !Directory.Exists(WorkDirBox.Text.Trim())) return "A pasta de trabalho não existe.";
        foreach (var (box, label) in new[] { (WebsiteBox, "site oficial"), (TrailerBox, "trailer") })
            if (box.Text.Trim().Length > 0 && !(Uri.TryCreate(box.Text.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))
                return $"O link do {label} precisa começar com https://";
        if (!double.IsNaN(HoursBox.Value) && HoursBox.Value < 0) return "Horas jogadas não pode ser negativo.";
        return null;
    }

    /// <summary>
    /// Copia os campos para o jogo e baixa as artes selecionadas. Falhas de download não impedem
    /// o salvamento: viram avisos em <paramref name="warnings"/>.
    /// </summary>
    public async Task<Game> CommitAsync(ImageCache cache, List<string> warnings, CancellationToken ct = default)
    {
        var g = _game;
        g.Title = TitleBox.Text.Trim();
        g.AlternativeNames = NullIfEmpty(AltNamesBox.Text);
        g.Developer = NullIfEmpty(DeveloperBox.Text);
        g.Publisher = NullIfEmpty(PublisherBox.Text);
        g.Genres = Fmt.SplitList(GenresBox.Text);
        g.Tags = Fmt.SplitList(TagsBox.Text);
        g.Platforms = Fmt.SplitList(PlatformsBox.Text);
        var release = NullIfEmpty(ReleaseBox.Text);
        g.ReleaseDate = release is null ? null : DateParsing.ToIso(release) ?? release;
        g.AgeRating = NullIfEmpty(AgeRatingBox.Text);
        g.Website = NullIfEmpty(WebsiteBox.Text);
        g.TrailerUrl = NullIfEmpty(TrailerBox.Text);
        g.Source = NullIfEmpty(PlatformSourceBox.Text) ?? GameSources.Manual;
        g.ShortDescription = NullIfEmpty(ShortDescBox.Text);
        g.Description = NullIfEmpty(DescriptionBox.Text);
        g.Status = (StatusBox.SelectedItem as ComboBoxItem)?.Tag is GameStatus s ? s : GameStatus.None;
        g.PlaytimeMinutes = double.IsNaN(HoursBox.Value) ? 0 : (long)Math.Round(HoursBox.Value * 60);
        g.IsFavorite = FavoriteBox.IsChecked == true;
        g.ExecutablePath = NullIfEmpty(ExeBox.Text.Trim().Trim('"'));
        g.LaunchArguments = NullIfEmpty(ArgsBox.Text);
        g.WorkingDirectory = NullIfEmpty(WorkDirBox.Text);
        g.IconPath = _iconPath;

        g.CoverPath = await ResolveAsync(CoverGallery, cache, warnings, "capa", ct);
        g.BackgroundPath = await ResolveAsync(BackgroundGallery, cache, warnings, "background", ct);
        g.LogoPath = await ResolveAsync(LogoGallery, cache, warnings, "logo", ct);

        if (g.IconPath is null && g.ExecutablePath is not null && await IconExtractor.ExtractPngAsync(g.ExecutablePath) is { } png)
            g.IconPath = await cache.StoreAsync(png, ImageKind.Icon, ct);
        return g;
    }

    private static async Task<string?> ResolveAsync(GridView view, ImageCache cache, List<string> warnings, string label, CancellationToken ct)
    {
        if (view.SelectedItem is not ImageChoice { Source: { } src } choice) return null;
        try
        {
            if (choice.IsRemote) return await cache.DownloadAsync(src, choice.Kind == ImageKind.Screenshot ? ImageKind.Background : choice.Kind, ct);
            if (src.StartsWith(cache.Root, StringComparison.OrdinalIgnoreCase)) return src; // já está no cache
            return await cache.ImportLocalAsync(src, choice.Kind, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Não foi possível salvar a {label}: {ex.Message}");
            return null;
        }
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
