using System.Globalization;
using GameHub.App.Controls;
using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Journey;
using GameHub.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace GameHub.App.Pages;

/// <summary>Linha da linha do tempo, já formatada para a tela.</summary>
public sealed class TimelineRow
{
    public Game Game { get; init; } = null!;
    public string DayText { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string Info { get; init; } = "";
    public string? Excerpt { get; init; }
    public Visibility ExcerptVisibility => string.IsNullOrWhiteSpace(Excerpt) ? Visibility.Collapsed : Visibility.Visible;
    public string RatingText => Fmt.Rating(Game.PersonalRating);
    public Visibility RatingVisibility => Game.PersonalRating is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Grupo (ano) da linha do tempo.</summary>
public sealed class TimelineGroup : List<TimelineRow>
{
    public string Key { get; }
    public TimelineGroup(string key, IEnumerable<TimelineRow> rows) : base(rows) => Key = key;
}

public sealed partial class JourneyPage : Page
{
    private static readonly CultureInfo Pt = Fmt.PtBr;
    private bool _ready;
    private Game? _best;
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly? _selectedDay;
    private string _tab = "dash";
    private static string _lastTab = "dash";

    public JourneyPage()
    {
        InitializeComponent();
        TStatusBox.Items.Add(new ComboBoxItem { Content = "Todos", IsSelected = true });
        foreach (var s in GameStatusText.All.Where(s => s != GameStatus.None))
            TStatusBox.Items.Add(new ComboBoxItem { Content = s.ToDisplay(), Tag = s });
        for (var m = 1; m <= 12; m++)
            TMonthBox.Items.Add(new ComboBoxItem { Content = Pt.TextInfo.ToTitleCase(Pt.DateTimeFormat.GetMonthName(m)), Tag = m });
        TMonthBox.SelectedIndex = DateTime.Today.Month - 1;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ready = false;
        FillFilterLists();
        _ready = true;
        var tab = e.Parameter as string ?? _lastTab;
        var item = Tabs.Items.FirstOrDefault(i => (string)i.Tag == tab) ?? Tabs.Items[0];
        if (Tabs.SelectedItem == item) ShowTab(tab);
        else Tabs.SelectedItem = item;
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag) ShowTab(tag);
    }

    private void ShowTab(string tag)
    {
        _tab = _lastTab = tag;
        DashPanel.Visibility = tag == "dash" ? Visibility.Visible : Visibility.Collapsed;
        TimelinePanel.Visibility = tag == "timeline" ? Visibility.Visible : Visibility.Collapsed;
        YearPanel.Visibility = tag == "year" ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = tag == "calendar" ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            switch (tag)
            {
                case "dash": LoadDashboard(); break;
                case "timeline": LoadTimeline(); break;
                case "year": LoadYear(); break;
                case "calendar": LoadCalendar(); break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao montar Minha Jornada", ex);
        }
    }

    // ================= Painel =================

    private void LoadDashboard()
    {
        var d = AppServices.Journey.Dashboard();
        var s = d.Library;
        DTotal.Text = s.TotalGames.ToString(Pt);
        DFinished.Text = s.Finished.ToString(Pt);
        DPlaying.Text = s.Playing.ToString(Pt);
        DAbandoned.Text = s.Abandoned.ToString(Pt);
        DHours.Text = Fmt.Hours(s.TotalPlaytimeMinutes);
        DAvg.Text = s.AverageRating is { } avg ? avg.ToString("0.0", Pt) : "—";
        DGenre.Text = s.FavoriteGenre ?? "—";
        DPlatform.Text = d.TopPlatform ?? "—";

        _best = s.BestRatedGame;
        DBestCard.Visibility = _best is null ? Visibility.Collapsed : Visibility.Visible;
        if (_best is not null)
        {
            DBestCover.Game = _best;
            DBestTitle.Content = _best.Title;
            DBestInfo.Text = $"Nota {Fmt.Rating(_best.PersonalRating)} · {Fmt.Hours(_best.TotalPlaytimeMinutes)} jogadas";
        }

        ChartPerYear.SetItems(d.FinishedPerYear.TakeLast(10).ToList());
        ChartGenres.SetItems(d.TopGenres);
        ChartStatus.SetItems(d.StatusBreakdown);
    }

    private void DBest_Click(object sender, RoutedEventArgs e)
    {
        if (_best is not null) App.MainWindow!.Navigate(typeof(GameDetailsPage), _best.Id);
    }

    // ================= Linha do tempo =================

    private void FillFilterLists()
    {
        var years = AppServices.Journey.YearsWithActivity();
        if (!years.Contains(DateTime.Today.Year)) years.Insert(0, DateTime.Today.Year);

        TYearBox.Items.Clear();
        foreach (var y in years) TYearBox.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
        TYearBox.SelectedIndex = 0;

        YearBox.Items.Clear();
        foreach (var y in years) YearBox.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
        YearBox.SelectedIndex = 0;

        TGenreBox.Items.Clear();
        TGenreBox.Items.Add(new ComboBoxItem { Content = "Todos" });
        foreach (var g in AppServices.Games.DistinctGenres()) TGenreBox.Items.Add(new ComboBoxItem { Content = g, Tag = g });
        TGenreBox.SelectedIndex = 0;

        TSourceBox.Items.Clear();
        TSourceBox.Items.Add(new ComboBoxItem { Content = "Todas" });
        foreach (var src in AppServices.Games.DistinctSources()) TSourceBox.Items.Add(new ComboBoxItem { Content = src, Tag = src });
        TSourceBox.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => OnFilterChanged();
    private void Date_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => OnFilterChanged();
    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => OnFilterChanged();
    private void Check_Changed(object sender, RoutedEventArgs e) => OnFilterChanged();

    private void OnFilterChanged()
    {
        if (!_ready) return;
        if (_tab == "timeline") LoadTimeline();
    }

    private static T? TagOf<T>(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag is T t ? t : default;

    private void LoadTimeline()
    {
        var period = TagOf<string>(PeriodBox) ?? "all";
        TYearBox.Visibility = period is "year" or "month" ? Visibility.Visible : Visibility.Collapsed;
        TMonthBox.Visibility = period == "month" ? Visibility.Visible : Visibility.Collapsed;
        TFrom.Visibility = TTo.Visibility = period == "custom" ? Visibility.Visible : Visibility.Collapsed;

        DateOnly? from = null, to = null;
        var year = TagOf<int>(TYearBox);
        if (period == "year" && year > 0) { from = new DateOnly(year, 1, 1); to = new DateOnly(year, 12, 31); }
        else if (period == "month" && year > 0)
        {
            var m = Math.Max(1, TagOf<int>(TMonthBox));
            from = new DateOnly(year, m, 1);
            to = from.Value.AddMonths(1).AddDays(-1);
        }
        else if (period == "custom")
        {
            if (TFrom.Date is { } f) from = DateOnly.FromDateTime(f.Date);
            if (TTo.Date is { } t) to = DateOnly.FromDateTime(t.Date);
        }

        GameStatus? status = (TStatusBox.SelectedItem as ComboBoxItem)?.Tag is GameStatus st ? st : null;
        var filter = new JourneyService.TimelineFilter(
            From: from, To: to, Status: status,
            MinRating: double.IsNaN(TMinRating.Value) || TMinRating.Value <= 0 ? null : TMinRating.Value,
            Genre: TagOf<string>(TGenreBox), Source: TagOf<string>(TSourceBox),
            MinMinutes: double.IsNaN(TMinHours.Value) || TMinHours.Value <= 0 ? null : (long)(TMinHours.Value * 60),
            MaxMinutes: double.IsNaN(TMaxHours.Value) || TMaxHours.Value <= 0 ? null : (long)(TMaxHours.Value * 60),
            IncludeUndated: TUndated.IsChecked == true);

        var entries = AppServices.Journey.Timeline(filter);
        var groups = entries
            .GroupBy(e => e.Date?.Year.ToString() ?? "Sem data")
            .Select(g => new TimelineGroup(g.Key, g.Select(ToRow)))
            .ToList();
        var cvs = new CollectionViewSource { IsSourceGrouped = true, Source = groups };
        TimelineList.ItemsSource = cvs.View;
        TimelineEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TimelineRow ToRow(TimelineEntry e)
    {
        var parts = new List<string> { e.Game.Status.ToDisplay() };
        if (e.Started is { } s) parts.Add("começou " + s.ToString("dd/MM/yyyy", Pt));
        if (e.Finished is { } f) parts.Add("terminou " + f.ToString("dd/MM/yyyy", Pt));
        if (e.Started is { } s2 && e.Finished is { } f2 && f2 >= s2)
        {
            var days = f2.DayNumber - s2.DayNumber;
            parts.Add(days == 0 ? "no mesmo dia" : $"em {days} dia{(days == 1 ? "" : "s")}");
        }
        if (e.Game.TotalPlaytimeMinutes > 0) parts.Add(Fmt.Hours(e.Game.TotalPlaytimeMinutes));
        parts.Add(e.Game.Source);
        return new TimelineRow
        {
            Game = e.Game,
            DayText = e.Date is { } d ? d.ToString("dd 'de' MMM", Pt) : "—",
            SourceText = e.DateSource,
            Info = string.Join(" · ", parts),
            Excerpt = e.ReviewExcerpt,
        };
    }

    private void Timeline_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TimelineRow r) App.MainWindow!.Navigate(typeof(GameDetailsPage), r.Game.Id);
    }

    // ================= Meu Ano Gamer =================

    private void YearBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && _tab == "year") LoadYear();
    }

