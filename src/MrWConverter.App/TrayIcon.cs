using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using MrWConverter.App.Wheel;

namespace MrWConverter.App;

/// <summary>
/// The icon next to the clock, so the wheel can keep working with no window open.
/// WPF has none of its own, and Windows Forms would be a whole framework for this one icon.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string Tip = "MrWConverter";

    // Windows' own default name for the icon of a program built from an .ico file.
    private const int ApplicationIconResource = 32512;

    private readonly HwndSource _window;
    private readonly ContextMenu _menu;
    private readonly Action _open;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;

    public TrayIcon(ContextMenu menu, Action open)
    {
        _menu = menu;
        _open = open;

        // Hidden rather than message-only, because message-only windows miss TaskbarCreated.
        _window = new HwndSource(new HwndSourceParameters("MrWConverterTray") { WindowStyle = 0, Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        Add();
    }

    public void Dispose()
    {
        Remove();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private NativeMethods.NotifyIconData NewData(uint flags) => new()
    {
        Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = _window.Handle,
        Id = 1,
        Flags = flags,
        CallbackMessage = NativeMethods.TrayCallbackMessage,
        Icon = _icon,
        Tip = Tip,
    };

    private void Add()
    {
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmallIconWidth, NativeMethods.GetDpiForWindow(_window.Handle));
        _icon = NativeMethods.LoadImage(NativeMethods.GetModuleHandle(null), ApplicationIconResource, NativeMethods.IconImage, size, size, 0);

        var data = NewData(NativeMethods.TrayIconFlags);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.TrayAdd, ref data))
        {
            // Normal while Explorer is restarting; TaskbarCreated brings the icon back.
            Trace.TraceWarning("The tray icon could not be added: {0}", new Win32Exception().Message);
            return;
        }

        // Version 4 is what reports the keyboard as well as the mouse.
        data.Version = NativeMethods.TrayVersion4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.TraySetVersion, ref data);
    }

    private void Remove()
    {
        var data = NewData(0);
        NativeMethods.Shell_NotifyIcon(NativeMethods.TrayDelete, ref data);

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreated)
        {
            // Explorer restarted or the scale changed: the old icon is gone or the wrong size.
            Remove();
            Add();
        }
        else if (message == NativeMethods.TrayCallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NativeMethods.TraySelect:
                case NativeMethods.TrayKeySelect:
                    _open();
                    break;

                case NativeMethods.ContextMenuMessage:
                    var position = wParam.ToInt64();
                    ShowMenu((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));
                    break;
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, 2);
        NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY);

        // Without this, the menu wouldn't close when the user clicks somewhere else.
        NativeMethods.SetForegroundWindow(_window.Handle);

        _menu.Placement = PlacementMode.AbsolutePoint;
        _menu.HorizontalOffset = x * 96d / dpiX;
        _menu.VerticalOffset = y * 96d / dpiY;
        _menu.IsOpen = true;
    }
}
