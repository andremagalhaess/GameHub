using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameHub.App.Helpers;

public static class Dialogs
{
    private static XamlRoot Root => App.MainWindow!.Content.XamlRoot;

    public static async Task<bool> ConfirmAsync(string title, string message, string primary, string close = "Cancelar", bool destructive = false)
    {
        var d = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
            RequestedTheme = (App.MainWindow!.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
        };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task InfoAsync(string title, string message)
    {
        var d = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
            RequestedTheme = (App.MainWindow!.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
        };
        await d.ShowAsync();
    }
}
