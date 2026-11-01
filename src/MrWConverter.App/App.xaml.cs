using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.App.Wheel;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Settings;
using MrWConverter.Core.Tools;

namespace MrWConverter.App;

public partial class App : Application
{
    private WheelController? _wheel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var catalog = FormatCatalog.Default;
        var ffmpegFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "ben2-base.onnx");
        var runner = new ConversionRunner(
            catalog,
            [new ImageConverter(), new FfmpegConverter(ffmpegFolder)],
            [new BackgroundRemover(modelPath)]);
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrWConverter", "settings.json");
        var viewModel = new MainViewModel(catalog, runner, AppSettings.Load(settingsPath), settingsPath);

        var window = new MainWindow(viewModel, catalog);
        SessionEnding += (_, _) => window.ExitWithoutAsking();
        window.Show();
        _wheel = new WheelController(viewModel, window.BringToFront);

        viewModel.AddFiles(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wheel?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Store policy 10.4.2: the app handles exceptions and stays responsive.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.TraceError("Unhandled UI exception: {0}", e.Exception);
        MessageBox.Show(Strings.UnexpectedError, Strings.UnexpectedErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Trace.TraceError("Unobserved task exception: {0}", e.Exception);
        e.SetObserved();
    }
}
