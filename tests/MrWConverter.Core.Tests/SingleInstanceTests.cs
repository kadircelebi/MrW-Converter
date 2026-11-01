using MrWConverter.Core.Settings;

namespace MrWConverter.Core.Tests;

public class SingleInstanceTests
{
    private static string UniqueName() => $"MrWConverterTest-{Guid.NewGuid():N}";

    [Fact]
    public void ASecondClaim_IsRefusedUntilTheFirstEnds()
    {
        var name = UniqueName();

        var first = SingleInstance.TryClaim(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryClaim(name));

        first.Dispose();

        using var again = SingleInstance.TryClaim(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void DifferentNames_DoNotCollide()
    {
        using var first = SingleInstance.TryClaim(UniqueName());
        using var second = SingleInstance.TryClaim(UniqueName());

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Send_DeliversPathsUnchanged()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryClaim(name)!;
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.FilesReceived += paths => received.TrySetResult(paths);
        string[] paths = [@"C:\a b\çığöşü [1] %d.png", @"D:\ikinci dosya.mp4"];

        Assert.True(await SingleInstance.SendAsync(name, paths));

        Assert.Equal(paths, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Send_WithNoFiles_StillReachesTheRunningCopy()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryClaim(name)!;
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.FilesReceived += paths => received.TrySetResult(paths);

        Assert.True(await SingleInstance.SendAsync(name, []));

        Assert.Empty(await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Send_KeepsWorkingForLaterCopies()
    {
        var name = UniqueName();
        using var instance = SingleInstance.TryClaim(name)!;
        var count = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.FilesReceived += _ =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                both.TrySetResult();
            }
        };

        Assert.True(await SingleInstance.SendAsync(name, ["a"]));
        Assert.True(await SingleInstance.SendAsync(name, ["b"]));

        await both.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Send_WithNobodyListening_ReportsFailure()
    {
        Assert.False(await SingleInstance.SendAsync(UniqueName(), ["a"]));
    }
}
