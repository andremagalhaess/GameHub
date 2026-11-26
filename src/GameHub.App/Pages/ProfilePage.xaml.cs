using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Data;
using GameHub.Core.Metadata;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class ProfilePage : Page
{
    private PlayerProfile _profile = new();
    private Game? _current;
    private string? _editAvatar, _editBanner;

    public ProfilePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    private void Load()
    {
        _profile = AppServices.Profiles.Get();
        var games = AppServices.Games;
        var s = games.Stats();

        NameText.Text = _profile.DisplayName;
        UserText.Text = string.IsNullOrWhiteSpace(_profile.Username) ? "" : "@" + _profile.Username;
        Avatar.DisplayName = _profile.DisplayName;
        Avatar.ProfilePicture = ImageHelper.Local(_profile.AvatarPath, 208);
        BannerImage.Source = ImageHelper.Local(_profile.BannerPath, 1800);
        BioText.Text = string.IsNullOrWhiteSpace(_profile.Bio) ? "Escreva uma biografia em Editar perfil." : _profile.Bio;
        BioText.Opacity = string.IsNullOrWhiteSpace(_profile.Bio) ? 0.6 : 1;

        _current = _profile.CurrentGameId is { } id ? games.Get(id) : null;
        CurrentCard.Visibility = _current is null ? Visibility.Collapsed : Visibility.Visible;
        if (_current is not null)
        {
            CurrentCover.Game = _current;
            CurrentTitle.Content = _current.Title;
            CurrentInfo.Text = $"{Fmt.Hours(_current.PlaytimeMinutes)} jogadas · {_current.Status.ToDisplay()}";
        }

        StTotal.Text = s.TotalGames.ToString();
        StFinished.Text = s.Finished.ToString();
        StHours.Text = Fmt.Hours(s.TotalPlaytimeMinutes);
        StAvg.Text = s.AverageRating is { } avg ? avg.ToString("0.0", Fmt.PtBr) : "—";
        StFav.Text = s.Favorites.ToString();
        StPlaying.Text = s.Playing.ToString();
        StBest.Text = s.BestRatedGame?.Title ?? "—";
        StGenre.Text = s.FavoriteGenre ?? "—";
        RatingsHint.Visibility = s.RatedGames == 0 ? Visibility.Visible : Visibility.Collapsed;

        Fill(FavSection, FavList, games.Query(new GameQuery(FavoritesOnly: true)));
        Fill(PlayingSection, PlayingList, games.Query(new GameQuery(Status: GameStatus.Playing)));
        Fill(FinishedSection, FinishedList, games.Query(new GameQuery(Status: GameStatus.Beaten)).Concat(games.Query(new GameQuery(Status: GameStatus.Completed100))).ToList());
        Fill(AbandonedSection, AbandonedList, games.Query(new GameQuery(Status: GameStatus.Abandoned)));
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

    private void Current_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) App.MainWindow!.Navigate(typeof(GameDetailsPage), _current.Id);
    }

    // ---------- Edição ----------

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        _editAvatar = _profile.AvatarPath;
        _editBanner = _profile.BannerPath;
        EditName.Text = _profile.DisplayName;
        EditUser.Text = _profile.Username;
        EditBio.Text = _profile.Bio ?? "";
        EditAvatar.DisplayName = _profile.DisplayName;
        EditAvatar.ProfilePicture = ImageHelper.Local(_editAvatar, 128);
        BannerState.Text = _editBanner is null ? "Sem banner" : "Banner definido";
        EditAccent.Color = ThemeService.ParseColor(_profile.AccentColor);

        var all = AppServices.Games.All();
        var none = new Game { Id = 0, Title = "(nenhum)" };
        var options = new List<Game> { none };
        options.AddRange(all);
        EditCurrent.ItemsSource = options;
        EditCurrent.SelectedItem = options.FirstOrDefault(g => g.Id == _profile.CurrentGameId) ?? none;
        EditError.Visibility = Visibility.Collapsed;

        EditDialog.XamlRoot = XamlRoot;
        EditDialog.RequestedTheme = ActualTheme;
        await EditDialog.ShowAsync();
    }

    private void EditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(EditName.Text))
        {
            args.Cancel = true;
            EditError.Text = "O nome de exibição é obrigatório.";
            EditError.Visibility = Visibility.Visible;
            return;
        }
        var p = _profile;
        p.DisplayName = EditName.Text.Trim();
        p.Username = EditUser.Text.Trim();
        p.Bio = string.IsNullOrWhiteSpace(EditBio.Text) ? null : EditBio.Text.Trim();
        p.AvatarPath = _editAvatar;
        p.BannerPath = _editBanner;
        p.AccentColor = ThemeService.ToHex(EditAccent.Color);
        p.CurrentGameId = EditCurrent.SelectedItem is Game { Id: > 0 } g ? g.Id : null;
        try
        {
            AppServices.Profiles.Save(p);
            ThemeService.ApplyAccentLive(p.AccentColor);
            Load();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            EditError.Text = ex.Message;
            EditError.Visibility = Visibility.Visible;
        }
    }

    private async void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickAndImport();
        if (path is null) return;
        _editAvatar = path;
        EditAvatar.ProfilePicture = ImageHelper.Local(path, 128);
    }

    private void RemoveAvatar_Click(object sender, RoutedEventArgs e)
    {
        _editAvatar = null;
        EditAvatar.ProfilePicture = null;
    }

    private async void PickBanner_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickAndImport();
        if (path is null) return;
        _editBanner = path;
        BannerState.Text = "Novo banner escolhido";
    }

    private void RemoveBanner_Click(object sender, RoutedEventArgs e)
    {
        _editBanner = null;
        BannerState.Text = "Sem banner";
    }

    private async Task<string?> PickAndImport()
    {
        // O seletor de arquivos abre por cima do diálogo; o diálogo continua aberto.
        var file = await Pickers.PickImageAsync();
        if (file is null) return null;
        try { return await AppServices.Images.ImportLocalAsync(file, ImageKind.Icon); }
        catch (Exception ex)
        {
            EditError.Text = ex.Message;
            EditError.Visibility = Visibility.Visible;
            return null;
        }
    }
}
