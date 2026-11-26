using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameHub.App.Pages;

public sealed partial class GameEditPage : Page
{
    private Game? _game;

    public GameEditPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is long id && AppServices.Games.Get(id) is { } g)
        {
            _game = g;
            Header.Text = $"Editar · {g.Title}";
            Editor.Load(g);
        }
        else App.MainWindow!.GoBackOr(typeof(LibraryPage));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => App.MainWindow!.GoBackOr(typeof(LibraryPage));

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        if (Editor.Validate() is { } error) { Editor.ShowError(error); return; }
        SaveButton.IsEnabled = false;
        Busy.IsActive = true;
        try
        {
            var warnings = new List<string>();
            var g = await Editor.CommitAsync(AppServices.Images, warnings);
            AppServices.Games.Update(g);
            if (warnings.Count > 0) await Dialogs.InfoAsync("Salvo com avisos", string.Join("\n", warnings));
            App.MainWindow!.GoBackOr(typeof(LibraryPage));
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar jogo", ex);
            Editor.ShowError($"Não foi possível salvar: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
            Busy.IsActive = false;
        }
    }
}
