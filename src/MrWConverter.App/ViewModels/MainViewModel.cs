using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MrWConverter.App.Resources;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Media;
using MrWConverter.Core.Settings;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.ViewModels;

public sealed partial class MainViewModel(FormatCatalog catalog, ConversionRunner runner, MediaEditor media, AppSettings settings, string settingsPath) : ObservableObject
{
    /// <summary>A chosen region or moment fits one file, and the media tools name their result by its format.</summary>
    private static readonly HashSet<string> OneFileTools =
    [
        ImageCropper.ToolId,
        ImageCensor.ToolId,
        MediaEditor.TrimId,
        MediaEditor.CropVideoId,
        MediaEditor.MuteVideoId,
        MediaEditor.SnapshotId,
    ];

    private const double VideoCropFrameShare = 0.1;

    private CancellationTokenSource? _jobCancellation;
    private bool _outputChosenByUser;
    private string? _firstOutput;

    public MediaEditor Media => media;

    public bool HideToTrayOnClose
    {
        get => settings.HideToTrayOnClose;
        set
        {
            if (settings.HideToTrayOnClose != value)
            {
                settings.HideToTrayOnClose = value;
                settings.Save(settingsPath);
                OnPropertyChanged();
            }
        }
    }

    public bool DragWheelEnabled
    {
        get => settings.DragWheelEnabled;
        set
        {
            if (settings.DragWheelEnabled != value)
            {
                settings.DragWheelEnabled = value;
                settings.Save(settingsPath);
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<FileEntry> Files { get; } = [];

    public ObservableCollection<TargetOption> Targets { get; } = [];

    public ObservableCollection<ToolOption> Tools { get; } = [];

    public bool IsIdle => !IsBusy;

    public bool HasFiles => Files.Count > 0;

    public bool IsEmpty => Files.Count == 0;

    public bool HasTools => Tools.Count > 0;

    public string FileCount => Strings.FileCount(Files.Count);

    public string ConvertLabel => SelectedTarget is { } target ? Strings.ConvertTo(target.Label) : Strings.Convert;

    /// <summary>Asks the user where to crop. The window sets this, because a dialog needs an owner.</summary>
    public Func<ImagePreview, ImageRegion?>? AskCropRegion { get; set; }

    /// <summary>Asks the user what to censor and how. Null means they cancelled.</summary>
    public Func<ImagePreview, (IReadOnlyList<ImageRegion> Regions, CensorStyle Style, double MosaicTilePercent)?>? AskCensor { get; set; }

    /// <summary>Asks which part of this file to keep. Null means they cancelled.</summary>
    public Func<string, MediaInfo, (double Start, double End)?>? AskTrim { get; set; }

    /// <summary>Asks which moment of this video to save. Null means they cancelled.</summary>
    public Func<string, MediaInfo, double?>? AskSnapshot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConvertLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial TargetOption? SelectedTarget { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunToolCommand))]
    public partial string? OutputDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunToolCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelJobCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>True until a converter reports real progress, so the bar never sits still at zero.</summary>
    [ObservableProperty]
    public partial bool IsProgressUnknown { get; set; }

    [ObservableProperty]
    public partial BannerKind Banner { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The tools that work on the given files, with the words and icons the app shows for them.</summary>
    public IReadOnlyList<ToolOption> ToolsFor(IReadOnlyCollection<string> paths) =>
        catalog.CommonTools(paths)
            .Where(tool => paths.Count == 1 || !OneFileTools.Contains(tool))
            .Select(ToToolOption)
            .ToArray();

    /// <summary>The wheel for these files: their formats, and a slice that opens their tools.</summary>
    public IReadOnlyList<WheelChoice> WheelChoicesFor(IReadOnlyCollection<string> paths)
    {
        List<WheelChoice> choices = [.. catalog.CommonTargets(paths).Select(WheelChoice.ForFormat)];

        var tools = ToolsFor(paths);
        if (tools.Count > 0)
        {
            choices.Add(WheelChoice.Menu($"{Strings.ToolsTitle} ›", tools.Select(WheelChoice.ForTool).ToArray()));
        }

        return choices;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var rejected = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path) || !catalog.IsSupported(path))
            {
                rejected++;
            }
            else if (!Files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                var file = new FileEntry(path, catalog.FormatOf(path), catalog.GroupOf(path));
                Files.Add(file);
                _ = file.LoadThumbnailAsync();
            }
        }

        if (rejected > 0)
        {
            Show(BannerKind.Info, Strings.StatusRejected(rejected));
        }
        else
        {
            Show(BannerKind.None, "");
        }

