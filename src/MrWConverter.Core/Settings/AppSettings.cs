using System.Diagnostics;
using System.Text.Json;

namespace MrWConverter.Core.Settings;

/// <summary>
/// User preferences in a small JSON file. A packaged app's AppData is private to it,
/// and Windows removes it when the app is uninstalled.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Off until the user turns it on: the wheel appears over other apps, so that is their choice.</summary>
    public bool DragWheelEnabled { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // No file yet, or a damaged one: start from the defaults.
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save settings: {0}", exception.Message);
        }
    }
}
