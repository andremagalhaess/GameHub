using GameHub.App.Controls;
using GameHub.App.Services;
using GameHub.Core.Data;
using GameHub.Core.Models;
using GameHub.Core.Platforms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private bool _ready;
    private double _coverWidth;

    public LibraryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled; // volta para a biblioteca sem recarregar tudo
        StatusFilter.Items.Add(new ComboBoxItem { Content = "Todos os status", Tag = null });
        foreach (var s in GameStatusText.All)
            StatusFilter.Items.Add(new ComboBoxItem { Content = s.ToDisplay(), Tag = s });
        StatusFilter.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
        RatingFilter.SelectedIndex = 0;
        TimeFilter.SelectedIndex = 0;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        _ready = true;
    }

    /// <summary>Parâmetro de navegação: abrir a biblioteca já procurando jogos instalados.</summary>
    public const string ImportOnOpen = "import";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _coverWidth = ThemeService.CoverWidth;
        RefreshFilterOptions();
        Reload();
        if (e.Parameter as string == ImportOnOpen && e.NavigationMode == NavigationMode.New) Import_Click(this, new RoutedEventArgs());
    }

    private void Reload()
    {
        if (!_ready) return;
        var status = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as GameStatus?;
        var sort = Enum.TryParse<GameSort>((SortBox.SelectedItem as ComboBoxItem)?.Tag as string, out var s) ? s : GameSort.Title;
        var genre = (GenreFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var source = (SourceFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        double? minRating = (RatingFilter.SelectedItem as ComboBoxItem)?.Tag is string r ? double.Parse(r) : null;
        long? minTime = null, maxTime = null;
        if ((TimeFilter.SelectedItem as ComboBoxItem)?.Tag is string t)
        {
            var parts = t.Split('-');
            minTime = long.Parse(parts[0]);
            maxTime = parts[1].Length > 0 ? long.Parse(parts[1]) : null;
        }
        var query = new GameQuery(SearchBox.Text, status, FavToggle.IsChecked == true, sort,
            Genre: genre, Source: source, MinRating: minRating, MinPlaytimeMinutes: minTime, MaxPlaytimeMinutes: maxTime);
        var games = AppServices.Games.Query(query);
        GamesGrid.ItemsSource = games;

        var total = AppServices.Games.Count();
        CountText.Text = total == 1 ? "1 jogo" : $"{total} jogos";
        if (games.Count < total) CountText.Text += $" · {games.Count} exibidos";

        EmptyState.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = total == 0 ? "Sua biblioteca está vazia." : "Nenhum jogo encontrado com esses filtros.";
        EmptyAdd.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is GameCoverCard card && card.CoverWidth != _coverWidth) card.CoverWidth = _coverWidth;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => Reload();
    private void Fav_Click(object sender, RoutedEventArgs e) => Reload();

    private void Grid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game g) App.MainWindow!.Navigate(typeof(GameDetailsPage), g.Id);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => App.MainWindow!.Navigate(typeof(AddGamePage));

    /// <summary>Preenche os filtros de gênero e plataforma com o que existe na biblioteca.</summary>
    private void RefreshFilterOptions()
    {
        var wasReady = _ready;
        _ready = false;
        var genre = (GenreFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var source = (SourceFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        GenreFilter.Items.Clear();
        GenreFilter.Items.Add(new ComboBoxItem { Content = "Todos os gêneros" });
        foreach (var g in AppServices.Games.DistinctGenres()) GenreFilter.Items.Add(new ComboBoxItem { Content = g, Tag = g });
        SourceFilter.Items.Clear();
        SourceFilter.Items.Add(new ComboBoxItem { Content = "Todas as plataformas" });
        foreach (var s in AppServices.Games.DistinctSources()) SourceFilter.Items.Add(new ComboBoxItem { Content = s, Tag = s });
        GenreFilter.SelectedItem = GenreFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == genre) ?? GenreFilter.Items[0];
        SourceFilter.SelectedItem = SourceFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == source) ?? SourceFilter.Items[0];
        _ready = wasReady;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _ready = false;
        SearchBox.Text = "";
        StatusFilter.SelectedIndex = 0;
        GenreFilter.SelectedIndex = 0;
        SourceFilter.SelectedIndex = 0;
        RatingFilter.SelectedIndex = 0;
        TimeFilter.SelectedIndex = 0;
        FavToggle.IsChecked = false;
        _ready = true;
        Reload();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        ImportBusy.IsActive = true;
        ImportInfo.IsOpen = false;
        try
        {
            var progress = new Progress<string>(msg => { ImportInfo.Severity = InfoBarSeverity.Informational; ImportInfo.Title = msg; ImportInfo.Message = ""; ImportInfo.IsOpen = true; });
            var report = await Task.Run(() => AppServices.Importer.ImportAsync(AppServices.IsScannerEnabled, progress));
            AppServices.Settings.Set(SettingKeys.LastImportAt, DateTimeOffset.UtcNow.ToString("O"));
            ShowReport(report);
            RefreshFilterOptions();
            Reload();
        }
        catch (Exception ex)
        {
            Log.Error("Falha na importação", ex);
            ImportInfo.Severity = InfoBarSeverity.Error;
            ImportInfo.Title = "Não foi possível atualizar a biblioteca";
            ImportInfo.Message = ex.Message;
            ImportInfo.IsOpen = true;
        }
        finally
        {
            ImportButton.IsEnabled = true;
            ImportBusy.IsActive = false;
        }
    }

    private void ShowReport(ImportReport r)
    {
        var parts = new List<string>();
        if (r.Added > 0) parts.Add($"{r.Added} novo(s)");
        if (r.Linked > 0) parts.Add($"{r.Linked} associado(s) a jogos que você já tinha");
        if (r.Updated > 0) parts.Add($"{r.Updated} atualizado(s)");
        if (r.MarkedUninstalled > 0) parts.Add($"{r.MarkedUninstalled} marcado(s) como não instalado(s)");
        if (r.Ignored > 0) parts.Add($"{r.Ignored} ignorado(s) porque você removeu");
        ImportInfo.Severity = r.Problems.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        ImportInfo.Title = parts.Count > 0 ? "Biblioteca atualizada: " + string.Join(", ", parts) : "Nenhum jogo novo encontrado.";
        var lines = new List<string>();
        if (r.PlatformsFound.Count > 0) lines.Add("Plataformas encontradas: " + string.Join(", ", r.PlatformsFound.Distinct()));
        lines.AddRange(r.Problems.Select(p => $"{p.Platform}: {p.Message}"));
        ImportInfo.Message = string.Join("\n", lines);
        ImportInfo.IsOpen = true;
    }
}
