using System.Globalization;
using ImageMagick;
using ImageMagick.Drawing;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Tools;

namespace MrWConverter.Core.Tests;

public class BackgroundRemoverTests
{
    [Fact]
    public void ToModelInput_ScalesToOneAndSeparatesChannels()
    {
        byte[] rgb = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];

        var input = BackgroundMask.ToModelInput(rgb, size: 2);

        Assert.Equal(12, input.Length);
        Assert.Equal(1f, input[0], 4); // red plane, first pixel
        Assert.Equal(0f, input[1], 4); // red plane, second pixel
        Assert.Equal(1f, input[5], 4); // green plane, second pixel
        Assert.Equal(1f, input[10], 4); // blue plane, third pixel
    }

    [Fact]
    public void ToModelInput_DoesNotStretchADarkImage()
    {
        byte[] rgb = [51, 0, 0];

        var input = BackgroundMask.ToModelInput(rgb, size: 1);

        Assert.Equal(0.2f, input[0], 4);
    }

    [Fact]
    public void ToModelInput_ForABlackImage_DoesNotDivideByZero()
    {
        var input = BackgroundMask.ToModelInput(new byte[3], size: 1);

        Assert.All(input, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void ToModelInput_RejectsTheWrongLength()
    {
        Assert.Throws<ArgumentException>(() => BackgroundMask.ToModelInput(new byte[5], size: 1));
    }

    [Fact]
    public void ToMask_StretchesToTheFullRange()
    {
        var mask = BackgroundMask.ToMask([0.2f, 0.4f, 0.6f])!;

        Assert.Equal(0, mask[0]);
        Assert.InRange(mask[1], 127, 128);
        Assert.Equal(255, mask[2]);
    }

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(float.NaN, 0.5f)]
    public void ToMask_ForAFlatOrInvalidResult_IsNull(float first, float second)
    {
        Assert.Null(BackgroundMask.ToMask([first, second]));
    }

    [Fact]
    public void ToMask_WithNoValues_IsNull()
    {
        Assert.Null(BackgroundMask.ToMask([]));
    }

    [Fact]
    public void ApplyMask_UsesTheMaskAsTransparency()
    {
        using var image = new MagickImage(MagickColors.Red, 40, 40);
        byte[] mask = [255, 0, 0, 255];

        BackgroundRemover.ApplyMask(image, mask, maskSize: 2);

        using var pixels = image.GetPixels();
        Assert.True(image.HasAlpha);
        Assert.True(pixels.GetPixel(2, 2).ToColor()!.A > 200, "The kept corner should be opaque.");
        Assert.True(pixels.GetPixel(37, 2).ToColor()!.A < 55, "The removed corner should be transparent.");
        Assert.Equal(MagickColors.Red.R, pixels.GetPixel(2, 2).ToColor()!.R);
    }

    [Fact]
    public async Task Runner_PublishesTheToolsResultAndKeepsTheOriginal()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("a.png"), "original");
        var runner = new ConversionRunner(FormatCatalog.Default, [], [new FakeTool()]);

        var outputs = await runner.RunToolAsync([folder.File("a.png")], "fake", folder.Path);

        Assert.Equal([folder.File("a-fake.png")], outputs);
        Assert.Equal("original", File.ReadAllText(folder.File("a.png")));
    }

    [Fact]
    public async Task Runner_WithAnUnknownTool_Fails()
    {
        using var folder = new TemporaryFolder();
        var runner = new ConversionRunner(FormatCatalog.Default, []);

        await Assert.ThrowsAsync<NotSupportedException>(() => runner.RunToolAsync([folder.File("a.png")], "nothing", folder.Path));
    }

    [ModelFact]
    public async Task RealModel_KeepsTheSubjectAndRemovesTheBackground()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.jpg");
        var output = folder.File("output.png");
        WriteSubjectOnPlainBackground(input);

        await new BackgroundRemover(ModelFile.Path).RunAsync(input, output, "jpg", null, CancellationToken.None);

        using var result = new MagickImage(output);
        using var pixels = result.GetPixels();
        Assert.Equal(MagickFormat.Png, result.Format);
        Assert.Equal(640u, result.Width);
        Assert.Equal(480u, result.Height);
        Assert.True(pixels.GetPixel(320, 240).ToColor()!.A > 200, "The middle of the subject should stay opaque.");
        Assert.True(pixels.GetPixel(10, 10).ToColor()!.A < 55, "The background corner should turn transparent.");
    }

    [ModelFact]
    public async Task RealModel_WorksUnderTurkishCultureWithSpecialFileNames()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            using var folder = new TemporaryFolder();
            var input = folder.File("şğüİı [1] %d.jpg");
            var output = folder.File("output.png");
            WriteSubjectOnPlainBackground(input);

            await new BackgroundRemover(ModelFile.Path).RunAsync(input, output, "jpg", null, CancellationToken.None);

            Assert.True(new FileInfo(output).Length > 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [ModelFact]
    public async Task RealModel_WhenAlreadyCancelled_StopsWithoutOutput()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.jpg");
        var output = folder.File("output.png");
        WriteSubjectOnPlainBackground(input);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BackgroundRemover(ModelFile.Path).RunAsync(input, output, "jpg", null, new CancellationToken(canceled: true)));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task MissingModel_Fails()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.jpg");
        WriteSubjectOnPlainBackground(input);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new BackgroundRemover(folder.File("no-model.onnx")).RunAsync(input, folder.File("output.png"), "jpg", null, CancellationToken.None));
    }

    [Fact]
    public async Task TooLargeImage_IsRefusedBeforeTheModelRuns()
    {
        using var folder = new TemporaryFolder();
        var input = folder.File("input.jpg");
        WriteSubjectOnPlainBackground(input);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BackgroundRemover(folder.File("no-model.onnx"), maxPixels: 100).RunAsync(input, folder.File("output.png"), "jpg", null, CancellationToken.None));
    }

    private static void WriteSubjectOnPlainBackground(string path)
    {
        using var image = new MagickImage(new MagickColor("#E8E8E8"), 640, 480);
        new Drawables()
            .FillColor(new MagickColor("#C0392B"))
            .Circle(320, 240, 320, 90)
            .Draw(image);

        using var stream = File.Create(path);
        image.Write(stream, MagickFormat.Jpeg);
    }

    private sealed class FakeTool : IFileTool
    {
        public string Id => "fake";

        public string OutputName(string inputStem) => $"{inputStem}-fake.png";

        public Task RunAsync(string inputPath, string outputPath, string sourceFormat, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            File.WriteAllText(outputPath, "done");
            return Task.CompletedTask;
        }
    }
}

internal static class ModelFile
{
    public static string Path { get; } = Find();

    public static bool IsSetUp => File.Exists(Path);

    private static string Find()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(folder.FullName, "MrWConverter.slnx")))
            {
                return System.IO.Path.Combine(folder.FullName, "third_party", "models", "ben2-base.onnx");
            }
        }

        throw new DirectoryNotFoundException("Could not find the solution folder.");
    }
}

/// <summary>The model isn't in the repository; a fresh checkout shows these tests as skipped, not passed.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (!ModelFile.IsSetUp)
        {
            Skip = @"The model is not set up. Run scripts\setup-models.ps1.";
        }
    }
}
