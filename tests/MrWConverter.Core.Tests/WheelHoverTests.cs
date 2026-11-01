using MrWConverter.Core.Wheel;

namespace MrWConverter.Core.Tests;

public class WheelHoverTests
{
    [Fact]
    public void ReachingASlice_CanAct()
    {
        var hover = new WheelHover();

        Assert.True(hover.MoveTo(2));
        Assert.True(hover.CanAct);
    }

    [Fact]
    public void StayingOnASlice_IsNotAChange()
    {
        var hover = new WheelHover();
        hover.MoveTo(2);

        Assert.False(hover.MoveTo(2));
    }

    [Fact]
    public void OffTheWheel_CannotAct()
    {
        var hover = new WheelHover();
        hover.MoveTo(2);
        hover.MoveTo(WheelLayout.None);

        Assert.False(hover.CanAct);
    }

    [Fact]
    public void AfterTheWheelChanges_TheSliceUnderThePointerStaysInert()
    {
        var hover = new WheelHover();

        hover.Landed(2);

        Assert.False(hover.CanAct);
    }

    [Fact]
    public void CrossingAGap_DoesNotWakeTheInertSlice()
    {
        var hover = new WheelHover();
        hover.Landed(2);

        hover.MoveTo(WheelLayout.None);
        Assert.True(hover.MoveTo(2));

        Assert.False(hover.CanAct);
    }

    [Fact]
    public void LeavingForAnotherSliceAndReturning_WakesIt()
    {
        var hover = new WheelHover();
        hover.Landed(2);

        hover.MoveTo(0);
        Assert.True(hover.CanAct);

        hover.MoveTo(2);
        Assert.True(hover.CanAct);
    }
}
