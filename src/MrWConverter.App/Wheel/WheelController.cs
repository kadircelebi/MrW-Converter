using System.ComponentModel;
using MrWConverter.App.ViewModels;

namespace MrWConverter.App.Wheel;

/// <summary>Opens the wheel for drags from other apps, for as long as the user keeps the setting on.</summary>
public sealed class WheelController : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _showMainWindow;
    private readonly DragWatcher _watcher = new();
    private WheelWindow? _wheel;
    private bool _openedThisDrag;

    public WheelController(MainViewModel viewModel, Action showMainWindow)
    {
        _viewModel = viewModel;
        _showMainWindow = showMainWindow;

        _watcher.WheelKeyPressed += OpenWheel;
        _watcher.DragEnded += CloseWheel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Apply();
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _watcher.Dispose();
        _wheel?.CloseOnce();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DragWheelEnabled))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_viewModel.DragWheelEnabled)
        {
            _watcher.Start();
        }
        else
        {
            _watcher.Stop();
        }
    }

    private void OpenWheel()
    {
        // Once per drag, so a wheel that closed itself because the drag holds text stays closed.
        if (_openedThisDrag)
        {
            return;
        }

        _openedThisDrag = true;
        _wheel?.CloseOnce();

        NativeMethods.GetCursorPos(out var cursor);
        var wheel = new WheelWindow(paths => _viewModel.WheelChoicesFor(paths.ToArray()), Dropped);
        wheel.Closed += (_, _) =>
        {
            // A new drag may already have its own wheel while this one finishes closing.
            if (_wheel == wheel)
            {
                _wheel = null;
            }
        };
        _wheel = wheel;
        wheel.ShowAt(cursor);
    }

    private void CloseWheel()
    {
        // Windows releases the mouse first and delivers the drop afterwards, so a wheel
        // closed right now would never receive it. It closes itself shortly after instead.
        _wheel?.CloseAfterDropWindow();
        _openedThisDrag = false;
    }

    private void Dropped(IReadOnlyList<string> files, WheelChoice choice)
    {
        _showMainWindow();
        _ = _viewModel.RunFromWheelAsync(files, choice);
    }
}
