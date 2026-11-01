using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MrWConverter.App.ViewModels;

public sealed partial class FileEntry : ObservableObject
{
    private const int ThumbnailPixels = 112;
    private const long MaxThumbnailBytes = 64 * 1024 * 1024;

    private readonly string _group;

    public FileEntry(string path, string format, string group)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Details = $"{format.ToUpperInvariant()} · {FormatSize(new FileInfo(path).Length)}";
        _group = group;
    }

    public string Path { get; }

    public string Name { get; }

    public string Details { get; }

    /// <summary>A Segoe Fluent Icons character, shown until a thumbnail replaces it.</summary>
    public string Glyph => _group switch
    {
        "image" => "",
        "audio" => "",
        _ => "",
    };

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    /// <summary>Screen readers announce list items through this.</summary>
    public override string ToString() => Name;

    public async Task LoadThumbnailAsync()
    {
        if (_group == "image")
        {
            Thumbnail = await Task.Run(() => ReadThumbnail(Path));
        }
    }

    /// <summary>A thumbnail is a nicety: any file Windows can't decode keeps its icon.</summary>
    private static BitmapImage? ReadThumbnail(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxThumbnailBytes)
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = ThumbnailPixels;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.Rotation = ReadRotation(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            Trace.TraceInformation("No thumbnail for {0}: {1}", path, exception.Message);
            return null;
        }
    }

    private static Rotation ReadRotation(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var orientation = (frame.Metadata as BitmapMetadata)?.ContainsQuery("System.Photo.Orientation") == true
            ? ((BitmapMetadata)frame.Metadata).GetQuery("System.Photo.Orientation") as ushort?
            : null;

        return orientation switch
        {
            3 => Rotation.Rotate180,
            6 => Rotation.Rotate90,
            8 => Rotation.Rotate270,
            _ => Rotation.Rotate0,
        };
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.CurrentCulture, unit == 0 ? "{0:N0} {1}" : "{0:N1} {1}", size, units[unit]);
    }
}

public sealed class TargetOption(string format)
{
    public string Format { get; } = format;

    public string Label { get; } = format.ToUpperInvariant();

    public override string ToString() => Label;
}

/// <summary>A tool offered for the selected files.</summary>
public sealed class ToolOption(string id, string label, string glyph)
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public string Glyph { get; } = glyph;

    public override string ToString() => Label;
}

/// <summary>One slice of the wheel: a format to convert to, a tool to run, or a way to another ring.</summary>
public sealed record WheelChoice(string Id, string Label, bool IsTool, IReadOnlyList<WheelChoice>? Items = null, bool GoesBack = false)
{
    /// <summary>True when choosing this changes the wheel instead of starting a job.</summary>
    public bool Navigates => Items is not null || GoesBack;

    public static WheelChoice ForFormat(string format) => new(format, format.ToUpperInvariant(), IsTool: false);

    public static WheelChoice ForTool(ToolOption tool) => new(tool.Id, tool.Label, IsTool: true);

    public static WheelChoice Menu(string label, IReadOnlyList<WheelChoice> items) => new("menu", label, IsTool: false, items);

    public static WheelChoice Back(string label) => new("back", label, IsTool: false, GoesBack: true);
}

public enum BannerKind
{
    None,
    Info,
    Success,
    Error,
}
