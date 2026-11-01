using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace MrWConverter.App.Resources;

/// <summary>UI text from Strings.resx. Windows' display language picks the translation.</summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("MrWConverter.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Intro => Get();
    public static string DropHint => Get();
    public static string DropNow => Get();
    public static string PrivacyNote => Get();
    public static string KindImage => Get();
    public static string KindAudio => Get();
    public static string KindVideo => Get();
    public static string ChooseFiles => Get();
    public static string AddFiles => Get();
    public static string ClearFiles => Get();
    public static string SelectedFiles => Get();
    public static string RemoveFile => Get();
    public static string TargetFormat => Get();
    public static string SaveTo => Get();
    public static string ChangeFolder => Get();
    public static string OpenFolder => Get();
    public static string Convert => Get();
    public static string Cancel => Get();
    public static string ConversionProgress => Get();
    public static string ChooseFilesDialogTitle => Get();
    public static string ChooseFolderDialogTitle => Get();
    public static string SupportedFilesFilter => Get();
    public static string AllFilesFilter => Get();
    public static string StatusCancelled => Get();
    public static string StatusNoCommonTarget => Get();
    public static string UnexpectedErrorTitle => Get();
    public static string UnexpectedError => Get();
    public static string About => Get();
    public static string AboutTitle => Get();
    public static string AboutFfmpegNotice => Get();
    public static string AboutVersion => Get();
    public static string AboutNoticesUnavailable => Get();
    public static string Close => Get();
    public static string DragWheel => Get();
    public static string DragWheelHelp => Get();
    public static string StartWithWindows => Get();
    public static string StartWithWindowsHelp => Get();
    public static string StartWithWindowsBlocked => Get();
    public static string TrayOpen => Get();
    public static string TrayHideOnClose => Get();
    public static string TrayExit => Get();
    public static string CloseAppDontAsk => Get();
    public static string OpenWheel => Get();
    public static string StatusBusy => Get();
    public static string ToolsTitle => Get();
    public static string ToolRemoveBackground => Get();
    public static string CloseAppQuestion => Get();
    public static string CloseAppHelp => Get();
    public static string CloseAppBusy => Get();
    public static string CloseAppHide => Get();
    public static string ToolCrop => Get();
    public static string WheelBack => Get();
    public static string ToolCensor => Get();
    public static string CensorArea => Get();
    public static string CensorAdd => Get();
    public static string CensorRemove => Get();
    public static string CensorHelp => Get();
    public static string CensorSave => Get();
    public static string CensorStyleName => Get();
    public static string CensorStyleNoise => Get();
    public static string CensorStyleBlack => Get();
    public static string CensorStyleMosaic => Get();
    public static string CensorMosaicSize => Get();
    public static string CensorMosaicWarning => Get();
    public static string CropTitle => Get();
    public static string CropArea => Get();
    public static string CropHelp => Get();
    public static string CropFree => Get();
    public static string CropSave => Get();
    public static string ToolTrim => Get();
    public static string ToolCropVideo => Get();
    public static string ToolMuteVideo => Get();
    public static string ToolSnapshot => Get();
    public static string TrimStart => Get();
    public static string TrimEnd => Get();
    public static string TrimHelp => Get();
    public static string TrimSave => Get();
    public static string SnapshotTime => Get();
    public static string SnapshotHelp => Get();
    public static string SnapshotSave => Get();
    public static string StatusMediaFeatureMissing => Get();

    // Screen readers need the text without the access key marker.
    public static string TargetFormatName => TargetFormat.Replace("_", "");

    public static string FileCount(int count) => Format(count);
    public static string CensorCount(int count) => Format(count);
    public static string TrimKept(string duration) => Format(duration);
    public static string ConvertTo(string format) => Format(format);
    public static string StatusRejected(int count) => Format(count);
    public static string StatusDone(int count) => Format(count);
    public static string StatusFileFailed(string fileName) => Format(fileName);
    public static string StatusSaveFailed(string folder) => Format(folder);

    private static string Get([CallerMemberName] string key = "") =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(object value, [CallerMemberName] string key = "") =>
        string.Format(CultureInfo.CurrentCulture, Get(key), value);
}
