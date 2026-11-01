using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Shows an image scaled to fit, and what every region editor needs on top of it: coordinates, handles and cursors.</summary>
public abstract class RegionSurface : FrameworkElement
{
    private const double HandleReach = 12;
    private const double HandleSize = 9;
    private const double KeyboardStepShare = 0.01;

    private static readonly Pen HandleOutline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1));

    protected static readonly Pen FrameOutline = Frozen(new Pen(Brushes.White, 1.5));

    private readonly BitmapImage _bitmap;

    protected RegionSurface(ImagePreview preview)
    {
        _bitmap = LoadBitmap(preview.Png);

        ImageWidth = preview.ImageWidth;
        ImageHeight = preview.ImageHeight;

        Focusable = true;
        FocusVisualStyle = null;
    }

    protected int ImageWidth { get; }

    protected int ImageHeight { get; }

    protected double Scale => Math.Min(ActualWidth / ImageWidth, ActualHeight / ImageHeight);

    /// <summary>How far one arrow key press changes a region, in image pixels.</summary>
    protected double KeyboardStep => Math.Max(1, Math.Max(ImageWidth, ImageHeight) * KeyboardStepShare);

    protected Rect ImageRect
    {
        get
        {
            var (width, height) = (ImageWidth * Scale, ImageHeight * Scale);
            return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        }
    }

    protected Rect ToScreen(ImageRegion region)
    {
        var image = ImageRect;
        return new Rect(image.X + region.X * Scale, image.Y + region.Y * Scale, region.Width * Scale, region.Height * Scale);
    }

    protected Point ToImage(Point position)
    {
        var image = ImageRect;
        return new Point((position.X - image.X) / Scale, (position.Y - image.Y) / Scale);
    }

    /// <summary>Draws the image over a surface that catches the pointer everywhere, even outside the image.</summary>
    protected void DrawImage(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        context.DrawImage(_bitmap, ImageRect);
    }

    protected static void DrawHandles(DrawingContext context, Rect region)
    {
        double[] xs = [region.Left, region.Left + region.Width / 2, region.Right];
        double[] ys = [region.Top, region.Top + region.Height / 2, region.Bottom];
        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                if (x != xs[1] || y != ys[1])
                {
                    context.DrawRectangle(Brushes.White, HandleOutline, new Rect(x - HandleSize / 2, y - HandleSize / 2, HandleSize, HandleSize));
                }
            }
        }
    }

    /// <summary>Marks a region as holding the keyboard focus, which the white frame alone doesn't show.</summary>
    protected void DrawFocusRing(DrawingContext context, Rect region)
    {
        if (IsKeyboardFocused)
        {
            context.DrawRectangle(null, FrameOutline, new Rect(region.X + 3, region.Y + 3, Math.Max(0, region.Width - 6), Math.Max(0, region.Height - 6)));
        }
    }

    protected static RegionEdges EdgesAt(Point position, Rect region)
    {
        var inReach = new Rect(region.X - HandleReach, region.Y - HandleReach, region.Width + HandleReach * 2, region.Height + HandleReach * 2);
        if (!inReach.Contains(position))
        {
            return RegionEdges.None;
        }

        var edges = RegionEdges.None;
        if (Math.Abs(position.X - region.Left) <= HandleReach)
        {
            edges |= RegionEdges.Left;
        }
        else if (Math.Abs(position.X - region.Right) <= HandleReach)
        {
            edges |= RegionEdges.Right;
        }

        if (Math.Abs(position.Y - region.Top) <= HandleReach)
        {
            edges |= RegionEdges.Top;
        }
        else if (Math.Abs(position.Y - region.Bottom) <= HandleReach)
        {
            edges |= RegionEdges.Bottom;
        }

        return edges;
    }

    protected static Cursor? ResizeCursor(RegionEdges edges) => edges switch
    {
        RegionEdges.Left or RegionEdges.Right => Cursors.SizeWE,
        RegionEdges.Top or RegionEdges.Bottom => Cursors.SizeNS,
        RegionEdges.Left | RegionEdges.Top or RegionEdges.Right | RegionEdges.Bottom => Cursors.SizeNWSE,
        RegionEdges.Right | RegionEdges.Top or RegionEdges.Left | RegionEdges.Bottom => Cursors.SizeNESW,
        _ => null,
    };

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected static BitmapImage LoadBitmap(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    protected static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
