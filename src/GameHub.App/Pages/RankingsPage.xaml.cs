using System.Collections.ObjectModel;
using GameHub.App.Controls;
using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed class RankingListItem
{
    public Ranking Ranking { get; init; } = null!;
    public string Name => RankingsPage.DisplayName(Ranking);
    public string Info { get; init; } = "";
}

public sealed class RankingRow
{
    public int Position { get; init; }
    public string PositionText => Position.ToString();
    public Game Game { get; init; } = null!;
    public string Info
    {
        get
        {
            var parts = new List<string>();
            if (Game.PersonalRating is { } r) parts.Add("Nota " + Fmt.Rating(r));
            if (Game.Status != GameStatus.None) parts.Add(Game.Status.ToDisplay());
            if (Game.TotalPlaytimeMinutes > 0) parts.Add(Fmt.Hours(Game.TotalPlaytimeMinutes));
            if (Game.ReleaseYear is { } y) parts.Add(y.ToString());
            return parts.Count == 0 ? Game.Source : string.Join(" · ", parts);
        }
    }
}

public sealed partial class RankingsPage : Page
{
    private Ranking? _current;
    private ObservableCollection<RankingRow> _rows = new();
    private List<Game> _library = new();

    public RankingsPage() => InitializeComponent();

    public static string DisplayName(Ranking r) => r.Year is { } y ? $"{r.Name} ({y})" : r.Name;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _library = AppServices.Games.All();
        LoadList(e.Parameter is long id ? id : _current?.Id);
    }

    private void LoadList(long? select = null)
    {
        var items = AppServices.Rankings.All().Select(r =>
        {
            var count = AppServices.Rankings.Entries(r.Id).Count;
            var info = count == 0 ? "Vazio" : count == 1 ? "1 jogo" : $"{count} jogos";
            if (r.MaxItems is { } max) info += $" de {max}";
            return new RankingListItem { Ranking = r, Info = info };
        }).ToList();
        RankingList.ItemsSource = items;
        RankingList.SelectedItem = items.FirstOrDefault(i => i.Ranking.Id == select) ?? items.FirstOrDefault();
        if (items.Count == 0) Show(null);
    }

    private void RankingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RankingList.SelectedItem is RankingListItem item) Show(item.Ranking);
    }

    private void Show(Ranking? r)
    {
        _current = r;
        Info.IsOpen = false;
        RTitle.Text = r is null ? "Nenhum ranking" : DisplayName(r);
        RDescription.Text = r?.Description ?? (r?.MaxItems is { } m ? $"Até {m} jogos." : "");
        RDescription.Visibility = string.IsNullOrWhiteSpace(RDescription.Text) ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = r is not null;
        AddBox.IsEnabled = r is not null;
        LoadEntries();
    }

    private void LoadEntries()
    {
        var entries = _current is null ? new List<RankingEntry>() : AppServices.Rankings.Entries(_current.Id);
        _rows = new ObservableCollection<RankingRow>(entries.Select(e => new RankingRow { Position = e.Position, Game = e.Game }));
        EntryList.ItemsSource = _rows;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = _rows.Count > 0;
    }

    private void SaveOrder(IEnumerable<long> ids)
    {
        if (_current is null) return;
        AppServices.Rankings.SetOrder(_current.Id, ids.ToList());
        LoadEntries();
    }

    private void EntryList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        SaveOrder(_rows.Select(r => r.Game.Id));

    private void EntryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankingRow r) App.MainWindow!.Navigate(typeof(GameDetailsPage), r.Game.Id);
    }

    private void Move(object sender, int delta)
    {
        if (sender is not FrameworkElement { Tag: long id }) return;
        var ids = _rows.Select(r => r.Game.Id).ToList();
        var i = ids.IndexOf(id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= ids.Count) return;
        (ids[i], ids[j]) = (ids[j], ids[i]);
        SaveOrder(ids);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || sender is not FrameworkElement { Tag: long id }) return;
        AppServices.Rankings.RemoveGame(_current.Id, id);
        LoadEntries();
        RefreshListInfo();
    }

    private void RefreshListInfo() => LoadList(_current?.Id);

    // ---------- Adicionar jogo ----------

    private void AddBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        var inRanking = _rows.Select(r => r.Game.Id).ToHashSet();
        sender.ItemsSource = q.Length == 0 ? null : _library
            .Where(g => !inRanking.Contains(g.Id) && g.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(g => g.Title.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ThenBy(g => g.Title)
            .Take(12).ToList();
    }

    private void AddBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Game g) sender.Text = g.Title;
    }

    private void AddBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_current is null) return;
        var game = args.ChosenSuggestion as Game
                   ?? _library.FirstOrDefault(g => string.Equals(g.Title, args.QueryText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (game is null)
        {
            ShowInfo(InfoBarSeverity.Warning, "Escolha um jogo da lista de sugestões (só jogos da sua biblioteca podem entrar no ranking).");
            return;
        }
        if (!AppServices.Rankings.AddGame(_current.Id, game.Id))
        {
            ShowInfo(InfoBarSeverity.Warning, _rows.Any(r => r.Game.Id == game.Id)
                ? $"{game.Title} já está neste ranking."
                : $"Este ranking aceita no máximo {_current.MaxItems} jogos. Tire um antes de adicionar outro.");
            return;
        }
        sender.Text = "";
        sender.ItemsSource = null;
        Info.IsOpen = false;
        LoadEntries();
        RefreshListInfo();
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        Info.Severity = severity;
        Info.Message = message;
        Info.IsOpen = true;
    }

    // ---------- Criar / editar / apagar ----------

    private async void NewRanking_Click(object sender, RoutedEventArgs e)
    {
        var r = new Ranking();
        if (!await EditDialogAsync(r, isNew: true)) return;
        LoadList(r.Id);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (await EditDialogAsync(_current, isNew: false)) LoadList(_current.Id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (!await Dialogs.ConfirmAsync("Apagar ranking?", $"\"{DisplayName(_current)}\" será apagado. Os jogos continuam na biblioteca.", "Apagar", destructive: true)) return;
        AppServices.Rankings.Delete(_current.Id);
        _current = null;
        LoadList();
    }

    private async Task<bool> EditDialogAsync(Ranking r, bool isNew)
    {
        var name = new TextBox { Header = "Nome", Text = r.Name };
        var desc = new TextBox { Header = "Descrição (opcional)", Text = r.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };
        var year = new NumberBox { Header = "Ano (opcional, ex.: para \"Melhores jogos do ano\")", Minimum = 1970, Maximum = 2100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = r.Year ?? double.NaN };
        var max = new NumberBox { Header = "Limite de jogos (opcional)", Minimum = 1, Maximum = 500, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = r.MaxItems ?? double.NaN };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = isNew ? "Novo ranking" : "Editar ranking",
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel { Spacing = 12, Width = 420, Children = { name, desc, year, max, error } },
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var current = isNew ? 0 : AppServices.Rankings.Entries(r.Id).Count;
                int? newMax = double.IsNaN(max.Value) ? null : (int)max.Value;
                if (newMax is { } m && m < current)
                    throw new ArgumentException($"O ranking já tem {current} jogos. Tire alguns antes de diminuir o limite.");
                r.Name = name.Text.Trim();
                r.Description = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text.Trim();
                r.Year = double.IsNaN(year.Value) ? null : (int)year.Value;
                r.MaxItems = newMax;
                if (isNew)
                {
                    r.Id = AppServices.Rankings.Create(r.Name, r.Description, r.MaxItems);
                }
                AppServices.Rankings.Update(r);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
            }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ---------- Exportar imagem ----------

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _rows.Count == 0) return;
        var poster = BuildPoster(_current, _rows.ToList());
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Exportar ranking",
            PrimaryButtonText = "Salvar PNG…",
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { MaxHeight = 560, Content = poster },
        };
        string? saved = null;
        Exception? failure = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { saved = await ImageExport.SavePngAsync(poster, DisplayName(_current)); }
            catch (Exception ex) { failure = ex; Log.Error("Falha ao exportar ranking", ex); }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (failure is not null) ShowInfo(InfoBarSeverity.Error, "Não foi possível salvar a imagem: " + failure.Message);
        else if (saved is not null) ShowInfo(InfoBarSeverity.Success, "Imagem salva em " + saved);
    }

    /// <summary>Pôster com fundo próprio e tema escuro fixo (a imagem sai igual em qualquer tema).</summary>
    private static FrameworkElement BuildPoster(Ranking r, List<RankingRow> rows)
    {
        var white = new SolidColorBrush(Colors.White);
        var muted = new SolidColorBrush(ColorHelper.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        var accent = (Brush)Application.Current.Resources["GhAccentBrush"];
        var list = new StackPanel { Spacing = 10 };
        foreach (var row in rows)
        {
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock { Text = row.PositionText, FontSize = row.Position <= 3 ? 30 : 24, FontWeight = FontWeights.Bold, Foreground = accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            var cover = new GameCoverCard { CoverWidth = row.Position <= 3 ? 56 : 44, ShowCaption = false, Game = row.Game };
            Grid.SetColumn(cover, 1);
            g.Children.Add(cover);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            text.Children.Add(new TextBlock { Text = row.Game.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = row.Info, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 2);
            g.Children.Add(text);
            list.Children.Add(g);
        }

        var header = new Grid();
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "MEUS RANKINGS", FontSize = 12, FontWeight = FontWeights.SemiBold, CharacterSpacing = 200, Foreground = accent });
        titles.Children.Add(new TextBlock { Text = DisplayName(r), FontSize = 30, FontWeight = FontWeights.Bold, Foreground = white, TextWrapping = TextWrapping.Wrap });
        var who = AppServices.Profiles.Get().DisplayName;
        if (!string.IsNullOrWhiteSpace(who)) titles.Children.Add(new TextBlock { Text = who, Foreground = muted });
        header.Children.Add(titles);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.75 };
        brand.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Logo256.png")), Width = 20, Height = 20 });
        brand.Children.Add(new TextBlock { Text = "GameHub", FontWeight = FontWeights.SemiBold, Foreground = white, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brand);

        var body = new StackPanel { Spacing = 20 };
        body.Children.Add(header);
        body.Children.Add(list);

        var bg = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        bg.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x1A, 0x14, 0x30), Offset = 0 });
        bg.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x10, 0x16, 0x24), Offset = 0.6 });
        bg.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x0B, 0x20, 0x30), Offset = 1 });
        return new Border { Width = 600, Padding = new Thickness(28), CornerRadius = new CornerRadius(16), Background = bg, RequestedTheme = ElementTheme.Dark, Child = body };
    }
}
