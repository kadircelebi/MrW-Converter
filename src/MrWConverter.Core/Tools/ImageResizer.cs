using ImageMagick;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

/// <summary>
/// Gives an image a new pixel size in the same format. Making it larger adds no detail: the
/// picture gets bigger and softer. A file that already has the asked size is copied as it is.
/// </summary>
public sealed class ImageResizer(ResizeOptions options, string format, ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileTool
{
    // What each encoder can write, as far as the libraries in the app go.
    private static readonly IReadOnlyDictionary<string, int> LargestSides = new Dictionary<string, int>
    {
        ["jpg"] = 65_500,
        ["webp"] = 16_383,
        ["avif"] = 65_536,
    };

    public string Id => ResizeOptions.ToolId;

    public string OutputName(string inputStem) => $"{inputStem}-resized.{format}";

    public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(inputPath, outputPath, sourceFormat), cancellationToken);

    private void Run(string inputPath, string outputPath, string sourceFormat)
    {
        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);
        var plan = options.Plan((int)image.Width, (int)image.Height);

        if (plan.ScaledWidth == image.Width && plan.ScaledHeight == image.Height && !plan.Crops)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        CheckFits(plan);

        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry((uint)plan.ScaledWidth, (uint)plan.ScaledHeight) { IgnoreAspectRatio = true });

        if (plan.Crops)
        {
            image.Crop(new MagickGeometry(
                (plan.ScaledWidth - plan.Width) / 2,
                (plan.ScaledHeight - plan.Height) / 2,
                (uint)plan.Width,
                (uint)plan.Height));
        }

        image.ResetPage();
        ImageFile.WriteWithoutMetadata(image, outputPath, format);
    }

    /// <summary>The size the picture shows at, which for a turned photo is not the stored one.</summary>
    public static (int Width, int Height) ReadSize(string path, string format)
    {
        using var input = File.OpenRead(path);
        var info = new MagickImageInfo(input, new MagickReadSettings { Format = ImageFile.Formats[format], FrameCount = 1 });
        var turned = info.Orientation is Orientation.LeftTop or Orientation.RightTop or Orientation.RightBottom or Orientation.LeftBottom;
        return turned ? ((int)info.Height, (int)info.Width) : ((int)info.Width, (int)info.Height);
    }

    private void CheckFits(ResizePlan plan)
    {
        if ((ulong)plan.ScaledWidth * (ulong)plan.ScaledHeight > maxPixels)
        {
            throw new ResizeTooLargeException($"{plan.ScaledWidth} x {plan.ScaledHeight} is more than {maxPixels} pixels.");
        }

        if (LargestSides.TryGetValue(format, out var side) && Math.Max(plan.Width, plan.Height) > side)
        {
            throw new ResizeTooLargeException($"{format} images can be at most {side} pixels on a side.");
        }
    }
}
