using GameHub.App.Controls;
using GameHub.App.Services;
using GameHub.Core.Data;
using GameHub.Core.Models;
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
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        _ready = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _coverWidth = ThemeService.CoverWidth;
        Reload();
    }

    private void Reload()
    {
        if (!_ready) return;
        var status = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as GameStatus?;
        var sort = Enum.TryParse<GameSort>((SortBox.SelectedItem as ComboBoxItem)?.Tag as string, out var s) ? s : GameSort.Title;
        var query = new GameQuery(SearchBox.Text, status, FavToggle.IsChecked == true, sort);
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
}
