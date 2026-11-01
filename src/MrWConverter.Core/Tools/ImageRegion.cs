namespace MrWConverter.Core.Tools;

/// <summary>Which sides of a region a drag moves.</summary>
[Flags]
public enum RegionEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>
/// A rectangle in image pixels, and the rules for changing it. Each change is worked out
/// from where the drag began, so the region can't drift when the pointer leaves the image.
/// </summary>
public readonly record struct ImageRegion(int X, int Y, int Width, int Height)
{
    private const int MinimumSize = 16;

    public static ImageRegion Whole(int imageWidth, int imageHeight) => new(0, 0, imageWidth, imageHeight);

    public bool FitsInside(uint imageWidth, uint imageHeight) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= imageWidth && Y + Height <= imageHeight;

    public ImageRegion Moved(double dx, double dy, int imageWidth, int imageHeight) => this with
    {
        X = (int)Math.Round(Math.Clamp(X + dx, 0, imageWidth - Width)),
        Y = (int)Math.Round(Math.Clamp(Y + dy, 0, imageHeight - Height)),
    };

    /// <summary>Moves the given edges, keeping width divided by height at <paramref name="ratio"/> if one is set.</summary>
    public ImageRegion Resized(RegionEdges edges, double dx, double dy, double? ratio, int imageWidth, int imageHeight)
    {
        var minimumWidth = Math.Min(MinimumSize, imageWidth);
        var minimumHeight = Math.Min(MinimumSize, imageHeight);

        double left = X, top = Y, right = X + Width, bottom = Y + Height;

        if (edges.HasFlag(RegionEdges.Left))
        {
            left = Math.Clamp(left + dx, 0, right - minimumWidth);
        }
        else if (edges.HasFlag(RegionEdges.Right))
        {
            right = Math.Clamp(right + dx, left + minimumWidth, imageWidth);
        }

        if (edges.HasFlag(RegionEdges.Top))
        {
            top = Math.Clamp(top + dy, 0, bottom - minimumHeight);
        }
        else if (edges.HasFlag(RegionEdges.Bottom))
        {
            bottom = Math.Clamp(bottom + dy, top + minimumHeight, imageHeight);
        }

        return ratio is { } wanted
            ? KeepRatio(left, top, right, bottom, edges, wanted, imageWidth, imageHeight)
            : FromEdges(left, top, right, bottom);
    }

    /// <summary>The largest centered region of this shape that fits the image.</summary>
    public static ImageRegion Fitted(double ratio, int imageWidth, int imageHeight)
    {
        var width = Math.Min(imageWidth, imageHeight * ratio);
        var height = width / ratio;
        var left = (imageWidth - width) / 2;
        var top = (imageHeight - height) / 2;
        return FromEdges(left, top, left + width, top + height);
    }

    /// <summary>The region between two points in any order, cut off at the image's edges.</summary>
    public static ImageRegion Between(double x1, double y1, double x2, double y2, int imageWidth, int imageHeight) => FromEdges(
        Math.Clamp(Math.Min(x1, x2), 0, imageWidth),
        Math.Clamp(Math.Min(y1, y2), 0, imageHeight),
        Math.Clamp(Math.Max(x1, x2), 0, imageWidth),
        Math.Clamp(Math.Max(y1, y2), 0, imageHeight));

    private static ImageRegion KeepRatio(double left, double top, double right, double bottom, RegionEdges edges, double ratio, int imageWidth, int imageHeight)
    {
        var horizontal = edges & (RegionEdges.Left | RegionEdges.Right);
        var vertical = edges & (RegionEdges.Top | RegionEdges.Bottom);

        var width = right - left;
        var height = bottom - top;

        // A side handle sets the size along its own axis; a corner follows whichever axis grew more.
        var wantedWidth = (horizontal, vertical) switch
        {
            (RegionEdges.None, _) => height * ratio,
            (_, RegionEdges.None) => width,
            _ => Math.Max(width, height * ratio),
        };

        // The opposite edge stays still. An axis no edge moves on stays centered instead.
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var roomX = horizontal switch
        {
            RegionEdges.Left => right,
            RegionEdges.Right => imageWidth - left,
            _ => 2 * Math.Min(centerX, imageWidth - centerX),
        };
        var roomY = vertical switch
        {
            RegionEdges.Top => bottom,
            RegionEdges.Bottom => imageHeight - top,
            _ => 2 * Math.Min(centerY, imageHeight - centerY),
        };

        var smallest = Math.Min(MinimumSize, Math.Min(imageWidth, imageHeight * ratio));
        width = Math.Min(Math.Max(wantedWidth, smallest), Math.Min(roomX, roomY * ratio));
        height = width / ratio;

        var newLeft = horizontal switch
        {
            RegionEdges.Left => right - width,
            RegionEdges.Right => left,
            _ => centerX - width / 2,
        };
        var newTop = vertical switch
        {
            RegionEdges.Top => bottom - height,
            RegionEdges.Bottom => top,
            _ => centerY - height / 2,
        };

        return FromEdges(newLeft, newTop, newLeft + width, newTop + height);
    }

    private static ImageRegion FromEdges(double left, double top, double right, double bottom)
    {
        var x = (int)Math.Round(left);
        var y = (int)Math.Round(top);
        return new ImageRegion(x, y, Math.Max(1, (int)Math.Round(right) - x), Math.Max(1, (int)Math.Round(bottom) - y));
    }
}
