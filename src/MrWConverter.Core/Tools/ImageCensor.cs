using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

public enum CensorStyle
{
    /// <summary>Solid black. Nothing of the original remains.</summary>
    Black,

    /// <summary>Random grain. It is generated without looking at the image, so nothing of the original remains.</summary>
    Noise,

    /// <summary>Small averaged tiles. They still come from the original, so shapes stay and text can sometimes be recovered.</summary>
    Mosaic,
}

/// <summary>
/// Replaces the given regions, in a new file of the same format. The output is encoded
/// from the painted pixels, so nothing under the regions survives except what the style
/// itself keeps. There is no blur style, because blurred text is the easiest to recover.
/// </summary>
public sealed class ImageCensor(
    IReadOnlyList<ImageRegion> regions,
    string format,
    CensorStyle style = CensorStyle.Black,
    ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileTool
{
    public const string ToolId = "censor";

    public const double MinimumMosaicTilePercent = 0.5;
    public const double DefaultMosaicTilePercent = 1;
    public const double MaximumMosaicTilePercent = 5;

    private const uint MinimumGrain = 2;
    private const uint GrainsPerSide = 400;
    private const uint MinimumTile = 4;

    public string Id => ToolId;

    /// <summary>The side of a mosaic tile, as a percentage of the image's longest side.</summary>
    public double MosaicTilePercent { get; init; } = DefaultMosaicTilePercent;

    /// <summary>Tests set this to show that two different images get the very same grain.</summary>
    internal Random NoiseSource { get; init; } = Random.Shared;

    public string OutputName(string inputStem) => $"{inputStem}-censored.{format}";

    public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(inputPath, outputPath, sourceFormat), cancellationToken);

    /// <summary>The style across a whole preview, as a PNG the same size, for showing through each region.</summary>
    public static byte[] RenderPreview(ImagePreview preview, CensorStyle style, double mosaicTilePercent = DefaultMosaicTilePercent)
    {
        using var image = new MagickImage(preview.Png, MagickFormat.Png);

        // The grain and tiles are sized for the real image, so the preview has to shrink them with it.
        var shrink = (double)Math.Max(image.Width, image.Height) / Math.Max(preview.ImageWidth, preview.ImageHeight);
        var longestSide = (uint)Math.Max(preview.ImageWidth, preview.ImageHeight);

        using var overlay = style switch
        {
            CensorStyle.Mosaic => Mosaic(image, Shrunk(TileSize(longestSide, mosaicTilePercent), shrink)),
            CensorStyle.Noise => Grain(image.Width, image.Height, Shrunk(GrainSize(longestSide), shrink), Random.Shared),
            _ => new MagickImage(MagickColors.Black, image.Width, image.Height),
        };

        using var output = new MemoryStream();
        overlay.Write(output, MagickFormat.Png);
        return output.ToArray();
    }

    private void Run(string inputPath, string outputPath, string sourceFormat)
    {
        if (regions.Count == 0)
        {
            throw new InvalidDataException("There is nothing to censor.");
        }

        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);

        // Checked first, so a bad region can never produce a file with only some regions painted.
        if (!regions.All(region => region.FitsInside(image.Width, image.Height)))
        {
            throw new InvalidDataException("A censored region is outside the image.");
        }

        var longestSide = Math.Max(image.Width, image.Height);
        using var mosaic = style == CensorStyle.Mosaic ? Mosaic(image, TileSize(longestSide, MosaicTilePercent)) : null;

        foreach (var region in regions)
        {
            var (width, height) = ((uint)region.Width, (uint)region.Height);
            using var box = style switch
            {
                CensorStyle.Mosaic => mosaic!.CloneArea(region.X, region.Y, width, height),
                CensorStyle.Noise => Grain(width, height, GrainSize(longestSide), NoiseSource),
                _ => new MagickImage(MagickColors.Black, width, height),
            };

            // Copy replaces the pixels outright, so a transparent area comes out opaque too.
            image.Composite(box, region.X, region.Y, CompositeOperator.Copy);
        }

        ImageFile.WriteWithoutMetadata(image, outputPath, format);
    }

    // One image pixel of grain would average out to flat gray on any large photo, so it scales with the image.
    private static uint GrainSize(uint longestSide) => Math.Max(MinimumGrain, longestSide / GrainsPerSide);

    private static uint TileSize(uint longestSide, double percent) => Math.Max(MinimumTile, (uint)(longestSide * percent / 100));

    private static uint Shrunk(uint size, double shrink) => Math.Max(1, (uint)Math.Round(size * shrink));

    private static IMagickImage<byte> Grain(uint width, uint height, uint grain, Random random)
    {
        var (columns, rows) = (Math.Max(1, width / grain + 1), Math.Max(1, height / grain + 1));
        var rgb = new byte[columns * rows * 3];

        for (var index = 0; index < rgb.Length; index += 3)
        {
            rgb[index] = rgb[index + 1] = rgb[index + 2] = (byte)random.Next(256);
        }

        var noise = new MagickImage();
        noise.ReadPixels(rgb, new PixelReadSettings(columns, rows, StorageType.Char, PixelMapping.RGB));
        noise.Sample(columns * grain, rows * grain);
        noise.Crop(new MagickGeometry(0, 0, width, height));
        noise.ResetPage();
        return noise;
    }

    private static IMagickImage<byte> Mosaic(MagickImage image, uint tile)
    {
        var (width, height) = (image.Width, image.Height);
        var tiles = image.Clone();

        tiles.BackgroundColor = MagickColors.Black;
        tiles.Alpha(AlphaAction.Remove);
        tiles.Scale(new MagickGeometry(Math.Max(1, width / tile), Math.Max(1, height / tile)) { IgnoreAspectRatio = true });
        tiles.Sample(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        return tiles;
    }
}
