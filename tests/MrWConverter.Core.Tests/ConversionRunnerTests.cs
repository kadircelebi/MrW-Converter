using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;

namespace MrWConverter.Core.Tests;

public class ConversionRunnerTests
{
    private static ConversionRunner Runner(params IFileConverter[] converters) => new(FormatCatalog.Default, converters);

    [Fact]
    public async Task Run_PublishesEveryOutputAndKeepsOriginals()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("a.png"), "a");
        File.WriteAllText(folder.File("b.png"), "b");
        var progress = new List<double>();

        var outputs = await Runner(new FakeConverter()).RunAsync(
            [folder.File("a.png"), folder.File("b.png")], "jpg", folder.Path, new ImmediateProgress(progress.Add));

        Assert.Equal([folder.File("a.jpg"), folder.File("b.jpg")], outputs);
        Assert.Equal(["a.jpg", "a.png", "b.jpg", "b.png"], folder.Entries());
        Assert.Equal([0.25, 0.5, 0.75, 1.0], progress);
    }

    [Fact]
    public async Task Run_WhenOneFileFails_PublishesNothingAndNamesTheFile()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("good.png"), "a");
        File.WriteAllText(folder.File("bad.png"), "b");

        var error = await Assert.ThrowsAsync<ConversionException>(() =>
            Runner(new FakeConverter(failOn: "bad.png")).RunAsync([folder.File("good.png"), folder.File("bad.png")], "jpg", folder.Path));

        Assert.Equal(folder.File("bad.png"), error.FilePath);
        Assert.Equal(["bad.png", "good.png"], folder.Entries());
    }

    [Fact]
    public async Task Run_WhenCancelled_PublishesNothing()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("a.png"), "a");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Runner(new FakeConverter()).RunAsync([folder.File("a.png")], "jpg", folder.Path, null, cancellation.Token));

        Assert.Equal(["a.png"], folder.Entries());
    }

    [Fact]
    public async Task Run_WithoutAMatchingConverter_Fails()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.File("a.png"), "a");

        await Assert.ThrowsAsync<NotSupportedException>(() => Runner().RunAsync([folder.File("a.png")], "jpg", folder.Path));
    }

    private sealed class FakeConverter(string? failOn = null) : IFileConverter
    {
        public bool CanConvert(string sourceFormat, string targetFormat) => true;

        public Task ConvertAsync(string inputPath, string outputPath, string sourceFormat, string targetFormat, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (Path.GetFileName(inputPath) == failOn)
            {
                throw new InvalidDataException("Broken file.");
            }

            progress?.Report(0.5);
            File.WriteAllText(outputPath, "converted");
            return Task.CompletedTask;
        }
    }

    /// <summary><see cref="Progress{T}"/> reports later on the thread pool; tests need the calls in order.</summary>
    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
