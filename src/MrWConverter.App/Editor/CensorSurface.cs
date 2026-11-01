using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>
/// Shows each censored region in the style it will have. Dragging on the image draws a new
/// box; dragging a box moves it, and the selected box's handles resize it.
/// </summary>
public sealed class CensorSurface : RegionSurface
{
    private const double MinimumDrawDistance = 4;
    private const double AddedBoxShare = 0.25;
    private const double AddedBoxOffsetShare = 0.04;

    private static readonly Pen BoxOutline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1));

    private readonly ImagePreview _preview;
    private readonly List<ImageRegion> _regions = [];
    private readonly Dictionary<CensorStyle, BitmapImage> _styleImages = [];
    private CensorStyle _style;
    private double _mosaicTilePercent = ImageCensor.DefaultMosaicTilePercent;
    private int _selected = -1;
    private DragMode _dragMode;
    private ImageRegion _dragStartRegion;
    private Point _dragStart;
    private RegionEdges _dragEdges;

    public CensorSurface(ImagePreview preview)
        : base(preview)
    {
        _preview = preview;
    }

    private enum DragMode
    {
        None,
        Pending,
        Drawing,
        Moving,
        Resizing,
    }

    public event Action? RegionsChanged;

    public IReadOnlyList<ImageRegion> Regions => _regions;

    public bool HasSelection => _selected >= 0;

    public CensorStyle ChosenStyle
    {
        get => _style;
        set
        {
            _style = value;
            InvalidateVisual();
        }
    }

    public double MosaicTilePercent
    {
        get => _mosaicTilePercent;
        set
        {
            _mosaicTilePercent = value;
            _styleImages.Remove(CensorStyle.Mosaic);
            InvalidateVisual();
        }
    }

    /// <summary>Adds a box without drawing one, so the tool also works from the keyboard.</summary>
    public void AddBox()
    {
        var size = (Width: Math.Max(1, (int)(ImageWidth * AddedBoxShare)), Height: Math.Max(1, (int)(ImageHeight * AddedBoxShare)));

        // Each new box starts a little further along, so it doesn't hide the last one.
        var offset = _regions.Count * AddedBoxOffsetShare;
        var box = new ImageRegion((ImageWidth - size.Width) / 2, (ImageHeight - size.Height) / 2, size.Width, size.Height)
            .Moved(ImageWidth * offset, ImageHeight * offset, ImageWidth, ImageHeight);

        _regions.Add(box);
        Select(_regions.Count - 1);
        Changed();
    }

    public void RemoveSelected()
    {
        if (HasSelection)
        {
            _regions.RemoveAt(_selected);
            _selected = _regions.Count - 1;
            Changed();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        DrawImage(context);

        var styled = StyleImage();
        foreach (var region in _regions)
        {
            var box = ToScreen(region);
            context.PushClip(new RectangleGeometry(box));
            context.DrawImage(styled, ImageRect);
            context.Pop();
            context.DrawRectangle(null, BoxOutline, box);
        }

        if (HasSelection)
        {
            var selected = ToScreen(_regions[_selected]);
            context.DrawRectangle(null, FrameOutline, selected);
            DrawHandles(context, selected);
            DrawFocusRing(context, selected);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var position = e.GetPosition(this);
        _dragStart = position;

        if (HasSelection && EdgesAt(position, ToScreen(_regions[_selected])) is var edges and not RegionEdges.None)
        {
            _dragMode = DragMode.Resizing;
            _dragEdges = edges;
            _dragStartRegion = _regions[_selected];
        }
        else if (BoxAt(position) is var box and >= 0)
        {
            Select(box);
            _dragMode = DragMode.Moving;
            _dragStartRegion = _regions[box];
        }
        else if (ImageRect.Contains(position))
        {
            // Becomes a box only once the pointer has moved, so a plain click just deselects.
            Select(-1);
            _dragMode = DragMode.Pending;
        }
        else
        {
            Select(-1);
            return;
        }

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);
        var dx = (position.X - _dragStart.X) / Scale;
        var dy = (position.Y - _dragStart.Y) / Scale;

        switch (_dragMode)
        {
            case DragMode.None:
                Cursor = CursorAt(position);
                break;

            case DragMode.Pending when (position - _dragStart).Length >= MinimumDrawDistance:
                _regions.Add(Drawn(position));
                _selected = _regions.Count - 1;
                _dragMode = DragMode.Drawing;
                Changed();
                break;

            case DragMode.Drawing:
                Update(Drawn(position));
                break;

            case DragMode.Moving:
                Update(_dragStartRegion.Moved(dx, dy, ImageWidth, ImageHeight));
                break;

            case DragMode.Resizing:
                Update(_dragStartRegion.Resized(_dragEdges, dx, dy, null, ImageWidth, ImageHeight));
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragMode = DragMode.None;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragMode = DragMode.None;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Delete:
                RemoveSelected();
                e.Handled = true;
                return;

            case Key.Space when _regions.Count > 0:
                Select((_selected + 1) % _regions.Count);
                e.Handled = true;
                return;
        }

        var step = KeyboardStep;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0d),
            Key.Right => (step, 0d),
            Key.Up => (0d, -step),
            Key.Down => (0d, step),
            _ => (0d, 0d),
        };

        if (HasSelection && (dx != 0 || dy != 0))
        {
            var region = _regions[_selected];
            Update(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? region.Resized(RegionEdges.Right | RegionEdges.Bottom, dx, dy, null, ImageWidth, ImageHeight)
                : region.Moved(dx, dy, ImageWidth, ImageHeight));
            e.Handled = true;
        }
    }

    /// <summary>The style across the whole image, made once and shown through each box while it moves.</summary>
    private BitmapImage StyleImage()
    {
        if (!_styleImages.TryGetValue(_style, out var image))
        {
            image = LoadBitmap(ImageCensor.RenderPreview(_preview, _style, _mosaicTilePercent));
            _styleImages[_style] = image;
        }

        return image;
    }

    private ImageRegion Drawn(Point position)
    {
        var start = ToImage(_dragStart);
        var end = ToImage(position);
        return ImageRegion.Between(start.X, start.Y, end.X, end.Y, ImageWidth, ImageHeight);
    }

    /// <summary>The topmost box at this point, or -1.</summary>
    private int BoxAt(Point position)
    {
        for (var index = _regions.Count - 1; index >= 0; index--)
        {
            if (ToScreen(_regions[index]).Contains(position))
            {
                return index;
            }
        }

        return -1;
    }

    private Cursor CursorAt(Point position)
    {
        if (HasSelection && ResizeCursor(EdgesAt(position, ToScreen(_regions[_selected]))) is { } resize)
        {
            return resize;
        }

        if (BoxAt(position) >= 0)
        {
            return Cursors.SizeAll;
        }

        return ImageRect.Contains(position) ? Cursors.Cross : Cursors.Arrow;
    }

    private void Select(int index)
    {
        _selected = index;
        Changed();
    }

    private void Update(ImageRegion region)
    {
        _regions[_selected] = region;
        Changed();
    }

    private void Changed()
    {
        InvalidateVisual();
        RegionsChanged?.Invoke();
    }
}
