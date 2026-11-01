namespace MrWConverter.Core.Wheel;

/// <summary>
/// The geometry of a ring of equal sectors. Angles are in degrees, clockwise, with 0 at
/// the top, and the first sector centered there. Coordinates are relative to the center.
/// </summary>
public sealed class WheelLayout(int sectorCount, double innerRadius, double outerRadius)
{
    public const int None = -1;

    public int SectorCount { get; } = sectorCount;

    public double InnerRadius { get; } = innerRadius;

    public double OuterRadius { get; } = outerRadius;

    private double SectorAngle => 360d / SectorCount;

    /// <summary>Returns the sector under a point, or <see cref="None"/> in the middle or outside the ring.</summary>
    public int SectorAt(double x, double y)
    {
        if (SectorCount == 0)
        {
            return None;
        }

        var distance = Math.Sqrt(x * x + y * y);
        if (distance < InnerRadius || distance > OuterRadius)
        {
            return None;
        }

        var angle = Math.Atan2(x, -y) * 180 / Math.PI;
        var shifted = (angle + SectorAngle / 2 + 360) % 360;
        return Math.Min((int)(shifted / SectorAngle), SectorCount - 1);
    }

    public double StartAngle(int sector) => sector * SectorAngle - SectorAngle / 2;

    public double EndAngle(int sector) => sector * SectorAngle + SectorAngle / 2;

    /// <summary>The middle of a sector, where its label goes.</summary>
    public (double X, double Y) LabelCenter(int sector) => PointAt((InnerRadius + OuterRadius) / 2, sector * SectorAngle);

    public static (double X, double Y) PointAt(double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }
}
