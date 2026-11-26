using GameHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace GameHub.App.Controls;

/// <summary>
/// Gráfico de barras simples desenhado com retângulos (sem bibliotecas de gráficos, para manter o app leve).
/// Horizontal: uma linha por item (rótulo, barra, valor). Vertical: colunas lado a lado (ex.: meses).
/// </summary>
public sealed class BarChart : Grid
{
    public IReadOnlyList<CountItem> Items { get; private set; } = Array.Empty<CountItem>();
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public double BarThickness { get; set; } = 14;
    public double ChartHeight { get; set; } = 140;
    public Func<double, string> FormatValue { get; set; } = v => v.ToString("0.#", Helpers.Fmt.PtBr);
    public string EmptyText { get; set; } = "Ainda não há dados para este gráfico.";

    public void SetItems(IReadOnlyList<CountItem> items)
    {
        Items = items;
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        RowSpacing = 0;
        ColumnSpacing = 0;

        if (items.Count == 0 || items.All(i => i.Value <= 0))
        {
            Children.Add(new TextBlock { Text = EmptyText, Style = (Style)Application.Current.Resources["GhMuted"], TextWrapping = TextWrapping.Wrap });
            return;
        }
        var max = items.Max(i => i.Value);
        if (Orientation == Orientation.Horizontal) BuildHorizontal(items, max);
        else BuildVertical(items, max);
    }

    private static Brush Accent => (Brush)Application.Current.Resources["GhAccentBrush"];
    private static Brush Track => (Brush)Application.Current.Resources["GhAccentSoftBrush"];

    private void BuildHorizontal(IReadOnlyList<CountItem> items, double max)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MaxWidth = 170 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnSpacing = 12;
        RowSpacing = 8;
        for (var i = 0; i < items.Count; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = items[i].Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(label, items[i].Label);
            SetRow(label, i);
            Children.Add(label);

            var bar = Bar(items[i].Value, max, horizontal: true);
            SetRow(bar, i); SetColumn(bar, 1);
            Children.Add(bar);

            var value = new TextBlock { Text = FormatValue(items[i].Value), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            SetRow(value, i); SetColumn(value, 2);
            Children.Add(value);
        }
    }

    private void BuildVertical(IReadOnlyList<CountItem> items, double max)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(ChartHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnSpacing = 6;
        for (var i = 0; i < items.Count; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var value = new TextBlock
            {
                Text = items[i].Value > 0 ? FormatValue(items[i].Value) : "",
                FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            };
            SetColumn(value, i);
            Children.Add(value);

            var bar = Bar(items[i].Value, max, horizontal: false);
            SetRow(bar, 1); SetColumn(bar, i);
            Children.Add(bar);

            var label = new TextBlock
            {
                Text = items[i].Label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)Application.Current.Resources["GhMutedTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis,
            };
            SetRow(label, 2); SetColumn(label, i);
            Children.Add(label);
        }
    }

    /// <summary>Barra proporcional feita com duas células em estrela (valor / restante), sem medir pixels.</summary>
    private Grid Bar(double value, double max, bool horizontal)
    {
        var filled = Math.Max(0, value);
        var rest = Math.Max(0, max - value);
        var g = new Grid { Background = Track, CornerRadius = new CornerRadius(BarThickness / 2) };
        var fill = new Rectangle { Fill = Accent, RadiusX = BarThickness / 2, RadiusY = BarThickness / 2 };
        if (horizontal)
        {
            g.Height = BarThickness;
            g.VerticalAlignment = VerticalAlignment.Center;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(filled, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rest, GridUnitType.Star) });
        }
        else
        {
            g.Width = BarThickness * 1.6;
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rest, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(filled, GridUnitType.Star) });
            SetRow(fill, 1);
        }
        if (filled > 0) g.Children.Add(fill);
        return g;
    }
}
