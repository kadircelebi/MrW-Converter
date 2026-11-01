using System.Text;
using System.Windows.Threading;

namespace MrWConverter.App.Wheel;

/// <summary>
/// Notices drags in other apps without watching the keyboard or mouse. Windows reports
/// when windows are shown, and a window of class "SysDragImage" appears when a file drag
/// starts. Keys and buttons are only read while such a drag is in progress. That class
/// name is not a documented contract, so if Windows changes it the wheel simply stops
/// appearing.
///
/// The image only marks the start. It hides whenever the pointer is over a drop target
/// that doesn't draw it, such as the wheel itself, so hiding says nothing about the end.
/// A drag ends when the system's drag loop releases the mouse, or when no button is down.
/// </summary>
public sealed class DragWatcher : IDisposable
{
    private const string DragImageClass = "SysDragImage";
    private const string DragLoopClass = "CLIPBRDWNDCLASS";

    private const uint CaptureEnd = 0x0009;
    private const uint ObjectShow = 0x8002;
    private const uint OutOfContextSkipOwnProcess = 0x0002;
    private const int WindowObject = 0;

    private readonly DispatcherTimer _keyTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly NativeMethods.WinEventProc _callback;
    private IntPtr _showHook;
    private IntPtr _captureHook;
    private bool _dragging;

    public DragWatcher()
    {
        // Kept in a field: Windows calls this delegate, so it must outlive the hooks.
        _callback = OnWindowEvent;
        _keyTimer.Tick += (_, _) => CheckKeys();
    }

    /// <summary>
    /// Raised on the UI thread, repeatedly, while Shift alone is down during a drag.
    /// Ctrl isn't used: Ctrl-dragging to copy is too common to interrupt with a wheel.
    /// </summary>
    public event Action? WheelKeyPressed;

    public event Action? DragEnded;

    public void Start()
    {
        if (_showHook == IntPtr.Zero)
        {
            _showHook = NativeMethods.SetWinEventHook(ObjectShow, ObjectShow, IntPtr.Zero, _callback, 0, 0, OutOfContextSkipOwnProcess);
            _captureHook = NativeMethods.SetWinEventHook(CaptureEnd, CaptureEnd, IntPtr.Zero, _callback, 0, 0, OutOfContextSkipOwnProcess);
        }
    }

    public void Stop()
    {
        if (_showHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_showHook);
            NativeMethods.UnhookWinEvent(_captureHook);
            _showHook = IntPtr.Zero;
            _captureHook = IntPtr.Zero;
        }

        EndDrag();
    }

    public void Dispose() => Stop();

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != WindowObject)
        {
            return;
        }

        if (eventType == ObjectShow && !_dragging && HasClass(window, DragImageClass))
        {
            _dragging = true;
            _keyTimer.Start();
            CheckKeys();
        }
        else if (eventType == CaptureEnd && _dragging && HasClass(window, DragLoopClass))
        {
            // Covers Esc, which ends the drag while the button is still down.
            EndDrag();
        }
    }

    private void CheckKeys()
    {
        if (!NativeMethods.IsDown(NativeMethods.LeftButton) && !NativeMethods.IsDown(NativeMethods.RightButton))
        {
            EndDrag();
            return;
        }

        if (NativeMethods.IsDown(NativeMethods.Shift) && !NativeMethods.IsDown(NativeMethods.Control) && !NativeMethods.IsDown(NativeMethods.Alt))
        {
            WheelKeyPressed?.Invoke();
        }
    }

    private void EndDrag()
    {
        _keyTimer.Stop();

        if (_dragging)
        {
            _dragging = false;
            DragEnded?.Invoke();
        }
    }

    private static bool HasClass(IntPtr window, string className)
    {
        var name = new StringBuilder(className.Length + 2);
        return NativeMethods.GetClassName(window, name, name.Capacity) > 0 && name.ToString() == className;
    }
}
