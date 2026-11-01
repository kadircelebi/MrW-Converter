namespace MrWConverter.Core.Tools;

/// <summary>What the user asked Shrink for. Without a size target, the result still may not outgrow the original.</summary>
/// <param name="MaxBytes">A limit for each file, or null.</param>
/// <param name="SizeShare">A share of each file's own size, from 0 to 1, or null.</param>
/// <param name="MaxSide">The longest side in pixels, or null to keep it.</param>
public sealed record ShrinkOptions(long? MaxBytes = null, double? SizeShare = null, int? MaxSide = null)
{
    public const string ToolId = "shrink";

    /// <summary>The most this file may come to. It is never more than the file is now.</summary>
    public long LimitFor(long sourceBytes) =>
        Math.Min(sourceBytes, MaxBytes ?? (SizeShare is { } share ? (long)(sourceBytes * share) : sourceBytes));

    /// <summary>True when a side this long has to get shorter.</summary>
    public bool Resizes(long longestSide) => MaxSide is { } side && side < longestSide;
}

/// <summary>Even the smallest result allowed is over the target.</summary>
public sealed class CannotShrinkException(string message) : Exception(message);
