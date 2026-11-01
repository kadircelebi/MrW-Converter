using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

/// <summary>A downscaled copy of an image for choosing regions on, and the size of the real one.</summary>
public sealed record ImagePreview(string Path, string Format, byte[] Png, int ImageWidth, int ImageHeight)
{
    private const uint Side = 1600;

    /// <summary>Reads the image the way the tools do, so a region lands on the same pixels.</summary>
    public static ImagePreview Read(string path, string format, ulong maxPixels = ImageConverter.DefaultMaxPixels)
    {
        using var image = ImageFile.Read(path, format, maxPixels);
        var (width, height) = ((int)image.Width, (int)image.Height);

        image.Strip();
        image.Thumbnail(new MagickGeometry(Side, Side) { Greater = true });

        using var output = new MemoryStream();
        image.Write(output, MagickFormat.Png);
        return new ImagePreview(path, format, output.ToArray(), width, height);
    }
}
