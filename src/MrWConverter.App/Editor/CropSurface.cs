using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Draws the crop region over the image, and turns pointer and keyboard input into region changes.</summary>
public sealed class CropSurface : RegionSurface
{
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
    private static readonly Pen Guide = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1));

    private ImageRegion _region;
    private ImageRegion _dragStartRegion;
    private Point _dragStart;
    private RegionEdges _dragEdges;
    private bool _dragging;

    public CropSurface(ImagePreview preview)
        : base(preview)
    {
        _region = ImageRegion.Whole(ImageWidth, ImageHeight);
    }

    public event Action? RegionChanged;

    public ImageRegion Region
    {
        get => _region;
        private set
        {
            if (_region != value)
            {
                _region = value;
                InvalidateVisual();
                RegionChanged?.Invoke();
            }
        }
    }

    /// <summary>Width divided by height, or null for any shape.</summary>
    public double? Ratio { get; private set; }

    public bool IsWholeImage => _region == ImageRegion.Whole(ImageWidth, ImageHeight);

    public bool IsPortrait => ImageHeight > ImageWidth;

    public void UseRatio(double? ratio)
    {
        Ratio = ratio;
        Region = ratio is { } wanted ? ImageRegion.Fitted(wanted, ImageWidth, ImageHeight) : ImageRegion.Whole(ImageWidth, ImageHeight);
    }

    protected override void OnRender(DrawingContext context)
    {
        DrawImage(context);

        var region = ToScreen(_region);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(ImageRect), new RectangleGeometry(region));
        context.DrawGeometry(Dim, null, outside);

        for (var third = 1; third < 3; third++)
        {
            var x = region.X + region.Width * third / 3;
            var y = region.Y + region.Height * third / 3;
            context.DrawLine(Guide, new Point(x, region.Top), new Point(x, region.Bottom));
            context.DrawLine(Guide, new Point(region.Left, y), new Point(region.Right, y));
        }

        context.DrawRectangle(null, FrameOutline, region);
        DrawHandles(context, region);
        DrawFocusRing(context, region);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var position = e.GetPosition(this);
        var region = ToScreen(_region);
        var edges = EdgesAt(position, region);
        if (edges != RegionEdges.None || region.Contains(position))
        {
            _dragEdges = edges;
            _dragStart = position;
            _dragStartRegion = _region;
            _dragging = CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);
        if (!_dragging)
        {
            var region = ToScreen(_region);
            Cursor = ResizeCursor(EdgesAt(position, region)) ?? (region.Contains(position) ? Cursors.SizeAll : Cursors.Arrow);
            return;
        }

        var dx = (position.X - _dragStart.X) / Scale;
        var dy = (position.Y - _dragStart.Y) / Scale;
        Region = _dragEdges == RegionEdges.None
            ? _dragStartRegion.Moved(dx, dy, ImageWidth, ImageHeight)
            : _dragStartRegion.Resized(_dragEdges, dx, dy, Ratio, ImageWidth, ImageHeight);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var step = KeyboardStep;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0d),
            Key.Right => (step, 0d),
            Key.Up => (0d, -step),
            Key.Down => (0d, step),
            _ => (0d, 0d),
        };

        if (dx == 0 && dy == 0)
        {
            return;
        }

        Region = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? _region.Resized(RegionEdges.Right | RegionEdges.Bottom, dx, dy, Ratio, ImageWidth, ImageHeight)
            : _region.Moved(dx, dy, ImageWidth, ImageHeight);
        e.Handled = true;
    }
}