        Refresh();
    }

    public void RemoveFiles(IEnumerable<FileEntry> files)
    {
        foreach (var file in files.ToArray())
        {
            Files.Remove(file);
        }

        Show(BannerKind.None, "");
        Refresh();
    }

    public void ChooseOutputDirectory(string path)
    {
        OutputDirectory = path;
        _outputChosenByUser = true;
    }

    /// <summary>Starts a job from the wheel: these files, this choice, saved beside the originals.</summary>
    public async Task RunFromWheelAsync(IReadOnlyList<string> paths, WheelChoice choice)
    {
        if (IsBusy)
        {
            Show(BannerKind.Info, Strings.StatusBusy);
            return;
        }

        Files.Clear();
        _outputChosenByUser = false;
        AddFiles(paths);
        await RunChoiceAsync(choice);
    }

    /// <summary>Starts a job for the files already in the list.</summary>
    public async Task RunChoiceAsync(WheelChoice choice)
    {
        if (choice.IsTool)
        {
            if (Tools.FirstOrDefault(tool => tool.Id == choice.Id) is { } tool && RunToolCommand.CanExecute(tool))
            {
                await RunToolCommand.ExecuteAsync(tool);
            }

            return;
        }

        SelectedTarget = Targets.FirstOrDefault(target => target.Format == choice.Id);
        if (ConvertCommand.CanExecute(null))
        {
            await ConvertCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void RemoveFile(FileEntry file) => RemoveFiles([file]);

    [RelayCommand]
    private void ClearFiles() => RemoveFiles(Files);

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (_firstOutput is not null)
        {
            Process.Start("explorer.exe", $"/select,\"{_firstOutput}\"");
        }
    }

    private bool CanConvert() =>
        !IsBusy && Files.Count > 0 && SelectedTarget is not null && OutputDirectory is not null;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private Task ConvertAsync()
    {
        var format = SelectedTarget!.Format;
        return RunJobAsync((paths, folder, progress, token) => runner.RunAsync(paths, format, folder, progress, token));
    }

    private bool CanRunTool(ToolOption? tool) =>
        !IsBusy && Files.Count > 0 && tool is not null && OutputDirectory is not null;

    [RelayCommand(CanExecute = nameof(CanRunTool))]
    private Task RunToolAsync(ToolOption? tool) => tool!.Id switch
    {
        ImageCropper.ToolId => EditAsync(Files[0], AskForCrop),
        ImageCensor.ToolId => EditAsync(Files[0], AskForCensor),
        MediaEditor.TrimId => EditMediaAsync(Files[0], AskForTrim),
        MediaEditor.CropVideoId => EditMediaAsync(Files[0], AskForVideoCropAsync),
        MediaEditor.MuteVideoId => EditMediaAsync(Files[0], (format, info, _) => Task.FromResult<IFileTool?>(media.Mute(format, RequireVideo(info).DurationSeconds))),
        MediaEditor.SnapshotId => EditMediaAsync(Files[0], AskForSnapshot),
        _ => RunJobAsync((paths, folder, progress, token) => runner.RunToolAsync(paths, tool.Id, folder, progress, token)),
    };

    private IFileTool? AskForCrop(ImagePreview preview) =>
        AskCropRegion?.Invoke(preview) is { } region ? new ImageCropper(region, preview.Format) : null;

    private IFileTool? AskForCensor(ImagePreview preview) =>
        AskCensor?.Invoke(preview) is { Regions.Count: > 0 } choice
            ? new ImageCensor(choice.Regions, preview.Format, choice.Style) { MosaicTilePercent = choice.MosaicTilePercent }
            : null;

    private Task<IFileTool?> AskForTrim(string format, MediaInfo info, string path) =>
        Task.FromResult(AskTrim?.Invoke(path, info) is { } range ? media.Trim(format, range.Start, range.End) : null);

    private Task<IFileTool?> AskForSnapshot(string format, MediaInfo info, string path) =>
        Task.FromResult(AskSnapshot?.Invoke(path, RequireVideo(info)) is { } seconds ? media.Snapshot(seconds) : null);

    /// <summary>The same crop window as for pictures, on a frame from early on, because videos often open on black.</summary>
    private async Task<IFileTool?> AskForVideoCropAsync(string format, MediaInfo info, string path)
    {
        var frame = await media.ReadFrameAsync(path, RequireVideo(info).DurationSeconds * VideoCropFrameShare);
        var preview = new ImagePreview(path, format, frame, info.Width, info.Height);
        return AskCropRegion?.Invoke(preview) is { } region ? media.CropVideo(format, region, info.DurationSeconds) : null;
    }

    private static MediaInfo RequireVideo(MediaInfo info) =>
        info.HasVideo ? info : throw new InvalidDataException("The file has no video.");

    /// <summary>Reads the file for the user to work on, then runs the tool they set up, if they didn't cancel.</summary>
    private async Task EditMediaAsync(FileEntry file, Func<string, MediaInfo, string, Task<IFileTool?>> askUser)
    {
        IFileTool? tool;

        try
        {
            tool = await askUser(catalog.FormatOf(file.Path), await media.ReadInfoAsync(file.Path), file.Path);
        }
        catch (Exception exception) when (exception is ToolFailedException or InvalidDataException or JsonException or Win32Exception)
        {
            Trace.TraceWarning("Could not read {0}: {1}", file.Path, exception);
            Show(BannerKind.Error, Strings.StatusFileFailed(file.Name));
            return;
        }

        if (tool is not null)
        {
            await RunJobAsync((paths, folder, progress, token) => runner.RunToolAsync(paths, tool, folder, progress, token));
        }
    }

    /// <summary>Shows the image for the user to work on, then runs the tool they set up, if they didn't cancel.</summary>
    private async Task EditAsync(FileEntry file, Func<ImagePreview, IFileTool?> askUser)
    {
        var format = catalog.FormatOf(file.Path);
        ImagePreview preview;

        try
        {
            preview = await Task.Run(() => ImagePreview.Read(file.Path, format));
        }
        catch (Exception exception)
        {
            Trace.TraceWarning("No preview for {0}: {1}", file.Path, exception);
            Show(BannerKind.Error, Strings.StatusFileFailed(file.Name));
            return;
        }

        if (askUser(preview) is { } tool)
        {
            await RunJobAsync((paths, folder, progress, token) => runner.RunToolAsync(paths, tool, folder, progress, token));
        }
    }

    private bool CanCancelJob() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelJob))]
    private void CancelJob() => _jobCancellation?.Cancel();

    private async Task RunJobAsync(Func<IReadOnlyList<string>, string, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> job)
    {
        var outputDirectory = OutputDirectory!;
        using var cancellation = new CancellationTokenSource();
        _jobCancellation = cancellation;

        IsBusy = true;
        Progress = 0;
        IsProgressUnknown = true;
        Show(BannerKind.None, "");

        var progress = new Progress<double>(value =>
        {
            Progress = value;
            IsProgressUnknown = false;
        });

        try
        {
            var outputs = await job(Files.Select(file => file.Path).ToArray(), outputDirectory, progress, cancellation.Token);

            _firstOutput = outputs[0];
            Show(BannerKind.Success, Strings.StatusDone(outputs.Count));
        }
        catch (OperationCanceledException)
        {
            Show(BannerKind.Info, Strings.StatusCancelled);
        }
        catch (ConversionException exception)
        {
            Trace.TraceWarning("{0}: {1}", exception.Message, exception.InnerException);

            // Windows N has no Media Foundation, which the H.264 and AAC encoders come from.
            var mediaFeatureMissing = exception.InnerException is ToolFailedException
                && !File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll"));
            Show(BannerKind.Error, mediaFeatureMissing ? Strings.StatusMediaFeatureMissing : Strings.StatusFileFailed(Path.GetFileName(exception.FilePath)));
        }
        catch (NotSupportedException exception)
        {
            Trace.TraceError("No converter: {0}", exception.Message);
            Show(BannerKind.Error, Strings.StatusNoCommonTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Saving failed: {0}", exception);
            Show(BannerKind.Error, Strings.StatusSaveFailed(outputDirectory));
        }
        finally
        {
            _jobCancellation = null;
            IsBusy = false;
        }
    }

    private void Show(BannerKind banner, string message)
    {
        Banner = banner;
        Status = message;
    }

    private void Refresh()
    {
        var paths = Files.Select(file => file.Path).ToArray();
        var selected = SelectedTarget?.Format;

        Targets.Clear();
        foreach (var target in catalog.CommonTargets(paths))
        {
            Targets.Add(new TargetOption(target));
        }

        SelectedTarget = Targets.FirstOrDefault(target => target.Format == selected) ?? Targets.FirstOrDefault();

        Tools.Clear();
        foreach (var tool in ToolsFor(paths))
        {
            Tools.Add(tool);
        }

        if (Files.Count > 0 && Targets.Count == 0)
        {
            Show(BannerKind.Info, Strings.StatusNoCommonTarget);
        }

        if (!_outputChosenByUser)
        {
            OutputDirectory = Files.Count > 0 ? Path.GetDirectoryName(Files[0].Path) : null;
        }

        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(FileCount));
        ConvertCommand.NotifyCanExecuteChanged();
        RunToolCommand.NotifyCanExecuteChanged();
    }

    private static ToolOption ToToolOption(string id) => id switch
    {
        ImageCropper.ToolId => new ToolOption(id, Strings.ToolCrop, ""),
        ImageCensor.ToolId => new ToolOption(id, Strings.ToolCensor, ""),
        BackgroundRemover.ToolId => new ToolOption(id, Strings.ToolRemoveBackground, ""),
        MediaEditor.TrimId => new ToolOption(id, Strings.ToolTrim, ""),
        MediaEditor.CropVideoId => new ToolOption(id, Strings.ToolCropVideo, ""),
        MediaEditor.MuteVideoId => new ToolOption(id, Strings.ToolMuteVideo, ""),
        MediaEditor.SnapshotId => new ToolOption(id, Strings.ToolSnapshot, ""),
        _ => new ToolOption(id, id, ""),
    };
}
