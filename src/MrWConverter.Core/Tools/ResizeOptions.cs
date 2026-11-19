namespace MrWConverter.Core.Tools;

public enum ResizeMode
{
    /// <summary>Both sides by the same share, for example 50 or 200 percent.</summary>
    Percent,

    /// <summary>The longest side becomes this many pixels, the other follows.</summary>
    LongestSide,

    /// <summary>The whole picture fits inside a box, so one side may come out shorter.</summary>
    Fit,

    /// <summary>The picture covers a box and the overflow is cut evenly from both edges.</summary>
    Fill,
}

/// <summary>The pixel size an image gets, before any edge is cut, and after.</summary>
public readonly record struct ResizePlan(int ScaledWidth, int ScaledHeight, int Width, int Height)
{
    public bool Crops => ScaledWidth != Width || ScaledHeight != Height;
}

/// <summary>What the user asked Resize for. Unlike Shrink, it may make a picture larger.</summary>
/// <param name="Value">The percent for <see cref="ResizeMode.Percent"/>, the side for <see cref="ResizeMode.LongestSide"/>.</param>
/// <param name="Width">The box width for <see cref="ResizeMode.Fit"/> and <see cref="ResizeMode.Fill"/>.</param>
/// <param name="Height">The box height for <see cref="ResizeMode.Fit"/> and <see cref="ResizeMode.Fill"/>.</param>
public sealed record ResizeOptions(ResizeMode Mode, double Value = 0, int Width = 0, int Height = 0)
{
    public const string ToolId = "resize";

    public const double SmallestPercent = 1;
    public const double LargestPercent = 1000;
    public const int LargestSide = 65_000;

    public static ResizeOptions ByPercent(double percent) => new(ResizeMode.Percent, percent);

    public static ResizeOptions ByLongestSide(int side) => new(ResizeMode.LongestSide, side);

    public static ResizeOptions ToBox(int width, int height, bool fill) => new(fill ? ResizeMode.Fill : ResizeMode.Fit, 0, width, height);

    public bool IsValid => Mode switch
    {
        ResizeMode.Percent => Value is >= SmallestPercent and <= LargestPercent,
        ResizeMode.LongestSide => Value is >= 1 and <= LargestSide,
        _ => Width is >= 1 and <= LargestSide && Height is >= 1 and <= LargestSide,
    };

    public ResizePlan Plan(int width, int height)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException($"{this} is not a size.");
        }

        var scale = Mode switch
        {
            ResizeMode.Percent => Value / 100,
            ResizeMode.LongestSide => Value / Math.Max(width, height),
            ResizeMode.Fit => Math.Min((double)Width / width, (double)Height / height),
            _ => Math.Max((double)Width / width, (double)Height / height),
        };

        var scaledWidth = Side(width * scale);
        var scaledHeight = Side(height * scale);

        // Rounding can leave a covering picture a pixel short of the box.
        if (Mode == ResizeMode.Fill)
        {
            (scaledWidth, scaledHeight) = (Math.Max(scaledWidth, Width), Math.Max(scaledHeight, Height));
            return new ResizePlan(scaledWidth, scaledHeight, Width, Height);
        }

        return new ResizePlan(scaledWidth, scaledHeight, scaledWidth, scaledHeight);
    }

    private static int Side(double pixels) => (int)Math.Max(1, Math.Round(pixels));
}

/// <summary>The result would be bigger than the format, or this app, can hold.</summary>
public sealed class ResizeTooLargeException(string message) : Exception(message);
