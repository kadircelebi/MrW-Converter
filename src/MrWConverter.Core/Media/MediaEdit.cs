namespace MrWConverter.Core.Media;

public readonly record struct TimeRange(double Start, double End)
{
    public double Length => End - Start;

    public bool Contains(double seconds) => seconds >= Start && seconds < End;
}

public sealed record EditClip(TimeRange Range, bool Kept);

/// <summary>
/// One file cut into clips that together cover its whole length, each kept or removed.
/// Every change returns a new edit, or this same one if the change isn't possible,
/// so an editor gets undo by holding on to the old ones.
/// </summary>
public sealed class MediaEdit
{
    public const double MinimumClipSeconds = 0.1;

    private MediaEdit(double duration, IReadOnlyList<EditClip> clips)
    {
        Duration = duration;
        Clips = clips;
    }

    public MediaEdit(double duration)
        : this(duration, [new EditClip(new TimeRange(0, duration), true)])
    {
    }

    public double Duration { get; }

    public IReadOnlyList<EditClip> Clips { get; }

    public IEnumerable<EditClip> KeptClips => Clips.Where(clip => clip.Kept);

    public double KeptSeconds => KeptClips.Sum(clip => clip.Range.Length);

    public int KeptCount => KeptClips.Count();

    public bool HasRemovals => Clips.Any(clip => !clip.Kept);

    /// <summary>What stays, with clips that sit side by side joined back together.</summary>
    public IReadOnlyList<TimeRange> KeptRanges
    {
        get
        {
            List<TimeRange> ranges = [];
            foreach (var clip in KeptClips)
            {
                if (ranges.Count > 0 && ranges[^1].End >= clip.Range.Start)
                {
                    ranges[^1] = ranges[^1] with { End = clip.Range.End };
                }
                else
                {
                    ranges.Add(clip.Range);
                }
            }

            return ranges;
        }
    }

    /// <summary>The clip at this time. The very end of the file belongs to the last clip.</summary>
    public int ClipIndexAt(double seconds)
    {
        for (var index = 0; index < Clips.Count; index++)
        {
            if (Clips[index].Range.Contains(seconds))
            {
                return index;
            }
        }

        return seconds <= 0 ? 0 : Clips.Count - 1;
    }

    /// <summary>The first kept time at or after this one, or null when nothing kept follows.</summary>
    public double? NextKept(double seconds)
    {
        foreach (var clip in KeptClips)
        {
            if (clip.Range.End > seconds)
            {
                return Math.Max(seconds, clip.Range.Start);
            }
        }

        return null;
    }

    public bool CanSplitAt(double seconds)
    {
        var range = Clips[ClipIndexAt(seconds)].Range;
        return seconds - range.Start >= MinimumClipSeconds && range.End - seconds >= MinimumClipSeconds;
    }

    public MediaEdit SplitAt(double seconds)
    {
        if (!CanSplitAt(seconds))
        {
            return this;
        }

        var index = ClipIndexAt(seconds);
        var clip = Clips[index];
        return Replacing(
            index,
            1,
            clip with { Range = clip.Range with { End = seconds } },
            clip with { Range = clip.Range with { Start = seconds } });
    }

    /// <summary>False for the only kept clip, because a result needs something in it.</summary>
    public bool CanRemove(int index) => Clips[index].Kept && KeptCount > 1;

    public MediaEdit Remove(int index) =>
        CanRemove(index) ? Replacing(index, 1, Clips[index] with { Kept = false }) : this;

    public MediaEdit Restore(int index) =>
        Clips[index].Kept ? this : Replacing(index, 1, Clips[index] with { Kept = true });

    /// <summary>Moves the line after this clip, staying inside the clips on either side of it.</summary>
    public MediaEdit MoveBoundary(int index, double seconds)
    {
        if (index < 0 || index >= Clips.Count - 1)
        {
            return this;
        }

        var (left, right) = (Clips[index], Clips[index + 1]);
        var time = Math.Clamp(seconds, left.Range.Start + MinimumClipSeconds, right.Range.End - MinimumClipSeconds);
        return Replacing(
            index,
            2,
            left with { Range = left.Range with { End = time } },
            right with { Range = right.Range with { Start = time } });
    }

    /// <summary>Removes everything before this time, up to the end of the first kept clip.</summary>
    public MediaEdit TrimStart(double seconds)
    {
        var first = FirstKeptIndex;
        var time = Math.Min(seconds, Clips[first].Range.End - MinimumClipSeconds);
        var kept = Clips[first] with { Range = Clips[first].Range with { Start = time < MinimumClipSeconds ? 0 : time } };

        return time < MinimumClipSeconds
            ? Replacing(0, first + 1, kept)
            : Replacing(0, first + 1, new EditClip(new TimeRange(0, time), false), kept);
    }

    /// <summary>Removes everything after this time, back to the start of the last kept clip.</summary>
    public MediaEdit TrimEnd(double seconds)
    {
        var last = LastKeptIndex;
        var time = Math.Max(seconds, Clips[last].Range.Start + MinimumClipSeconds);
        var count = Clips.Count - last;
        var kept = Clips[last] with { Range = Clips[last].Range with { End = Duration - time < MinimumClipSeconds ? Duration : time } };

        return Duration - time < MinimumClipSeconds
            ? Replacing(last, count, kept)
            : Replacing(last, count, kept, new EditClip(new TimeRange(time, Duration), false));
    }

    public int FirstKeptIndex => Clips.ToList().FindIndex(clip => clip.Kept);

    public int LastKeptIndex => Clips.ToList().FindLastIndex(clip => clip.Kept);

    private MediaEdit Replacing(int index, int count, params EditClip[] clips)
    {
        var replaced = Clips.ToList();
        replaced.RemoveRange(index, count);
        replaced.InsertRange(index, clips);
        return new MediaEdit(Duration, replaced);
    }
}
