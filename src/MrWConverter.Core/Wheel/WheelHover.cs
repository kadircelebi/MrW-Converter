namespace MrWConverter.Core.Wheel;

/// <summary>
/// Tracks which slice the pointer is on, for slices that act when the pointer rests on them.
/// After the wheel changes, the slice under the pointer stays inert until the pointer reaches
/// a different slice. Passing over a gap doesn't count, because during a drag Windows reports
/// the pointer as gone whenever it crosses a transparent pixel, and a redrawn wheel causes that
/// too; counting it would flip the wheel straight back.
/// </summary>
public sealed class WheelHover
{
    private int _resting = WheelLayout.None;

    public int Current { get; private set; } = WheelLayout.None;

    /// <summary>True when resting on the current slice may act.</summary>
    public bool CanAct => Current != WheelLayout.None && Current != _resting;

    /// <summary>Records the pointer's slice. Returns false if it is still on the same one.</summary>
    public bool MoveTo(int sector)
    {
        if (sector == Current)
        {
            return false;
        }

        Current = sector;

        if (sector != WheelLayout.None && sector != _resting)
        {
            _resting = WheelLayout.None;
        }

        return true;
    }

    /// <summary>Call after the wheel changes, with the new slice under the pointer.</summary>
    public void Landed(int sector)
    {
        Current = sector;
        _resting = sector;
    }
}
