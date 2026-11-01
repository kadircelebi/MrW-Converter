using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MrWConverter.App.Resources;

namespace MrWConverter.App.Editor;

/// <summary>Shows the frame at a time as a slider moves. Only the latest request counts, so dragging stays smooth.</summary>
public sealed class FrameLoader(Func<double, CancellationToken, Task<byte[]>> read, Action<ImageSource> show) : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);

    private CancellationTokenSource? _current;

    public async void Show(double seconds)
    {
        _current?.Cancel();
        _current?.Dispose();

        var current = new CancellationTokenSource();
        _current = current;
        var token = current.Token;

        try
        {
            await Task.Delay(SettleDelay, token);
            show(Decode(await read(seconds, token)));
        }
        catch (OperationCanceledException)
        {
            // A newer position took over, or the window closed.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Saving can still work without a picture, and fails with its own message if it can't.
            Trace.TraceWarning("No frame at {0}: {1}", seconds, exception);
        }
    }

    public void Dispose()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }

    public static ImageSource Decode(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>"1:02.5", with hours only when the file is that long.</summary>
    public static string TimeText(double seconds, double duration) =>
        TimeSpan.FromSeconds(seconds).ToString(duration >= 3600 ? @"h\:mm\:ss\.f" : @"m\:ss\.f");
}
