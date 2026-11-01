namespace MrWConverter.Core.Tests;

internal sealed class TemporaryFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mrwconverter-tests-{Guid.NewGuid():N}");

    public TemporaryFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Everything in the folder, including hidden items such as staging folders.</summary>
    public string[] Entries() =>
        Directory.GetFileSystemEntries(Path).Select(entry => System.IO.Path.GetFileName(entry)!).Order().ToArray();

    /// <summary>
    /// Windows can hold a new file briefly, likely for an antivirus scan, and a test's result
    /// shouldn't depend on that. What can't be removed stays in the temporary folder.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < DeleteAttempts)
            {
                Thread.Sleep(DeleteRetryMilliseconds);
            }
            catch (IOException)
            {
                // Out of attempts: leave it.
            }
        }
    }

    private const int DeleteAttempts = 10;
    private const int DeleteRetryMilliseconds = 50;
}
