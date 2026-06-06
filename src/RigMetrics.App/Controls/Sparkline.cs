using System.Windows;
using System.Windows.Media;

namespace RigMetrics.App.Controls;

/// <summary>
/// A minimal line graph that plots a series of values across its width.
/// Drawn directly with a <see cref="DrawingContext"/> so the app needs no
/// external charting library.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private double[] _values = Array.Empty<double>();

    public Brush Stroke { get; set; } = Brushes.LimeGreen;
    public double StrokeThickness { get; set; } = 1.5;
    public Brush? Fill { get; set; }

    /// <summary>Force the vertical axis range. When null, the range auto-scales to the data.</summary>
    public double? FixedMin { get; set; }
    public double? FixedMax { get; set; }

    public void SetValues(double[] values)
    {
        _values = values ?? Array.Empty<double>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1 || _values.Length < 2)
            return;

        double min = FixedMin ?? Min(_values);
        double max = FixedMax ?? Max(_values);
        if (max - min < 1e-6) { max = min + 1; min -= 1; }

        double range = max - min;
        double dx = w / (_values.Length - 1);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            Point first = MapPoint(0, _values[0], dx, h, min, range);
            ctx.BeginFigure(first, isFilled: Fill is not null, isClosed: false);

            var points = new List<Point>(_values.Length - 1);
            for (int i = 1; i < _values.Length; i++)
                points.Add(MapPoint(i, _values[i], dx, h, min, range));
            ctx.PolyLineTo(points, isStroked: true, isSmoothJoin: false);

            if (Fill is not null)
            {
                // Close the area down to the baseline for a filled look.
                ctx.LineTo(new Point(w, h), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(0, h), isStroked: false, isSmoothJoin: false);
            }
        }
        geometry.Freeze();

        if (Fill is not null)
            dc.DrawGeometry(Fill, null, geometry);

        dc.DrawGeometry(null, new Pen(Stroke, StrokeThickness), geometry);
    }

    private static Point MapPoint(int index, double value, double dx, double h, double min, double range)
    {
        double x = index * dx;
        double norm = (value - min) / range;       // 0..1
        double y = h - (norm * h);                  // invert: high value -> top
        return new Point(x, y);
    }

    private static double Min(double[] v)
    {
        double m = double.PositiveInfinity;
        foreach (var x in v) if (x < m) m = x;
        return m;
    }

    private static double Max(double[] v)
    {
        double m = double.NegativeInfinity;
        foreach (var x in v) if (x > m) m = x;
        return m;
    }
}
