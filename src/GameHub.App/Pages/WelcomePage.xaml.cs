using GameHub.App.Helpers;
using GameHub.App.Services;
using GameHub.Core.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameHub.App.Pages;

public sealed partial class WelcomePage : Page
{
    private string? _avatarPath;

    public WelcomePage() => InitializeComponent();

    private async void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        var file = await Pickers.PickImageAsync();
        if (file is null) return;
        try
        {
            _avatarPath = await AppServices.Images.ImportLocalAsync(file, ImageKind.Icon);
            Avatar.ProfilePicture = ImageHelper.Local(_avatarPath, 144);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        StartButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        Avatar.DisplayName = NameBox.Text;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = AppServices.Profiles.Get();
            p.DisplayName = NameBox.Text.Trim();
            p.Username = string.IsNullOrWhiteSpace(UserBox.Text) ? NameBox.Text.Trim().ToLowerInvariant().Replace(' ', '_') : UserBox.Text.Trim();
            p.AvatarPath = _avatarPath;
            AppServices.Profiles.Save(p);
            App.MainWindow!.FinishWelcome(importNow: ImportBox.IsChecked == true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
