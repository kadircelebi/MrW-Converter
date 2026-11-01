using System.Diagnostics;

namespace MrWConverter.Core.Output;

/// <summary>
/// Collects the files of one job in a hidden folder inside the destination, then
/// publishes them together. A failed or cancelled job leaves nothing behind, and
/// an existing file is never overwritten.
/// </summary>
public sealed class OutputBatch : IDisposable
{
    private readonly string _destination;
    private readonly string _stagingDirectory;
    private readonly List<StagedFile> _stagedFiles = [];
    private readonly List<string> _publishedFiles = [];
    private bool _committed;

    public OutputBatch(string destination)
    {
        _destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(_destination);

        // Staying on the destination's volume makes each publish a plain rename.
        _stagingDirectory = Path.Combine(_destination, $".mrwconverter-{Guid.NewGuid():N}");
        var staging = Directory.CreateDirectory(_stagingDirectory);
        staging.Attributes |= FileAttributes.Hidden;
    }

    /// <summary>Reserves a working path for an output that will be published as <paramref name="fileName"/>.</summary>
    public string Stage(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("Expected a file name without a folder.", nameof(fileName));
        }

        var stagedPath = Path.Combine(_stagingDirectory, $"{_stagedFiles.Count}{Path.GetExtension(fileName)}");
        _stagedFiles.Add(new StagedFile(stagedPath, fileName));
        return stagedPath;
    }

    public IReadOnlyList<string> Commit()
    {
        foreach (var file in _stagedFiles)
        {
            var info = new FileInfo(file.StagedPath);
            if (!info.Exists || info.Length == 0)
            {
                throw new InvalidDataException($"No output was produced for {file.FileName}.");
            }
        }

        foreach (var file in _stagedFiles)
        {
            _publishedFiles.Add(Publish(file));
        }

        _committed = true;
        return _publishedFiles.ToArray();
    }

    /// <summary>
    /// Cleanup must not replace the job's real outcome, for example when an antivirus
    /// scan still holds a staged file. A folder left that way is hidden and harmless.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (!_committed)
            {
                foreach (var path in _publishedFiles)
                {
                    File.Delete(path);
                }
            }

            if (Directory.Exists(_stagingDirectory))
            {
                Directory.Delete(_stagingDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not clean up {0}: {1}", _stagingDirectory, exception.Message);
        }
    }

    private string Publish(StagedFile file)
    {
        for (var number = 1; ; number++)
        {
            var candidate = Path.Combine(_destination, NumberedName(file.FileName, number));
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Move(file.StagedPath, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another program took this name after the check; try the next number.
            }
        }
    }

    private static string NumberedName(string fileName, int number) =>
        number == 1
            ? fileName
            : $"{Path.GetFileNameWithoutExtension(fileName)} ({number}){Path.GetExtension(fileName)}";

    private sealed record StagedFile(string StagedPath, string FileName);
}
