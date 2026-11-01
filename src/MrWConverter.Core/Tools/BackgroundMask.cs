namespace MrWConverter.Core.Tools;

/// <summary>
/// The arithmetic around the BEN2 model, kept apart from the model so it can be
/// tested alone: pixels to the model's input, and its output to a transparency mask.
/// </summary>
public static class BackgroundMask
{
    /// <summary>The square size the model works at.</summary>
    public const int ModelSize = 1024;

    private const float MinimumSpread = 0.001f;

    /// <summary>
    /// Turns interleaved RGB bytes into planar floats from 0 to 1. The publisher's own
    /// example feeds the model exactly this; ImageNet normalization visibly hurts it.
    /// </summary>
    public static float[] ToModelInput(byte[] rgb, int size = ModelSize)
    {
        if (rgb.Length != size * size * 3)
        {
            throw new ArgumentException("Expected three bytes for every pixel.", nameof(rgb));
        }

        var input = new float[rgb.Length];

        for (var pixel = 0; pixel < size * size; pixel++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                input[channel * size * size + pixel] = rgb[pixel * 3 + channel] / 255f;
            }
        }

        return input;
    }

    /// <summary>
    /// Stretches the model's output to the full 0-255 range. Returns null when it is
    /// flat, which means the model found no subject and any mask would be meaningless.
    /// </summary>
    public static byte[]? ToMask(ReadOnlySpan<float> prediction)
    {
        if (prediction.IsEmpty)
        {
            return null;
        }

        var lowest = float.MaxValue;
        var highest = float.MinValue;
        foreach (var value in prediction)
        {
            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
        }

        var spread = highest - lowest;
        if (!float.IsFinite(spread) || spread < MinimumSpread)
        {
            return null;
        }

        var mask = new byte[prediction.Length];
        for (var index = 0; index < prediction.Length; index++)
        {
            mask[index] = (byte)Math.Round((prediction[index] - lowest) / spread * 255);
        }

        return mask;
    }
}
