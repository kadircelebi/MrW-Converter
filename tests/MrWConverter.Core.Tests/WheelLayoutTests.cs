using MrWConverter.Core.Wheel;

namespace MrWConverter.Core.Tests;

public class WheelLayoutTests
{
    [Theory]
    [InlineData(0, -100, 0)] // top
    [InlineData(100, 0, 1)] // right
    [InlineData(0, 100, 2)] // bottom
    [InlineData(-100, 0, 3)] // left
    public void SectorAt_GoesClockwiseFromTheTop(double x, double y, int expected)
    {
        Assert.Equal(expected, new WheelLayout(4, 50, 150).SectorAt(x, y));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(0, -49)]
    [InlineData(0, -151)]
    [InlineData(200, 200)]
    public void SectorAt_IsNoneInTheMiddleAndOutside(double x, double y)
    {
        Assert.Equal(WheelLayout.None, new WheelLayout(4, 50, 150).SectorAt(x, y));
    }

    [Fact]
    public void SectorAt_WithNoSectors_IsNone()
    {
        Assert.Equal(WheelLayout.None, new WheelLayout(0, 50, 150).SectorAt(0, -100));
    }

    [Fact]
    public void SectorAt_WithOneSector_CoversTheWholeRing()
    {
        var layout = new WheelLayout(1, 50, 150);

        foreach (var (x, y) in new[] { (0d, -100d), (100d, 0d), (0d, 100d), (-100d, 0d) })
        {
            Assert.Equal(0, layout.SectorAt(x, y));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    public void LabelCenter_LiesInsideItsOwnSector(int count)
    {
        var layout = new WheelLayout(count, 60, 170);

        for (var sector = 0; sector < count; sector++)
        {
            var (x, y) = layout.LabelCenter(sector);
            Assert.Equal(sector, layout.SectorAt(x, y));
        }
    }

    [Fact]
    public void Sectors_MeetWithoutGapsOrOverlap()
    {
        var layout = new WheelLayout(5, 60, 170);

        for (var sector = 0; sector < 4; sector++)
        {
            Assert.Equal(layout.EndAngle(sector), layout.StartAngle(sector + 1), 6);
        }

        Assert.Equal(360, layout.EndAngle(4) - layout.StartAngle(0), 6);
    }
}
