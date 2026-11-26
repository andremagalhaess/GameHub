using System.Diagnostics;
using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Launching;
using GameHub.Core.Metadata;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class GameDetailsPage : Page
{
    private Game? _game;
    private bool _loading;

    public GameDetailsPage()
    {
        InitializeComponent();
        foreach (var s in GameStatusText.All)
        {
            StatusBox.Items.Add(new ComboBoxItem { Content = s.ToDisplay(), Tag = s });
            ReviewStatus.Items.Add(new ComboBoxItem { Content = s.ToDisplay(), Tag = s });
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is long id) Load(id);
    }

    private void Load(long id)
    {
        _game = AppServices.Games.Get(id);
        if (_game is null)
        {
            App.MainWindow!.GoBackOr(typeof(LibraryPage));
            return;
        }
        var g = _game;
        _loading = true;

        HeroImage.Source = ImageHelper.Local(g.BackgroundPath ?? g.CoverPath, 1920);
        Cover.Game = g;
        LogoImage.Source = ImageHelper.Local(g.LogoPath, 880);
        LogoImage.Visibility = LogoImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = g.Title;
        TitleText.Visibility = LogoImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = string.Join("  ·  ", new[] { g.ReleaseYear?.ToString(), g.Developer, g.Genres.FirstOrDefault() }.Where(x => !string.IsNullOrWhiteSpace(x)));

        PlayButton.IsEnabled = g.CanLaunch;
        ToolTipService.SetToolTip(PlayButton, g.LaunchUri ?? g.ExecutablePath ?? "Nenhum executável configurado");
        FavButton.IsChecked = g.IsFavorite;
        FavIcon.Glyph = g.IsFavorite ? "" : "";
        StatusBox.SelectedIndex = GameStatusText.All.ToList().IndexOf(g.Status);
        OpenFolderItem.IsEnabled = g.ExecutablePath is not null;

        HoursText.Text = Fmt.Hours(g.TotalPlaytimeMinutes);
        LastPlayedText.Text = Fmt.When(g.LastPlayedAt);
        RatingText.Text = g.ExternalRating is { } r ? $"{r:0} / 100" : "—";
        RatingSource.Text = g.ExternalRatingSource ?? "Nota externa";
        DescriptionText.Text = g.Description ?? g.ShortDescription ?? "Sem descrição. Use Editar ou Atualizar informações online para preencher.";

        FactsPanel.Children.Clear();
        AddFact("Desenvolvedora", g.Developer);
        AddFact("Publicadora", g.Publisher);
        AddFact("Lançamento", Fmt.ReleaseDate(g.ReleaseDate));
        AddFact("Gêneros", Fmt.List(g.Genres));
        AddFact("Plataformas", Fmt.List(g.Platforms));
        AddFact("Classificação indicativa", g.AgeRating);
        if (g.Tags.Count > 0) AddFact("Tags", Fmt.List(g.Tags));
        AddLink("Site oficial", g.Website);
        AddLink("Trailer", g.TrailerUrl);
        AddFact("Origem", g.Source + (g.MetadataProvider is { } mp ? $" · dados: {mp}" : ""));
        AddFact("Adicionado em", g.AddedAt.ToLocalTime().ToString("d 'de' MMMM 'de' yyyy", Fmt.PtBr));
        if (g.MetadataProvider == RawgProvider.ProviderName) AddLink("Dados fornecidos por", "https://rawg.io", "RAWG");

        LoadReview(g);
        LoadPlatforms(g);
        _loading = false;
    }

    private void LoadReview(Game g)
    {
        var review = AppServices.Reviews.Get(g.Id);
        MyRatingText.Text = g.PersonalRating is { } r ? r.ToString("0.#", Fmt.PtBr) : "—";
        MyStatusText.Text = g.Status.ToDisplay();
        var dates = new List<string>();
        if (Fmt.ShortDate(g.StartedAt) is { } st) dates.Add($"Comecei em {st}");
        if (Fmt.ShortDate(g.FinishedAt) is { } fi) dates.Add($"terminei em {fi}");
        MyDatesText.Text = dates.Count > 0 ? string.Join(", ", dates) : "Datas não registradas";
        ReviewButtonText.Text = g.PersonalRating is null && review is null ? "Registrar avaliação" : "Editar avaliação";
        ReviewText.Text = review?.Review ?? (g.PersonalRating is null ? "Você ainda não avaliou este jogo." : "");
        ReviewText.Visibility = string.IsNullOrEmpty(ReviewText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ProsText.Text = Fmt.Or(review?.Pros);
        ConsText.Text = Fmt.Or(review?.Cons);
        ProsConsGrid.Visibility = review?.Pros is null && review?.Cons is null ? Visibility.Collapsed : Visibility.Visible;
        MomentsText.Text = review?.Moments ?? "";
        MomentsPanel.Visibility = Fmt.IfText(review?.Moments);
        CommentsText.Text = review?.Comments ?? "";
        CommentsPanel.Visibility = Fmt.IfText(review?.Comments);
    }

    private void LoadPlatforms(Game g)
    {
        var links = AppServices.Sources.ForGame(g.Id);
        PlatformsCard.Visibility = links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlatformsPanel.Children.Clear();
        foreach (var l in links)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = l.Source, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var detail = l.IsInstalled ? "Instalado" : "Não instalado";
            if (l.PlaytimeMinutes is > 0) detail += $" · {Fmt.Hours(l.PlaytimeMinutes.Value)} na plataforma";
            info.Children.Add(new TextBlock { Text = detail, FontSize = 12, Opacity = 0.7 });
            row.Children.Add(info);
            var unlink = new Button { Content = "Desvincular", FontSize = 12, Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(unlink, "Separa esta plataforma deste jogo. Na próxima atualização da biblioteca ela vira uma entrada própria.");
            var linkId = l.Id;
            unlink.Click += (_, _) => { AppServices.Sources.Unlink(linkId); LoadPlatforms(g); };
            Grid.SetColumn(unlink, 1);
            row.Children.Add(unlink);
            PlatformsPanel.Children.Add(row);
        }
    }

    // ---------- Avaliação ----------

    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var g = _game;
        var review = AppServices.Reviews.Get(g.Id);
        NoRatingBox.IsChecked = g.PersonalRating is null;
        RatingSlider.Value = g.PersonalRating ?? 7;
        UpdateRatingLabel();
        ReviewStatus.SelectedIndex = GameStatusText.All.ToList().IndexOf(g.Status);
        StartedPicker.Date = Fmt.ToDateTimeOffset(g.StartedAt);
        FinishedPicker.Date = Fmt.ToDateTimeOffset(g.FinishedAt);
        ReviewBox.Text = review?.Review ?? "";
        ProsBox.Text = review?.Pros ?? "";
        ConsBox.Text = review?.Cons ?? "";
        MomentsBox.Text = review?.Moments ?? "";
        CommentsBox.Text = review?.Comments ?? "";
        ReviewTagsBox.Text = string.Join(", ", g.Tags);
        ReviewDialog.Title = $"Minha avaliação · {g.Title}";
        ReviewDialog.XamlRoot = XamlRoot;
        ReviewDialog.RequestedTheme = ActualTheme;
        await ReviewDialog.ShowAsync();
    }

    private void RatingSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => UpdateRatingLabel();
    private void NoRating_Changed(object sender, RoutedEventArgs e) => UpdateRatingLabel();

    private void UpdateRatingLabel()
    {
        var none = NoRatingBox.IsChecked == true;
        RatingSlider.IsEnabled = !none;
        RatingValue.Text = none ? "—" : RatingSlider.Value.ToString("0.#", Fmt.PtBr);
    }

    private void ReviewDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_game is null) return;
        var g = _game;
        var started = StartedPicker.Date;
        var finished = FinishedPicker.Date;
        if (started is not null && finished is not null && finished < started)
        {
            args.Cancel = true;
            ReviewDialog.Title = "A data de conclusão é anterior ao início";
            return;
        }
        g.PersonalRating = NoRatingBox.IsChecked == true ? null : Math.Round(RatingSlider.Value * 2) / 2;
        g.Status = (ReviewStatus.SelectedItem as ComboBoxItem)?.Tag is GameStatus st ? st : g.Status;
        g.StartedAt = Fmt.ToIsoDate(started);
        g.FinishedAt = Fmt.ToIsoDate(finished);
        g.Tags = Fmt.SplitList(ReviewTagsBox.Text);
        AppServices.Games.Update(g);
        AppServices.Reviews.Save(new GameReview
        {
            GameId = g.Id,
            Review = NullIfEmpty(ReviewBox.Text),
            Pros = NullIfEmpty(ProsBox.Text),
            Cons = NullIfEmpty(ConsBox.Text),
            Moments = NullIfEmpty(MomentsBox.Text),
            Comments = NullIfEmpty(CommentsBox.Text),
        });
        Load(g.Id);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void AddFact(string label, string? value)
    {
        var sp = new StackPanel { Spacing = 2 };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 });
        sp.Children.Add(new TextBlock { Text = Fmt.Or(value), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        FactsPanel.Children.Add(sp);
    }

    private void AddLink(string label, string? url, string? text = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
        var sp = new StackPanel { Spacing = 0 };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 });
        sp.Children.Add(new HyperlinkButton { Content = text ?? uri.Host, NavigateUri = uri, Padding = new Thickness(0) });
        FactsPanel.Children.Add(sp);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var result = AppServices.Launcher.Launch(_game);
        switch (result.Outcome)
        {
            case LaunchOutcome.Started:
                ShowInfo(InfoBarSeverity.Success, _game.LaunchUri is not null
                    ? $"Abrindo {_game.Title} pelo launcher. O tempo de jogo fica com a plataforma (a Steam informa as horas via Web API)."
                    : $"Iniciando {_game.Title}. O tempo de jogo é medido enquanto o executável estiver aberto.");
                LastPlayedText.Text = Fmt.When(DateTimeOffset.UtcNow);
                if (result.SessionMinutes is { } session) _ = AfterSessionAsync(_game.Id, session);
                break;
            default:
                ShowInfo(InfoBarSeverity.Error, result.Message ?? "Não foi possível iniciar.");
                break;
        }
    }

    private async Task AfterSessionAsync(long id, Task<long?> session)
    {
        long? minutes;
        try { minutes = await session; } catch { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_game?.Id != id) return;
            Load(id);
            ShowInfo(minutes is null ? InfoBarSeverity.Informational : InfoBarSeverity.Success,
                minutes is null
                    ? "O executável fechou logo após abrir (provavelmente abriu outro launcher). Essa sessão não foi contada."
                    : $"Sessão registrada: {Fmt.Hours(minutes.Value)}.");
        });
    }

    private void Fav_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        _game.IsFavorite = FavButton.IsChecked == true;
        AppServices.Games.SetFavorite(_game.Id, _game.IsFavorite);
        FavIcon.Glyph = _game.IsFavorite ? "" : "";
        Cover.Game = null; Cover.Game = _game;
    }

    private void Status_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _game is null || (StatusBox.SelectedItem as ComboBoxItem)?.Tag is not GameStatus s) return;
        _game.Status = s;
        AppServices.Games.SetStatus(_game.Id, s);
        Cover.Game = null; Cover.Game = _game;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not null) App.MainWindow!.Navigate(typeof(GameEditPage), _game.Id);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not null) App.MainWindow!.Navigate(typeof(AddGamePage), new RefreshMetadataRequest(_game.Id));
    }

    private async void ChangeCover_Click(object sender, RoutedEventArgs e) => await ChangeImageAsync(ImageKind.Cover);
    private async void ChangeBackground_Click(object sender, RoutedEventArgs e) => await ChangeImageAsync(ImageKind.Background);

    private async Task ChangeImageAsync(ImageKind kind)
    {
        if (_game is null) return;
        var file = await Pickers.PickImageAsync();
        if (file is null) return;
        try
        {
            var path = await AppServices.Images.ImportLocalAsync(file, kind);
            if (kind == ImageKind.Cover) _game.CoverPath = path; else _game.BackgroundPath = path;
            AppServices.Games.Update(_game);
            Load(_game.Id);
        }
        catch (Exception ex) { ShowInfo(InfoBarSeverity.Error, ex.Message); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_game?.ExecutablePath ?? "");
        if (dir is null || !Directory.Exists(dir)) { ShowInfo(InfoBarSeverity.Warning, "A pasta do jogo não existe mais."); return; }
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { dir }, UseShellExecute = false });
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var ok = await Dialogs.ConfirmAsync("Remover da biblioteca?",
            $"\"{_game.Title}\" sai da biblioteca do GameHub, junto com o tempo registrado aqui. Os arquivos do jogo NÃO são apagados nem desinstalados.",
            "Remover", destructive: true);
        if (!ok) return;
        AppServices.Games.Remove(_game.Id);
        App.MainWindow!.GoBackOr(typeof(LibraryPage));
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        Info.Severity = severity;
        Info.Message = message;
        Info.IsOpen = true;
    }
}

/// <summary>Parâmetro para reabrir o fluxo de busca online para um jogo existente.</summary>
public sealed record RefreshMetadataRequest(long GameId);
