using MrWConverter.Core.Media;

namespace MrWConverter.Core.Tests;

public class MediaEditTests
{
    private static TimeRange[] Ranges(MediaEdit edit) => edit.Clips.Select(clip => clip.Range).ToArray();

    [Fact]
    public void New_IsOneKeptClip()
    {
        var edit = new MediaEdit(10);

        Assert.Equal([new TimeRange(0, 10)], Ranges(edit));
        Assert.Equal([new TimeRange(0, 10)], edit.KeptRanges);
        Assert.Equal(10, edit.KeptSeconds);
        Assert.False(edit.HasRemovals);
    }

    [Fact]
    public void Split_CutsTheClipUnderTheTimeAndKeepsBothParts()
    {
        var edit = new MediaEdit(10).SplitAt(4).SplitAt(7);

        Assert.Equal([new TimeRange(0, 4), new TimeRange(4, 7), new TimeRange(7, 10)], Ranges(edit));
        Assert.All(edit.Clips, clip => Assert.True(clip.Kept));
        Assert.False(edit.HasRemovals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.05)]
    [InlineData(9.95)]
    [InlineData(10)]
    public void Split_TooCloseToAnEdge_ChangesNothing(double time)
    {
        var edit = new MediaEdit(10);

        Assert.False(edit.CanSplitAt(time));
        Assert.Same(edit, edit.SplitAt(time));
    }

    [Fact]
    public void Split_OfARemovedClip_StaysRemoved()
    {
        var edit = new MediaEdit(10).SplitAt(5).Remove(0).SplitAt(2);

        Assert.Equal([false, false, true], edit.Clips.Select(clip => clip.Kept));
    }

    [Fact]
    public void Remove_LeavesTheOriginalEditAlone_ForUndo()
    {
        var before = new MediaEdit(10).SplitAt(5);

        var after = before.Remove(0);

        Assert.True(before.Clips[0].Kept);
        Assert.False(after.Clips[0].Kept);
    }

    [Fact]
    public void Remove_TheLastKeptClip_IsRefused()
    {
        var edit = new MediaEdit(10).SplitAt(5).Remove(0);

        Assert.False(edit.CanRemove(1));
        Assert.Same(edit, edit.Remove(1));
    }

    [Fact]
    public void Restore_BringsAClipBack()
    {
        var edit = new MediaEdit(10).SplitAt(5).Remove(0).Restore(0);

        Assert.False(edit.HasRemovals);
    }

    [Fact]
    public void KeptRanges_JoinClipsSideBySideAndSkipRemovedOnes()
    {
        var edit = new MediaEdit(10).SplitAt(2).SplitAt(4).SplitAt(6).SplitAt(8).Remove(2);

        Assert.Equal([new TimeRange(0, 4), new TimeRange(6, 10)], edit.KeptRanges);
        Assert.Equal(8, edit.KeptSeconds, 6);
        Assert.Equal(4, edit.KeptCount);
    }

    [Fact]
    public void MoveBoundary_ShiftsTheLineBetweenTwoClips()
    {
        var edit = new MediaEdit(10).SplitAt(5).MoveBoundary(0, 3);

        Assert.Equal([new TimeRange(0, 3), new TimeRange(3, 10)], Ranges(edit));
    }

    [Theory]
    [InlineData(-5, 4.1)]
    [InlineData(100, 5.9)]
    public void MoveBoundary_StopsShortOfItsNeighbors(double time, double expected)
    {
        var edit = new MediaEdit(10).SplitAt(4).SplitAt(5).SplitAt(6).MoveBoundary(1, time);

        Assert.Equal(expected, edit.Clips[1].Range.End, 6);
    }

    [Fact]
    public void MoveBoundary_AfterTheLastClip_ChangesNothing()
    {
        var edit = new MediaEdit(10);

        Assert.Same(edit, edit.MoveBoundary(0, 5));
    }

    [Fact]
    public void TrimStart_RemovesTheBeginning_AndCanBeUndoneByDraggingBack()
    {
        var trimmed = new MediaEdit(10).TrimStart(3);

        Assert.Equal([new TimeRange(3, 10)], trimmed.KeptRanges);
        Assert.False(trimmed.Clips[0].Kept);

        var back = trimmed.TrimStart(0);
        Assert.Equal([new TimeRange(0, 10)], Ranges(back));
        Assert.False(back.HasRemovals);
    }

    [Fact]
    public void TrimStart_StopsBeforeTheEndOfTheFirstKeptClip()
    {
        var edit = new MediaEdit(10).SplitAt(5).TrimStart(8);

        Assert.Equal(new TimeRange(4.9, 10), edit.KeptRanges[0], new RangeComparer());
    }

    [Fact]
    public void TrimEnd_RemovesTheEnding_AndCanBeUndoneByDraggingBack()
    {
        var trimmed = new MediaEdit(10).TrimEnd(7);

        Assert.Equal([new TimeRange(0, 7)], trimmed.KeptRanges);
        Assert.False(trimmed.Clips[^1].Kept);

        var back = trimmed.TrimEnd(10);
        Assert.Equal([new TimeRange(0, 10)], Ranges(back));
    }

    [Fact]
    public void TrimStartAndEnd_KeepTheCutsInBetween()
    {
        var edit = new MediaEdit(10).SplitAt(5).TrimStart(2).TrimEnd(8);

        Assert.Equal([new TimeRange(0, 2), new TimeRange(2, 5), new TimeRange(5, 8), new TimeRange(8, 10)], Ranges(edit));
        Assert.Equal([false, true, true, false], edit.Clips.Select(clip => clip.Kept));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4.999, 0)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(99, 1)]
    public void ClipIndexAt_FindsTheClipAndCountsTheVeryEndAsTheLast(double time, int index)
    {
        Assert.Equal(index, new MediaEdit(10).SplitAt(5).ClipIndexAt(time));
    }

    [Fact]
    public void NextKept_JumpsOverRemovedClipsAndEndsWithNull()
    {
        var edit = new MediaEdit(10).SplitAt(3).SplitAt(6).Remove(1);

        Assert.Equal(1, edit.NextKept(1));
        Assert.Equal(6, edit.NextKept(4));
        Assert.Equal(6, edit.NextKept(3));
        Assert.Null(edit.NextKept(10));
    }

    private sealed class RangeComparer : IEqualityComparer<TimeRange>
    {
        public bool Equals(TimeRange a, TimeRange b) => Math.Abs(a.Start - b.Start) < 1e-6 && Math.Abs(a.End - b.End) < 1e-6;

        public int GetHashCode(TimeRange range) => 0;
    }
}
