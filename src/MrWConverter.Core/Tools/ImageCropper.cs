using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

/// <summary>Cuts one region out of an image into a new file of the same format.</summary>
public sealed class ImageCropper(ImageRegion region, string format, ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileTool
{
    public const string ToolId = "crop";

    public string Id => ToolId;

    public string OutputName(string inputStem) => $"{inputStem}-crop.{format}";

    public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(inputPath, outputPath, sourceFormat), cancellationToken);

    private void Run(string inputPath, string outputPath, string sourceFormat)
    {
        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);

        if (!region.FitsInside(image.Width, image.Height))
        {
            throw new InvalidDataException("The crop region is outside the image.");
        }

        image.Crop(new MagickGeometry(region.X, region.Y, (uint)region.Width, (uint)region.Height));
        image.ResetPage();

        ImageFile.WriteWithoutMetadata(image, outputPath, format);
    }
}
