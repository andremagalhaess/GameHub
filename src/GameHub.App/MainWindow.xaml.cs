using GameHub.App.Pages;
using GameHub.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Routes = new()
    {
        ["home"] = typeof(HomePage),
        ["library"] = typeof(LibraryPage),
        ["add"] = typeof(AddGamePage),
        ["profile"] = typeof(ProfilePage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GameHub.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = 960;
            p.PreferredMinimumHeight = 620;
        }

        ApplyAppearance();

        if (AppServices.Profiles.Get().IsCreated)
            Navigate(typeof(HomePage));
        else
            ShowWelcome();
    }

    /// <summary>Tema, transparência (Mica) e fundo.</summary>
    public void ApplyAppearance()
    {
        RootGrid.RequestedTheme = ThemeService.CurrentTheme;
        ThemeService.ApplyBackdrop(this);
        RootGrid.Background = ThemeService.TransparencyEnabled
            ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        var dark = RootGrid.ActualTheme == ElementTheme.Dark || ThemeService.CurrentTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    public void ShowWelcome()
    {
        Nav.IsPaneVisible = false;
        ContentFrame.Navigate(typeof(WelcomePage));
    }

    public void FinishWelcome()
    {
        Nav.IsPaneVisible = true;
        ContentFrame.BackStack.Clear();
        Navigate(typeof(HomePage));
        ContentFrame.BackStack.Clear();
    }

    public void Navigate(Type page, object? parameter = null)
    {
        NavigationTransitionInfo transition = ThemeService.AnimationsEnabled
            ? new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
        ContentFrame.Navigate(page, parameter, transition);
    }

    public void GoBackOr(Type fallback)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        else Navigate(fallback);
    }

    /// <summary>Remove a página atual do histórico (ex.: depois de concluir o fluxo de adicionar).</summary>
    public void ReplaceCurrent(Type page, object? parameter = null)
    {
        Navigate(page, parameter);
        if (ContentFrame.BackStack.Count > 0) ContentFrame.BackStack.RemoveAt(ContentFrame.BackStack.Count - 1);
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag && Routes.TryGetValue(tag, out var page))
        {
            if (ContentFrame.CurrentSourcePageType == page && page != typeof(AddGamePage)) return;
            Navigate(page);
        }
    }

    private void Nav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        Nav.IsBackEnabled = ContentFrame.CanGoBack;
        var tag = Routes.FirstOrDefault(r => r.Value == e.SourcePageType).Key;
        if (e.SourcePageType == typeof(GameDetailsPage) || e.SourcePageType == typeof(GameEditPage)) tag = "library";
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
        Nav.SelectedItem = null;
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        Log.Error($"Falha ao abrir {e.SourcePageType?.FullName}", e.Exception);
        e.Handled = true;
    }
}
