using System.IO.Pipes;
using System.Text;

namespace MrWConverter.Core.Settings;

/// <summary>
/// Keeps one copy of the app per Windows session, because two copies would each open a
/// wheel for the same drag. A later copy hands its files over and quits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int SendTimeoutMilliseconds = 2000;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _pipeName = name;
    }

    /// <summary>Raised on a background thread when a later copy starts, with the files it was given.</summary>
    public event Action<IReadOnlyList<string>>? FilesReceived;

    /// <summary>Null means another copy already holds this name.</summary>
    public static SingleInstance? TryClaim(string name)
    {
        var mutex = new Mutex(true, $@"Local\{name}", out var claimed);
        if (!claimed)
        {
            mutex.Dispose();
            return null;
        }

        var instance = new SingleInstance(mutex, name);
        _ = instance.ListenAsync();
        return instance;
    }

    /// <summary>Hands the files to the running copy. False means it could not be reached.</summary>
    public static async Task<bool> SendAsync(string name, IEnumerable<string> paths)
    {
        try
        {
            // CurrentUserOnly, so another user on the machine can't pose as either end.
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(SendTimeoutMilliseconds);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8);
            await writer.WriteAsync(string.Join('\n', paths));
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(SendTimeoutMilliseconds);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(timeout.Token);

                // A path can't contain a line break, so it can't split one path into two.
                FilesReceived?.Invoke(message.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
            {
                // A later copy stalled mid-message. Ignore it and wait for the next one.
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A later copy quit mid-message. Ignore it and wait for the next one.
            }
        }
    }
}
