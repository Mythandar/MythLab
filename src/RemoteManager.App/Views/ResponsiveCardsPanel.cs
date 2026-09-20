using System.Windows;
using System.Windows.Controls;

namespace RemoteManager.App.Views;

/// <summary>Equal-width cards in as many columns as the viewport can comfortably fit.</summary>
public sealed class ResponsiveCardsPanel : Panel
{
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth), typeof(double), typeof(ResponsiveCardsPanel),
        new FrameworkPropertyMetadata(340d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double width && double.IsFinite(width) && width > 0);

    public double MinimumColumnWidth
    {
        get => (double)GetValue(MinimumColumnWidthProperty);
        set => SetValue(MinimumColumnWidthProperty, value);
    }

    private int columns = 1;
    private double rowHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinimumColumnWidth;
        columns = Math.Min(InternalChildren.Count, Math.Max(1, (int)(width / MinimumColumnWidth)));
        var columnWidth = width / columns;
        rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        var rows = (InternalChildren.Count + columns - 1) / columns;
        return new Size(width, rows * rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width / columns;
        for (var i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(i % columns * width, i / columns * rowHeight, width, rowHeight));
        return finalSize;
    }
}
