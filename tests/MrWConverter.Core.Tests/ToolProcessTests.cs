using System.Diagnostics;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tests;

/// <summary>Uses tools that ship with Windows, so these run without FFmpeg.</summary>
public class ToolProcessTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly string Ping = Path.Combine(Environment.SystemDirectory, "ping.exe");

    [Fact]
    public async Task Run_ReturnsOutputAndReportsEachLine()
    {
        var lines = new List<string>();

        var output = await ToolProcess.RunAsync(Cmd, ["/c", "echo first& echo second"], lines.Add);

        Assert.Equal(["first", "second"], lines.Select(line => line.Trim()));
        Assert.Contains("second", output);
    }

    [Fact]
    public async Task Run_PassesAnArgumentThatLooksLikeACommandAsOneValue()
    {
        var output = await ToolProcess.RunAsync(Cmd, ["/c", "echo", "a b & c"]);

        Assert.Contains("a b", output);
    }

    [Fact]
    public async Task Run_WhenToolFails_IncludesItsErrorOutput()
    {
        var error = await Assert.ThrowsAsync<ToolFailedException>(() =>
            ToolProcess.RunAsync(Cmd, ["/c", "echo broken file 1>&2 & exit 3"]));

        Assert.Contains("code 3", error.Message);
        Assert.Contains("broken file", error.Message);
    }

    [Fact]
    public async Task Run_WhenCancelled_StopsTheToolQuickly()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolProcess.RunAsync(Ping, ["-n", "60", "127.0.0.1"], null, cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Run_WhenAlreadyCancelled_DoesNotStartTheTool()
    {
        var missing = Path.Combine(Environment.SystemDirectory, "does-not-exist.exe");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolProcess.RunAsync(missing, [], null, new CancellationToken(canceled: true)));
    }
}
