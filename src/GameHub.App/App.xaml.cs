using GameHub.App.Services;
using Microsoft.UI.Xaml;

namespace GameHub.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Error("Exceção não tratada", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        ThemeService.ApplyAccentAtStartup(AppServices.Profiles.Get().AccentColor);
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
