using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Windows.ApplicationModel;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.App.Editor;
using MrWConverter.App.Wheel;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.App;

/// <summary>Holds only what needs the window: drag and drop, system dialogs, keyboard and layout.</summary>
public partial class MainWindow : Window
{
    private const double StackedLayoutWidth = 780;
    private const double WideMinHeight = 440;
    private const double StackedMinHeight = 760;

    private readonly MainViewModel _viewModel;
    private readonly FormatCatalog _catalog;
    private bool _closeAfterJob;
    private bool _exitConfirmed;
    private bool _askOnClose;
    private bool _showingStartupState;
    private StartupTask? _startupTask;

    public MainWindow(MainViewModel viewModel, FormatCatalog catalog)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _catalog = catalog;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.AskCropRegion = AskCropRegion;
        viewModel.AskCensor = AskCensor;
        viewModel.AskEdit = AskEdit;
        viewModel.AskSnapshot = AskSnapshot;
        Loaded += async (_, _) => await ShowStartupStateAsync();
    }

    private ImageRegion? AskCropRegion(ImagePreview preview)
    {
        var window = new CropWindow(preview) { Owner = this };
        return window.ShowDialog() == true ? window.Region : null;
    }

    private (IReadOnlyList<ImageRegion> Regions, CensorStyle Style, double MosaicTilePercent)? AskCensor(ImagePreview preview)
    {
        var window = new CensorWindow(preview) { Owner = this };
        return window.ShowDialog() == true ? (window.Regions, window.ChosenStyle, window.MosaicTilePercent) : null;
    }

    private (MediaEdit Edit, bool SeparateFiles)? AskEdit(string path, MediaInfo info)
    {
        var window = new EditWindow(_viewModel.Media, path, info) { Owner = this };
        return window.ShowDialog() == true ? (window.Edit, window.SeparateFiles) : null;
    }

    private double? AskSnapshot(string path, MediaInfo info)
    {
        var window = new SnapshotWindow(_viewModel.Media, path, info) { Owner = this };
        return window.ShowDialog() == true ? window.Seconds : null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status))
        {
            AnnounceStatus();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsBusy) && _closeAfterJob && !_viewModel.IsBusy)
        {
            // Queued, because the job's own cleanup is still finishing on this thread.
            Dispatcher.BeginInvoke(Close);
        }
    }

    /// <summary>Windows is shutting down, so nobody is there to answer a question.</summary>
    public void ExitWithoutAsking() => _exitConfirmed = true;

    /// <summary>Closing mid-job cancels it first, so no half-made files stay in the user's folder.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitConfirmed)
        {
            // Closing a minimized window from the taskbar can only mean quit, and a
            // dialog owned by a minimized window wouldn't be visible to answer anyway.
            var choice = _viewModel.HideToTrayOnClose && !_askOnClose ? CloseChoice.Hide
                : WindowState == WindowState.Minimized ? CloseChoice.Exit
                : AskHowToClose();

            if (choice != CloseChoice.Exit)
            {
                e.Cancel = true;

                if (choice == CloseChoice.Hide)
                {
                    Hide();
                }

                return;
            }

            _exitConfirmed = true;
        }

        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            _closeAfterJob = true;
            _viewModel.CancelJobCommand.Execute(null);
        }
    }

    private CloseChoice AskHowToClose()
    {
        var window = new CloseAppWindow(_viewModel.IsBusy) { Owner = this };
        window.ShowDialog();

        if (window.Choice == CloseChoice.Hide && window.DontAskAgain)
        {
            _viewModel.HideToTrayOnClose = true;
        }

        return window.Choice;
    }

    /// <summary>The tray's Exit always quits, except that a running job or an open dialog gets looked at first.</summary>
    public void ExitFromTray()
    {
        if (OwnedWindows.Count > 0)
        {
            BringToFront();
        }
        else if (_viewModel.IsBusy)
        {
            BringToFront();

            // Asks even if the X button normally hides, because quitting would cancel the job.
            _askOnClose = true;
            Close();
            _askOnClose = false;
        }
        else
        {
            _exitConfirmed = true;
            Close();
        }
    }

    /// <summary>A later copy of the app was started, perhaps with files.</summary>
    public void ReceiveFiles(IReadOnlyList<string> paths)
    {
        BringToFront();

        if (_viewModel.IsIdle && OwnedWindows.Count == 0)
        {
            _viewModel.AddFiles(paths);
        }
    }

    private async Task ShowStartupStateAsync()
    {
        _startupTask = await WindowsStartup.FindTaskAsync();

        if (_startupTask is not null)
        {
            StartWithWindowsToggle.Visibility = Visibility.Visible;
            ShowStartupState();
        }
    }

    private void ShowStartupState()
    {
        var state = _startupTask!.State;

        _showingStartupState = true;
        StartWithWindowsToggle.IsChecked = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartWithWindowsToggle.IsEnabled = state is not (StartupTaskState.EnabledByPolicy or StartupTaskState.DisabledByPolicy);
        _showingStartupState = false;
    }

    /// <summary>Checked and Unchecked rather than Click, because a screen reader's toggle raises no Click.</summary>
    private async void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_showingStartupState)
        {
            return;
        }

        if (StartWithWindowsToggle.IsChecked == true)
        {
            var state = await _startupTask!.RequestEnableAsync();

            // Windows doesn't let an app overrule the switch the user turned off in Settings.
            if (state == StartupTaskState.DisabledByUser
                && MessageBox.Show(this, Strings.StartWithWindowsBlocked, Strings.StartWithWindows.Replace("_", ""), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
        }
        else
        {
            _startupTask!.Disable();
        }

        ShowStartupState();
    }

    /// <summary>A live region only reaches screen readers when its change is raised explicitly.</summary>
    private void AnnounceStatus()
    {
        var peer = UIElementAutomationPeer.FromElement(StatusText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = ActualWidth < StackedLayoutWidth;

        Grid.SetColumn(OptionsPane, stacked ? 0 : 1);
        Grid.SetRow(OptionsPane, stacked ? 1 : 0);
        OptionsColumn.Width = stacked ? new GridLength(0) : new GridLength(360);
        FilesPane.Margin = stacked ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 20, 0);
        Page.MinHeight = stacked && _viewModel.HasFiles ? StackedMinHeight : WideMinHeight;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var canDrop = _viewModel.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = canDrop ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (_viewModel.IsIdle && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddFiles(paths);
        }

        e.Handled = true;
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var extensions = string.Join(';', _catalog.InputExtensions.Select(extension => $"*.{extension}"));
        var dialog = new OpenFileDialog
        {
            Title = Strings.ChooseFilesDialogTitle,
            Multiselect = true,
            Filter = $"{Strings.SupportedFilesFilter}|{extensions}|{Strings.AllFilesFilter}|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddFiles(dialog.FileNames);
        }
    }

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.IsIdle && Clipboard.ContainsFileDropList();
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.AddFiles(Clipboard.GetFileDropList().Cast<string>());
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.ChooseFolderDialogTitle,
            InitialDirectory = _viewModel.OutputDirectory ?? "",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ChooseOutputDirectory(dialog.FolderName);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    public void BringToFront()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // An open dialog blocks this window, so it is the one to bring up.
        (OwnedWindows.Count > 0 ? OwnedWindows[OwnedWindows.Count - 1] : this).Activate();
    }

    private void OpenWheel_Click(object sender, RoutedEventArgs e) => OpenWheel();

    private void OpenWheel_Executed(object sender, ExecutedRoutedEventArgs e) => OpenWheel();

    /// <summary>The same wheel for files already in the list, with the mouse or the keyboard.</summary>
    private void OpenWheel()
    {
        var choices = _viewModel.WheelChoicesFor(_viewModel.Files.Select(file => file.Path).ToArray());
        if (!_viewModel.HasFiles || !_viewModel.IsIdle || choices.Count == 0)
        {
            return;
        }

        var middle = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        var wheel = new WheelWindow(choices, choice => _ = _viewModel.RunChoiceAsync(choice)) { Owner = this };
        wheel.ShowAt(new NativeMethods.Point { X = (int)middle.X, Y = (int)middle.Y });
        wheel.Activate();
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            _viewModel.RemoveFiles(FileList.SelectedItems.Cast<FileEntry>());
            e.Handled = true;
        }
    }
}
