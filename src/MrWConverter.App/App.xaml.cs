using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.App.Wheel;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Office;
using MrWConverter.Core.Pdf;
using MrWConverter.Core.Settings;
using MrWConverter.Core.Tools;

namespace MrWConverter.App;

public partial class App : Application
{
    private WheelController? _wheel;
    private TrayIcon? _tray;
    private SingleInstance? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var startedAtSignIn = WindowsStartup.LaunchedAtSignIn;
        var instanceName = $"MrWConverter-{Process.GetCurrentProcess().SessionId}";

        _instance = SingleInstance.TryClaim(instanceName);
        if (_instance is null)
        {
            // Signing in shouldn't pop up a copy the user already has running.
            if (!startedAtSignIn)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.AnyProcess);
                await SingleInstance.SendAsync(instanceName, e.Args);
            }

            Shutdown();
            return;
        }

        var catalog = FormatCatalog.Default;
        var ffmpegFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var modelPath = Path.Combine(AppContext.BaseDirectory, "models", "ben2-base.onnx");
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrWConverter");
        KeepLog(appData);
        var office = new OfficeRuntime(Path.Combine(WindowsStartup.SharedDataFolder(appData), "office"), AppContext.BaseDirectory);
        var runner = new ConversionRunner(
            catalog,
            [new ImageConverter(), new FfmpegConverter(ffmpegFolder), new PdfConverter(), new OfficeConverter(office)],
            [new BackgroundRemover(modelPath)]);
        var settingsPath = Path.Combine(appData, "settings.json");
        var viewModel = new MainViewModel(catalog, runner, new MediaEditor(ffmpegFolder), office, AppSettings.Load(settingsPath), settingsPath);

        var window = new MainWindow(viewModel, catalog);
        SessionEnding += (_, _) => window.ExitWithoutAsking();

        if (!startedAtSignIn)
        {
            window.Show();
        }

        _wheel = new WheelController(viewModel, window.BringToFront);
        _tray = new TrayIcon(TrayMenu(window, viewModel), window.BringToFront);
        _instance.FilesReceived += paths => Dispatcher.BeginInvoke(() => window.ReceiveFiles(paths));

        viewModel.AddFiles(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _wheel?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static ContextMenu TrayMenu(MainWindow window, MainViewModel viewModel)
    {
        var open = new MenuItem { Header = Strings.TrayOpen, FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => window.BringToFront();

        var hideOnClose = new MenuItem { Header = Strings.TrayHideOnClose, IsCheckable = true };
        hideOnClose.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(MainViewModel.HideToTrayOnClose)) { Source = viewModel });

        var exit = new MenuItem { Header = Strings.TrayExit };
        exit.Click += (_, _) => window.ExitFromTray();

        return new ContextMenu { Items = { open, hideOnClose, new Separator(), exit } };
    }

    /// <summary>
    /// Keeps this session's warnings and errors in a file on this device, so a problem can be looked into.
    /// It holds file paths and error messages, is replaced at each start and never leaves the computer.
    /// </summary>
    private static void KeepLog(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Trace.AutoFlush = true;
            Trace.Listeners.Add(new TextWriterTraceListener(new StreamWriter(Path.Combine(folder, "last-session.log"), append: false)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The app works without its log.
        }
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
