using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Data;
using GameHub.Core.Launching;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class HomePage : Page
{
    private Game? _continue;

    public HomePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    private void Load()
    {
        var profile = AppServices.Profiles.Get();
        var games = AppServices.Games;
        var stats = games.Stats();

        GreetingText.Text = $"{Fmt.Greeting()}, {profile.DisplayName}";
        Avatar.DisplayName = profile.DisplayName;
        Avatar.ProfilePicture = ImageHelper.Local(profile.AvatarPath, 128);
        SummaryText.Text = stats.TotalGames == 0
            ? "Sua biblioteca ainda está vazia."
            : $"{stats.TotalGames} jogos · {Fmt.Hours(stats.TotalPlaytimeMinutes)} jogadas" + (stats.AverageRating is { } avg ? $" · nota média {avg.ToString("0.0", Fmt.PtBr)}" : "");

        StatTotal.Text = stats.TotalGames.ToString();
        StatHours.Text = Fmt.Hours(stats.TotalPlaytimeMinutes);
        StatFinished.Text = stats.Finished.ToString();
        StatPlaying.Text = stats.Playing.ToString();

        // Continuar jogando: o jogo atual do perfil ou o último iniciado.
        _continue = profile.CurrentGameId is { } cur ? games.Get(cur) : null;
        _continue ??= games.Query(new GameQuery(Sort: GameSort.RecentlyPlayed, Limit: 1)).FirstOrDefault(g => g.LastPlayedAt is not null);
        if (_continue is not null)
        {
            ContinueCard.Visibility = Visibility.Visible;
            ContinueTitle.Text = _continue.Title;
            ContinueInfo.Text = $"Última vez: {Fmt.When(_continue.LastPlayedAt)} · {Fmt.Hours(_continue.TotalPlaytimeMinutes)} jogadas";
            ContinueBackground.Source = ImageHelper.Local(_continue.BackgroundPath ?? _continue.CoverPath, 1600);
            ContinuePlay.IsEnabled = _continue.CanLaunch;
        }
        else ContinueCard.Visibility = Visibility.Collapsed;

        Fill(PlayingSection, PlayingList, games.Query(new GameQuery(Status: GameStatus.Playing, Sort: GameSort.RecentlyPlayed, Limit: 20)));
        Fill(FavoritesSection, FavoritesList, games.Query(new GameQuery(FavoritesOnly: true, Limit: 20)));
        Fill(RecentSection, RecentList, games.Query(new GameQuery(Sort: GameSort.RecentlyAdded, Limit: 20)));
        var beaten = games.Query(new GameQuery(Status: GameStatus.Beaten))
            .Concat(games.Query(new GameQuery(Status: GameStatus.Completed100)))
            .OrderByDescending(g => g.UpdatedAt).Take(20).ToList();
        Fill(BeatenSection, BeatenList, beaten);
        Fill(ReviewsSection, ReviewsList, AppServices.Reviews.Latest(games, 12).Select(x => x.Game).ToList());

        EmptyCard.Visibility = stats.TotalGames == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Fill(FrameworkElement section, ListView list, List<Game> items)
    {
        list.ItemsSource = items;
        section.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Shelf_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game g) App.MainWindow!.Navigate(typeof(GameDetailsPage), g.Id);
    }

    private void ContinuePlay_Click(object sender, RoutedEventArgs e)
    {
        if (_continue is null) return;
        var r = AppServices.Launcher.Launch(_continue);
        LaunchInfo.Severity = r.Outcome == LaunchOutcome.Started ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        LaunchInfo.Message = r.Outcome == LaunchOutcome.Started ? $"Iniciando {_continue.Title}…" : r.Message;
        LaunchInfo.IsOpen = true;
    }

    private void ContinueDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_continue is not null) App.MainWindow!.Navigate(typeof(GameDetailsPage), _continue.Id);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => App.MainWindow!.Navigate(typeof(AddGamePage));
}
