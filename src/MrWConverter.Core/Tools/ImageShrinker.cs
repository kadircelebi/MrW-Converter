using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

/// <summary>
/// Makes an image fit a size in the same format. Lowers the quality first where the format
/// has one, then the pixels. A file that already fits and needs no resizing is copied as it is.
/// </summary>
public sealed class ImageShrinker(ShrinkOptions options, string format, ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileTool
{
    // Below this, blocks show more than a smaller picture would lose.
    private const uint LowestQuality = 50;
    private const int QualitySearchSteps = 3;

    // ImageMagick reads a PNG's quality as digits: zlib level 9, adaptive filtering.
    private const uint PngStrongest = 95;

    private const int MaxRounds = 8;
    private const uint SmallestSide = 16;
    private const double LargestScaleStep = 0.9;

    // Size follows pixels only roughly, so each round aims a little lower than the sum says.
    private const double ScaleHeadroom = 0.97;

    public string Id => ShrinkOptions.ToolId;

    public string OutputName(string inputStem) => $"{inputStem}-small.{format}";

    public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(inputPath, outputPath, sourceFormat, cancellationToken), cancellationToken);

    private void Run(string inputPath, string outputPath, string sourceFormat, CancellationToken cancellationToken)
    {
        var sourceBytes = new FileInfo(inputPath).Length;
        var limit = options.LimitFor(sourceBytes);

        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);
        var longestSide = Math.Max(image.Width, image.Height);

        if (!options.Resizes(longestSide) && sourceBytes <= limit)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        ImageFile.RemoveMetadata(image);
        var scale = options.Resizes(longestSide) ? (double)options.MaxSide!.Value / longestSide : 1;

        for (var round = 0; round < MaxRounds && longestSide * scale >= SmallestSide; round++)
        {
            using var scaled = Scaled(image, scale);

            if (Fit(scaled, limit, cancellationToken, out var smallest) is { } result)
            {
                File.WriteAllBytes(outputPath, result);
                return;
            }

            scale *= Math.Min(LargestScaleStep, Math.Sqrt((double)limit / smallest) * ScaleHeadroom);
        }

        throw new CannotShrinkException($"{limit} bytes is too small for this image.");
    }

    private static IMagickImage<byte> Scaled(MagickImage image, double scale)
    {
        var copy = image.Clone();

        if (scale < 1)
        {
            copy.Resize(new MagickGeometry(
                (uint)Math.Max(1, Math.Round(image.Width * scale)),
                (uint)Math.Max(1, Math.Round(image.Height * scale)))
            {
                IgnoreAspectRatio = true,
            });
            copy.ResetPage();
        }

        return copy;
    }

    /// <summary>The best quality that fits at this size, or null with the smallest result it could make.</summary>
    private byte[]? Fit(IMagickImage<byte> image, long limit, CancellationToken cancellationToken, out long smallest)
    {
        var best = Encode(image, ImageFile.LossyQuality, cancellationToken);
        smallest = best.Length;

        if (best.Length <= limit)
        {
            return best;
        }

        if (format is not ("jpg" or "webp" or "avif"))
        {
            return null;
        }

        best = Encode(image, LowestQuality, cancellationToken);
        smallest = best.Length;

        if (best.Length > limit)
        {
            return null;
        }

        var (fits, tooBig) = (LowestQuality, ImageFile.LossyQuality);
        for (var step = 0; step < QualitySearchSteps; step++)
        {
            var quality = (fits + tooBig) / 2;
            var attempt = Encode(image, quality, cancellationToken);

            if (attempt.Length <= limit)
            {
                (best, fits) = (attempt, quality);
            }
            else
            {
                tooBig = quality;
            }
        }

        return best;
    }

    private byte[] Encode(IMagickImage<byte> image, uint quality, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        image.Quality = format == "png" ? PngStrongest : quality;

        if (format == "tiff")
        {
            image.Settings.Compression = CompressionMethod.LZW;
        }

        return image.ToByteArray(ImageFile.Formats[format]);
    }
}