    private void LoadYear()
    {
        var year = TagOf<int>(YearBox);
        if (year <= 0) year = DateTime.Today.Year;
        var y = AppServices.Journey.YearInReview(year);
        var profile = AppServices.Profiles.Get();

        YearEmpty.Visibility = y.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        YearPoster.Visibility = y.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        ExportYearButton.IsEnabled = !y.IsEmpty;
        YearInfo.IsOpen = false;
        if (y.IsEmpty) return;

        YTitle.Text = year.ToString();
        YPlayer.Text = profile.DisplayName;
        YPlayed.Text = y.Played.Count.ToString(Pt);
        YStarted.Text = y.Started.Count.ToString(Pt);
        YFinished.Text = y.Finished.Count.ToString(Pt);
        YHours.Text = y.TrackedMinutes > 0 ? Fmt.Hours(y.TrackedMinutes) : "—";
        YHoursLabel.Text = "horas medidas pelo GameHub";

        YBest.Text = y.Best is { } b ? $"{b.Title} ({Fmt.Rating(b.PersonalRating)})" : "Sem notas no ano";
        YBestCover.Game = y.Best;
        YBestCover.Visibility = y.Best is null ? Visibility.Collapsed : Visibility.Visible;
        YWorst.Text = y.Worst is { } w ? $"{w.Title} ({Fmt.Rating(w.PersonalRating)})" : "Precisa de 2 jogos com nota";
        YWorstCover.Game = y.Worst;
        YWorstCover.Visibility = y.Worst is null ? Visibility.Collapsed : Visibility.Visible;
        YGenre.Text = y.TopGenre ?? "—";
        YPlatform.Text = y.TopPlatform ?? "—";
        YChart.SetItems(y.FinishedPerMonth);

        YFinishedList.ItemsSource = y.Finished;
        YFinishedSection.Visibility = y.Finished.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        YFavList.ItemsSource = y.Favorites;
        YFavSection.Visibility = y.Favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        YReviews.Children.Clear();
        foreach (var (game, review) in y.Reviews.Take(6))
        {
            var text = review.Review ?? review.Moments ?? review.Pros ?? review.Comments ?? "";
            YReviews.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = game.Title + (game.PersonalRating is { } r ? $"  ·  {Fmt.Rating(r)}" : ""), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) },
                        new TextBlock { Text = GameHub.Core.Text.HtmlText.Summarize(text, 260) ?? "", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) },
                    },
                },
            });
        }
        YReviewsSection.Visibility = y.Reviews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ExportYear_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await ImageExport.SavePngAsync(YearPoster, $"Meu Ano Gamer {YTitle.Text}");
            if (path is null) return;
            YearInfo.Severity = InfoBarSeverity.Success;
            YearInfo.Message = "Imagem salva em " + path;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao exportar Meu Ano Gamer", ex);
            YearInfo.Severity = InfoBarSeverity.Error;
            YearInfo.Message = "Não foi possível salvar a imagem: " + ex.Message;
        }
        YearInfo.IsOpen = true;
    }

    // ================= Calendário =================

    private static readonly Dictionary<CalendarEventKind, Windows.UI.Color> KindColors = new()
    {
        [CalendarEventKind.Started] = ColorHelper.FromArgb(0xFF, 0x4C, 0xC3, 0x8A),
        [CalendarEventKind.Finished] = ColorHelper.FromArgb(0xFF, 0xFF, 0xB5, 0x47),
        [CalendarEventKind.Memory] = ColorHelper.FromArgb(0xFF, 0x3F, 0xB6, 0xE8),
    };

    private static Brush KindBrush(CalendarEventKind k) =>
        KindColors.TryGetValue(k, out var c) ? new SolidColorBrush(c) : (Brush)Application.Current.Resources["GhAccentBrush"];

    private void PrevMonth_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); _selectedDay = null; LoadCalendar(); }
    private void NextMonth_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); _selectedDay = null; LoadCalendar(); }
    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDay = DateOnly.FromDateTime(DateTime.Today);
        LoadCalendar();
    }

    private void LoadCalendar()
    {
        MonthTitle.Text = Pt.TextInfo.ToTitleCase(_month.ToString("MMMM 'de' yyyy", Pt));
        var last = _month.AddMonths(1).AddDays(-1);
        var events = AppServices.Journey.Calendar(_month, last);
        var byDay = events.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.ToList());

        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        for (var c = 0; c < 7; c++) CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Cabeçalho (domingo primeiro, como no calendário brasileiro).
        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < 7; c++)
        {
            var name = Pt.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)c).TrimEnd('.');
            var h = new TextBlock { Text = Pt.TextInfo.ToTitleCase(name), Style = (Style)Application.Current.Resources["GhMuted"], HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(h, c);
            CalendarGrid.Children.Add(h);
        }

        var offset = (int)_month.DayOfWeek;
        var weeks = (int)Math.Ceiling((offset + last.Day) / 7.0);
        for (var w = 0; w < weeks; w++) CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var day = 1; day <= last.Day; day++)
        {
            var date = new DateOnly(_month.Year, _month.Month, day);
            var cell = offset + day - 1;
            byDay.TryGetValue(date, out var list);

            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (list is not null)
                foreach (var kind in list.Select(x => x.Kind).Distinct())
                    dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = KindBrush(kind) });

            var content = new Grid { RowSpacing = 4 };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(new TextBlock { Text = day.ToString(), FontWeight = date == today ? FontWeights.Bold : FontWeights.Normal, Foreground = date == today ? (Brush)Application.Current.Resources["GhAccentBrush"] : null });
            if (list is not null)
            {
                var games = list.Select(x => x.Game.Title).Distinct().ToList();
                var t = new TextBlock { Text = games.Count == 1 ? games[0] : $"{games.Count} jogos", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(t, 1);
                content.Children.Add(t);
            }
            Grid.SetRow(dots, 2);
            content.Children.Add(dots);

            var b = new Button
            {
                Content = content,
                Tag = date,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Background = _selectedDay == date ? (Brush)Application.Current.Resources["GhAccentSoftBrush"] : (Brush)Application.Current.Resources["GhCardBrush"],
                BorderBrush = (Brush)Application.Current.Resources["GhCardBorderBrush"],
                CornerRadius = new CornerRadius(8),
            };
            b.Click += Day_Click;
            Grid.SetRow(b, 1 + cell / 7);
            Grid.SetColumn(b, cell % 7);
            CalendarGrid.Children.Add(b);
        }

        ShowDay(byDay);
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly d })
        {
            _selectedDay = d;
            LoadCalendar();
        }
    }

    private void ShowDay(Dictionary<DateOnly, List<CalendarEvent>> byDay)
    {
        DayEvents.Children.Clear();
        if (_selectedDay is not { } day || day.Year != _month.Year || day.Month != _month.Month)
        {
            var total = byDay.Values.Sum(v => v.Count);
            DayTitle.Text = total == 0 ? "Nenhum registro neste mês" : "Clique em um dia para ver o que você jogou";
            return;
        }
        DayTitle.Text = Pt.TextInfo.ToTitleCase(day.ToString("dddd, d 'de' MMMM", Pt));
        if (!byDay.TryGetValue(day, out var list))
        {
            DayEvents.Children.Add(new TextBlock { Text = "Nada registrado neste dia.", Style = (Style)Application.Current.Resources["GhMuted"] });
            return;
        }
        foreach (var ev in list)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = KindBrush(ev.Kind), VerticalAlignment = VerticalAlignment.Center });
            var cover = new GameCoverCard { CoverWidth = 40, ShowCaption = false, Game = ev.Game };
            Grid.SetColumn(cover, 1);
            row.Children.Add(cover);
            var link = new HyperlinkButton { Content = $"{ev.Label}: {ev.Game.Title}", Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Tag = ev.Game.Id };
            link.Click += (_, _) => App.MainWindow!.Navigate(typeof(GameDetailsPage), ev.Game.Id);
            Grid.SetColumn(link, 2);
            row.Children.Add(link);
            DayEvents.Children.Add(row);
        }
    }
}
