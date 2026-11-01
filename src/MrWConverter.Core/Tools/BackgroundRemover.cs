using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MrWConverter.Core.Conversion;

namespace MrWConverter.Core.Tools;

/// <summary>
/// Removes an image's background with a local BEN2 model, so the image never leaves
/// the device. The result is always a PNG, because it needs transparency.
/// </summary>
public sealed class BackgroundRemover(string modelPath, ulong maxPixels = ImageConverter.DefaultMaxPixels) : IFileTool
{
    public const string ToolId = "removeBackground";

    public string Id => ToolId;

    public string OutputName(string inputStem) => $"{inputStem}-nobg.png";

    public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(inputPath, outputPath, sourceFormat, progress, cancellationToken), cancellationToken);

    private void Run(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var image = ImageFile.Read(inputPath, sourceFormat, maxPixels);

        // Nothing is reported until the model finishes, so the bar keeps moving instead of sitting still.
        var prediction = Predict(ModelInput(image), cancellationToken);
        progress?.Report(0.8);

        var mask = BackgroundMask.ToMask(prediction)
            ?? throw new InvalidDataException("The model found no subject in this image.");

        ApplyMask(image, mask, BackgroundMask.ModelSize);
        cancellationToken.ThrowIfCancellationRequested();

        image.Write(outputPath, MagickFormat.Png);
    }

    private static float[] ModelInput(MagickImage image)
    {
        using var small = image.Clone();
        small.Alpha(AlphaAction.Remove);
        small.FilterType = FilterType.Lanczos;
        small.Resize(new MagickGeometry(BackgroundMask.ModelSize, BackgroundMask.ModelSize) { IgnoreAspectRatio = true });

        using var pixels = small.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidDataException("The image has no readable pixels.");

        return BackgroundMask.ToModelInput(rgb);
    }

    private float[] Predict(float[] input, CancellationToken cancellationToken)
    {
        // The model peaks near 4 GB. Without a memory arena, it gives that back to Windows
        // when the session closes after each image instead of holding on to it.
        using var sessionOptions = new SessionOptions
        {
            EnableCpuMemArena = false,
            EnableMemoryPattern = false,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        using var session = new InferenceSession(modelPath, sessionOptions);
        using var options = new RunOptions();
        using var cancel = cancellationToken.Register(() => options.Terminate = true);

        var size = BackgroundMask.ModelSize;
        var tensor = new DenseTensor<float>(input, [1, 3, size, size]);
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor) };
        var outputs = new[] { session.OutputMetadata.Keys.First() };

        try
        {
            using var results = session.Run(inputs, outputs, options);
            return results[0].AsEnumerable<Float16>().Select(value => (float)BitConverter.UInt16BitsToHalf(value.value)).ToArray();
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>Scales a square mask up to the image and uses it as the image's transparency.</summary>
    internal static void ApplyMask(MagickImage image, byte[] mask, int maskSize)
    {
        var rgb = new byte[mask.Length * 3];
        for (var index = 0; index < mask.Length; index++)
        {
            rgb[index * 3] = rgb[index * 3 + 1] = rgb[index * 3 + 2] = mask[index];
        }

        using var scaled = new MagickImage();
        scaled.ReadPixels(rgb, new PixelReadSettings((uint)maskSize, (uint)maskSize, StorageType.Char, PixelMapping.RGB));
        scaled.FilterType = FilterType.Lanczos;
        scaled.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });

        image.Composite(scaled, CompositeOperator.CopyAlpha);
    }
}
