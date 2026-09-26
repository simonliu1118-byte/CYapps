using System.Windows;
using System.Windows.Media;

namespace CYEnvelope;

public sealed class VisualHost : FrameworkElement
{
    private DrawingVisual? _visual;
    protected override int VisualChildrenCount => _visual is null ? 0 : 1;
    protected override Visual GetVisualChild(int index) =>
        index == 0 && _visual is not null ? _visual : throw new ArgumentOutOfRangeException(nameof(index));

    public void Show(DrawingVisual visual, double width, double height)
    {
        if (_visual is not null) RemoveVisualChild(_visual);
        _visual = visual;
        AddVisualChild(visual);
        Width = width;
        Height = height;
        InvalidateMeasure();
        InvalidateVisual();
    }
}
