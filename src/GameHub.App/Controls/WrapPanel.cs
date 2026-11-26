using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace GameHub.App.Controls;

/// <summary>Painel que quebra linha (o WinUI não traz um WrapPanel pronto). Sem virtualização: use em listas curtas.</summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > available.Width)
            {
                y += rowH + VerticalSpacing;
                x = 0; rowH = 0;
            }
            x += s.Width + HorizontalSpacing;
            rowH = Math.Max(rowH, s.Height);
            maxW = Math.Max(maxW, x - HorizontalSpacing);
        }
        return new Size(double.IsInfinity(available.Width) ? maxW : Math.Min(maxW, available.Width), y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > final.Width)
            {
                y += rowH + VerticalSpacing;
                x = 0; rowH = 0;
            }
            child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + HorizontalSpacing;
            rowH = Math.Max(rowH, s.Height);
        }
        return final;
    }
}
