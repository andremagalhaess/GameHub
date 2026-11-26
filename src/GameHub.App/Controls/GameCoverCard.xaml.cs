using GameHub.App.Helpers;
using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameHub.App.Controls;

/// <summary>Capa vertical (2:3) de um jogo com nome, status e favorito.</summary>
public sealed partial class GameCoverCard : UserControl
{
    public static readonly DependencyProperty GameProperty = DependencyProperty.Register(
        nameof(Game), typeof(Game), typeof(GameCoverCard), new PropertyMetadata(null, (d, _) => ((GameCoverCard)d).Refresh()));

    public static readonly DependencyProperty CoverWidthProperty = DependencyProperty.Register(
        nameof(CoverWidth), typeof(double), typeof(GameCoverCard), new PropertyMetadata(170.0, (d, _) => ((GameCoverCard)d).Resize()));

    public static readonly DependencyProperty ShowCaptionProperty = DependencyProperty.Register(
        nameof(ShowCaption), typeof(bool), typeof(GameCoverCard), new PropertyMetadata(true, (d, _) => ((GameCoverCard)d).Refresh()));

    public Game? Game { get => (Game?)GetValue(GameProperty); set => SetValue(GameProperty, value); }
    public double CoverWidth { get => (double)GetValue(CoverWidthProperty); set => SetValue(CoverWidthProperty, value); }
    public bool ShowCaption { get => (bool)GetValue(ShowCaptionProperty); set => SetValue(ShowCaptionProperty, value); }

    public GameCoverCard()
    {
        InitializeComponent();
        Resize();
    }

    private void Resize()
    {
        Width = CoverWidth;
        CoverHost.Width = CoverWidth;
        CoverHost.Height = Math.Round(CoverWidth * 1.5);
    }

    private void Refresh()
    {
        var g = Game;
        Cover.Source = null;
        if (g is null) return;
        var source = ImageHelper.Local(g.CoverPath, (int)Math.Ceiling(CoverWidth * 1.25));
        Cover.Source = source;
        Placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderText.Text = g.Title;
        TitleText.Text = g.Title;
        SubText.Text = g.PlaytimeMinutes > 0 ? $"{Fmt.Hours(g.PlaytimeMinutes)} jogadas" : (g.ReleaseYear?.ToString() ?? g.Developer ?? "");
        StatusText.Text = g.Status.ToDisplay();
        StatusBadge.Visibility = g.Status == GameStatus.None ? Visibility.Collapsed : Visibility.Visible;
        FavIcon.Visibility = g.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        Caption.Visibility = ShowCaption ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, g.Title);
    }

    private void Cover_ImageFailed(object sender, ExceptionRoutedEventArgs e) => Placeholder.Visibility = Visibility.Visible;
}
