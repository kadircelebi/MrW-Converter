using System.Runtime.InteropServices;
using System.Text;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Office;

/// <summary>
/// Reads the files out of a Windows Installer package without installing it: no setup code
/// runs, nothing is registered, and nothing outside the destination changes.
/// </summary>
internal static class MsiPackage
{
    private const uint Success = 0;
    private const uint NoMoreItems = 259;
    private const int FieldCapacity = 4096;
    private const int StreamBuffer = 1 << 16;

    private static readonly string[] SystemFolders = ["System64", "System"];

    /// <param name="progress">Receives the finished fraction, from 0 to 1.</param>
    public static Task ExtractAsync(string package, string destination, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Extract(package, destination, progress, cancellationToken), cancellationToken);

    /// <summary>The folder a file goes in, from its directory's row and those of its parents.</summary>
    internal static string RelativeFolder(string directory, IReadOnlyDictionary<string, (string Parent, string Name)> directories, int depth = 0)
    {
        if (depth > directories.Count)
        {
            throw new InvalidDataException("The package's folders form a loop.");
        }

        if (!directories.TryGetValue(directory, out var row) || directory == "TARGETDIR")
        {
            return "";
        }

        var parent = RelativeFolder(row.Parent, directories, depth + 1);
        var name = LongName(row.Name);
        return name is "." or "" ? parent : Path.Combine(parent, name);
    }

    /// <summary>A package stores "short|long" names, and sometimes "target:source" pairs of them.</summary>
    internal static string LongName(string name)
    {
        var target = name.Split(':')[0];
        return target.Split('|')[^1];
    }

    /// <summary>Refuses a name that would land outside the destination, whatever the package claims.</summary>
    internal static string SafePath(string destination, string relative)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(destination, relative));

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new InvalidDataException($"The package has a file outside its folder: {relative}");
    }

    private static void Extract(string package, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var unpacked = destination + ".cabinets";
        Directory.CreateDirectory(unpacked);

        Check(MsiOpenDatabase(package, IntPtr.Zero, out var database));
        try
        {
            ExpandCabinets(database, unpacked, cancellationToken);

            var directories = Rows(database, "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", 3)
                .ToDictionary(row => row[0], row => (row[1], row[2]));
            var components = Rows(database, "SELECT `Component`, `Directory_` FROM `Component`", 2)
                .ToDictionary(row => row[0], row => row[1]);
            var files = Rows(database, "SELECT `File`, `Component_`, `FileName` FROM `File`", 3);

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (key, component, fileName) = (files[index][0], files[index][1], LongName(files[index][2]));
                var source = SafePath(unpacked, key);
                var folder = RelativeFolder(components[component], directories);

                if (!File.Exists(source))
                {
                    throw new InvalidDataException($"The package is missing {fileName}.");
                }

                // Runtime libraries meant for Windows' own folders. The package has them for several
                // processors under the same names, and the app's own dependency provides the right one.
                if (SystemFolders.Contains(folder.Split(Path.DirectorySeparatorChar)[0]))
                {
                    continue;
                }

                var target = SafePath(destination, Path.Combine(folder, fileName));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: true);
                progress?.Report((index + 1d) / files.Count);
            }
        }
        finally
        {
            _ = MsiCloseHandle(database);
            Directory.Delete(unpacked, recursive: true);
        }
    }

    private static void ExpandCabinets(uint database, string folder, CancellationToken cancellationToken)
    {
        var expand = Path.Combine(Environment.SystemDirectory, "expand.exe");

        foreach (var cabinet in Rows(database, "SELECT `Cabinet` FROM `Media`", 1).Select(row => row[0]).Where(name => name.StartsWith('#')))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = cabinet[1..];
            var path = SafePath(folder, name);
            WriteStream(database, name, path, cancellationToken);

            ToolProcess.RunAsync(expand, [path, "-F:*", folder], null, cancellationToken).GetAwaiter().GetResult();
            File.Delete(path);
        }
    }

    private static void WriteStream(uint database, string name, string path, CancellationToken cancellationToken)
    {
        if (name.Contains('\''))
        {
            throw new InvalidDataException("The package has an unexpected cabinet name.");
        }

        Check(MsiDatabaseOpenView(database, $"SELECT `Data` FROM `_Streams` WHERE `Name`='{name}'", out var view));
        try
        {
            Check(MsiViewExecute(view, 0));
            Check(MsiViewFetch(view, out var record));
            try
            {
                using var output = File.Create(path);
                var buffer = new byte[StreamBuffer];

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var size = (uint)buffer.Length;
                    Check(MsiRecordReadStream(record, 1, buffer, ref size));
                    if (size == 0)
                    {
                        return;
                    }

                    output.Write(buffer, 0, (int)size);
                }
            }
            finally
            {
                _ = MsiCloseHandle(record);
            }
        }
        finally
        {
            _ = MsiCloseHandle(view);
        }
    }

    private static List<string[]> Rows(uint database, string query, int fields)
    {
        var rows = new List<string[]>();

        Check(MsiDatabaseOpenView(database, query, out var view));
        try
        {
            Check(MsiViewExecute(view, 0));

            while (true)
            {
                var result = MsiViewFetch(view, out var record);
                if (result == NoMoreItems)
                {
                    return rows;
                }

                Check(result);
                try
                {
                    var row = new string[fields];
                    for (var field = 0; field < fields; field++)
                    {
                        var text = new StringBuilder(FieldCapacity);
                        var length = (uint)FieldCapacity;
                        Check(MsiRecordGetString(record, (uint)field + 1, text, ref length));
                        row[field] = text.ToString();
                    }

                    rows.Add(row);
                }
                finally
                {
                    _ = MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            _ = MsiCloseHandle(view);
        }
    }

    private static void Check(uint result)
    {
        if (result != Success)
        {
            throw new InvalidDataException($"Windows Installer could not read the package (code {result}).");
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out uint database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(uint view, uint record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(uint view, out uint record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetString(uint record, uint field, StringBuilder buffer, ref uint length);

    [DllImport("msi.dll")]
    private static extern uint MsiRecordReadStream(uint record, uint field, byte[] buffer, ref uint size);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint handle);
}
