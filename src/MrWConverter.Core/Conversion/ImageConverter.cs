using ImageMagick;

namespace MrWConverter.Core.Conversion;

public sealed class ImageConverter(ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileConverter
{
    public const ulong DefaultMaxPixels = 250_000_000;

    public bool CanConvert(string sourceFormat, string targetFormat) =>
        ImageFile.Formats.ContainsKey(sourceFormat) && ImageFile.Formats.ContainsKey(targetFormat);

    public Task ConvertAsync(string inputPath, string outputPath, string sourceFormat, string targetFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Convert(inputPath, outputPath, sourceFormat, ImageFile.Formats[targetFormat]), cancellationToken);

    private void Convert(string inputPath, string outputPath, string sourceFormat, MagickFormat target)
    {
        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);

        if (target == MagickFormat.Jpeg)
        {
            // JPEG has no transparency; without this, transparent areas turn black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }

        image.Quality = ImageFile.LossyQuality;
        image.Write(outputPath, target);
    }
}
