using ImageMagick;

namespace MrWConverter.Core.Conversion;

/// <summary>Opens an image the same safe way for every tool that reads one.</summary>
internal static class ImageFile
{
    public const uint LossyQuality = 90;

    public static readonly IReadOnlyDictionary<string, MagickFormat> Formats = new Dictionary<string, MagickFormat>
    {
        ["jpg"] = MagickFormat.Jpeg,
        ["png"] = MagickFormat.Png,
        ["webp"] = MagickFormat.WebP,
        ["tiff"] = MagickFormat.Tiff,
        ["bmp"] = MagickFormat.Bmp,
        ["avif"] = MagickFormat.Avif,
    };

    public static MagickImage Read(string path, string format, ulong maxPixels)
    {
        // Streams keep names like "photo[0].png" from being read as Magick syntax, and
        // the pinned format stops a renamed file from reaching any other Magick decoder.
        using var input = File.OpenRead(path);
        var settings = new MagickReadSettings { Format = Formats[format], FrameCount = 1 };

        var info = new MagickImageInfo(input, settings);
        if ((ulong)info.Width * info.Height > maxPixels)
        {
            throw new InvalidDataException($"The image is larger than {maxPixels} pixels.");
        }

        input.Position = 0;
        var image = new MagickImage(input, settings);
        ApplyOrientation(image);
        return image;
    }

    /// <summary>
    /// Writes an edited image without its EXIF, XMP, comments or timestamps. Metadata can carry
    /// a thumbnail of the whole picture, which would show what the user just cut away or covered.
    /// Only the color profile stays, because the image needs it.
    /// </summary>
    public static void WriteWithoutMetadata(MagickImage image, string path, string format)
    {
        RemoveMetadata(image);
        image.Quality = LossyQuality;
        image.Write(path, Formats[format]);
    }

    public static void RemoveMetadata(MagickImage image)
    {
        var color = image.GetColorProfile();
        image.Strip();

        if (color is not null)
        {
            image.SetProfile(color);
        }

        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
    }

    /// <summary>
    /// Bakes the orientation into the pixels and drops the EXIF tag. A rotating tag
    /// left behind makes some viewers turn the image a second time.
    /// </summary>
    private static void ApplyOrientation(MagickImage image)
    {
        image.AutoOrient();

        if (image.GetExifProfile() is { } exif)
        {
            exif.RemoveValue(ExifTag.Orientation);
            image.SetProfile(exif);
        }
    }
}
