using System.Security.Cryptography;

namespace MrWConverter.Core.Office;

/// <summary>Getting LibreOffice failed, and the user can do something about it, such as reconnecting.</summary>
public sealed class OfficeDownloadException(string message, Exception? cause = null) : Exception(message, cause);

/// <summary>
/// Finds LibreOffice for converting Office files, and fetches it when the user agrees. It is
/// used from inside the package if the package has it, then from our own copy, then from an
/// existing installation. Nothing is installed in Windows: the official installer is only
/// read as data, into a folder Windows removes along with the app.
/// </summary>
public sealed class OfficeRuntime(string privateFolder, string packageFolder)
{
    public const long DownloadBytes = 375_934_976;

    private const string Version = "26.8.2";
    private const string Sha256 = "ff4b911403b67ee9e6249f820f9782c44bb32c94fdaa45b0da53a8e419e5307a";
    private const double DownloadShare = 0.8;
    private const int BufferSize = 1 << 20;

    // Its Python part can hang on first start, and converting files doesn't use it.
    private const string PythonLoader = "pythonloaderlo.dll";

    private static readonly Uri[] Sources =
    [
        new($"https://download.documentfoundation.org/libreoffice/stable/{Version}/win/x86_64/LibreOffice_{Version}_Win_x86-64.msi"),
        new($"https://downloadarchive.documentfoundation.org/libreoffice/old/{Version}.2/win/x86_64/LibreOffice_{Version}.2_Win_x86-64.msi"),
    ];

    public bool IsReady => Executable is not null;

    public string? Executable => new[]
        {
            Path.Combine(packageFolder, "office"),
            OwnCopy,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice"),
        }
        .Select(folder => Path.Combine(folder, "program", "soffice.com"))
        .FirstOrDefault(File.Exists);

    /// <summary>Where LibreOffice keeps its settings. Making them takes it ten seconds, so they are made once.</summary>
    public string ProfileFolder => Path.Combine(privateFolder, "profile");

    /// <summary>
    /// For copies LibreOffice has to read. Not the temporary folder, because Windows redirects what a
    /// packaged app writes there to a place that programs it starts can't see.
    /// </summary>
    public string WorkFolder => Path.Combine(privateFolder, "work");

    private string OwnCopy => Path.Combine(privateFolder, $"libreoffice-{Version}");

    /// <param name="progress">Receives the finished fraction, from 0 to 1.</param>
    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (IsReady)
        {
            return;
        }

        Directory.CreateDirectory(privateFolder);
        var work = Path.Combine(privateFolder, $".download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var installer = Path.Combine(work, "libreoffice.msi");
            await DownloadInstallerAsync(installer, progress, cancellationToken);

            var unpacked = Path.Combine(work, "unpacked");
            await MsiPackage.ExtractAsync(
                installer,
                unpacked,
                progress is null ? null : new Progress<double>(value => progress.Report(DownloadShare + value * (1 - DownloadShare))),
                cancellationToken);
            File.Delete(installer);

            var program = Directory.GetFiles(unpacked, "soffice.com", SearchOption.AllDirectories).Single();
            var application = Path.GetDirectoryName(Path.GetDirectoryName(program))!;
            File.Delete(Path.Combine(application, "program", PythonLoader));

            // Last, so that a folder under this name is always a complete copy.
            Directory.Move(application, OwnCopy);
            progress?.Report(1);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OfficeDownloadException or OutOfMemoryException))
        {
            // Whatever goes wrong here, the app stays usable and only this one feature waits.
            throw new OfficeDownloadException($"LibreOffice could not be set up: {exception.Message}", exception);
        }
        finally
        {
            DeleteQuietly(work);
        }
    }

    private static async Task DownloadInstallerAsync(string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Exception? lastProblem = null;
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        foreach (var source in Sources)
        {
            try
            {
                await DownloadFromAsync(client, source, path, progress, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
            {
                lastProblem = exception;
            }
        }

        throw new OfficeDownloadException($"LibreOffice could not be downloaded: {lastProblem?.Message}", lastProblem);
    }

    private static async Task DownloadFromAsync(HttpClient client, Uri source, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[BufferSize];
        var total = 0L;

        while (await input.ReadAsync(buffer, cancellationToken) is var read and > 0)
        {
            total += read;

            // Stops a wrong file early rather than filling the disk with it.
            if (total > DownloadBytes)
            {
                throw new InvalidDataException("The download is larger than the expected file.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report((double)total / DownloadBytes * DownloadShare);
        }

        // The mirror is whichever one the foundation picks, so the file only counts if it is byte for byte the one we pinned.
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The download is not the expected file.");
        }
    }

    private static void DeleteQuietly(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It only takes up space in the app's own folder, and removing the app removes it.
        }
    }
}
