using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.Core.Wheel;

namespace MrWConverter.App.Wheel;

/// <summary>
/// The round menu. During a drag it starts invisible, because only the drag's contents
/// can tell whether it belongs: dragged text closes it at once, dragged files reveal it.
/// Its middle is fully transparent, and Windows skips such pixels, so releasing there
/// falls through to whatever is underneath and a normal drag keeps working.
/// </summary>
public partial class WheelWindow : Window
{
    private const double Center = 180;
    private const double InnerRadius = 62;
    private const double OuterRadius = 172;
    private const double GapDegrees = 1;
    private const double LabelSize = 15;
    private const double LabelMaxWidth = 110;

    private static readonly TimeSpan DropWindow = TimeSpan.FromMilliseconds(500);

    // Long enough that crossing the slice on the way to its neighbour doesn't change the wheel.
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(300);

    // Longer, because going back undoes what the user just asked for.
    private static readonly TimeSpan BackDelay = TimeSpan.FromMilliseconds(700);

    // Windows only sends drag events to pixels that aren't fully transparent.
    private static readonly Brush NearlyInvisible = new SolidColorBrush(Color.FromArgb(2, 255, 255, 255));

    private readonly Func<IReadOnlyList<string>, IReadOnlyList<WheelChoice>>? _choicesFor;
    private readonly Action<IReadOnlyList<string>, WheelChoice>? _dropped;
    private readonly Action<WheelChoice>? _picked;
    private readonly List<WheelSector> _sectors = [];
    private readonly List<TextBlock> _labels = [];
    private readonly DispatcherTimer _navigationTimer = new();
    private readonly WheelHover _hover = new();
    private WheelLayout _layout = new(0, InnerRadius, OuterRadius);
    private IReadOnlyList<WheelChoice> _rootChoices = [];
    private Ellipse? _dragCatcher;
    private bool _closing;

    /// <summary>A wheel for files being dragged from another app. It never takes the keyboard focus.</summary>
    public WheelWindow(Func<IReadOnlyList<string>, IReadOnlyList<WheelChoice>> choicesFor, Action<IReadOnlyList<string>, WheelChoice> dropped)
    {
        InitializeComponent();

        _choicesFor = choicesFor;
        _dropped = dropped;
        _navigationTimer.Tick += (_, _) => NavigateFromHover();
        SourceInitialized += (_, _) => AddExtendedStyle(NativeMethods.NoActivate);

        _dragCatcher = new Ellipse { Width = Center * 2, Height = Center * 2, Fill = NearlyInvisible };
        Surface.Children.Add(_dragCatcher);
    }

    /// <summary>A wheel for the app's own files, used with the mouse or the keyboard.</summary>
    public WheelWindow(IReadOnlyList<WheelChoice> choices, Action<WheelChoice> picked)
    {
        InitializeComponent();

        _picked = picked;
        _navigationTimer.Tick += (_, _) => NavigateFromHover();
        ShowActivated = true;
        _rootChoices = choices;
        BuildSectors(choices);
        Loaded += (_, _) => _sectors.FirstOrDefault()?.Focus();
    }

    /// <summary>Shows the wheel centered on a screen point, moved just enough to stay on that monitor.</summary>
    internal void ShowAt(NativeMethods.Point center)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        AddExtendedStyle(NativeMethods.ToolWindow);

        var monitor = NativeMethods.MonitorFromPoint(center, 2);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _);

        var size = (int)(Center * 2 * dpi / 96);
        var x = Math.Clamp(center.X - size / 2, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - size));
        var y = Math.Clamp(center.Y - size / 2, info.WorkArea.Top, Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - size));

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, size, size, NativeMethods.MoveNoZOrderNoActivate);
        Show();
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, size, size, NativeMethods.MoveNoZOrderNoActivate);
    }

    private void BuildSectors(IReadOnlyList<WheelChoice> choices)
    {
        Surface.Children.Clear();
        _sectors.Clear();
        _labels.Clear();
        _layout = new WheelLayout(choices.Count, InnerRadius, OuterRadius);

        for (var index = 0; index < choices.Count; index++)
        {
            var sector = new WheelSector(choices[index])
            {
                Shape = SectorShape(index),
                Width = Center * 2,
                Height = Center * 2,
            };
            sector.Click += (_, _) => Pick(sector.Choice);
            sector.GotKeyboardFocus += (_, _) => UpdateLabels();
            sector.LostKeyboardFocus += (_, _) => UpdateLabels();
            var sectorIndex = index;
            sector.MouseEnter += (_, _) =>
            {
                Hover(sectorIndex);
                UpdateLabels();
            };
            sector.MouseLeave += (_, _) =>
            {
                Hover(WheelLayout.None);
                UpdateLabels();
            };
            _sectors.Add(sector);
            Surface.Children.Add(sector);
        }

        // Added after every sector, so no slice can draw over a neighbour's label.
        for (var index = 0; index < choices.Count; index++)
        {
            var label = new TextBlock
            {
                Text = choices[index].Label,
                FontSize = LabelSize,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
                MaxWidth = LabelMaxWidth,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (x, y) = _layout.LabelCenter(index);
            Canvas.SetLeft(label, Center + x - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, Center + y - label.DesiredSize.Height / 2);
            _labels.Add(label);
            Surface.Children.Add(label);
        }

        UpdateLabels();
    }

    private Geometry SectorShape(int sector)
    {
        var start = _layout.StartAngle(sector) + GapDegrees;
        var end = _layout.EndAngle(sector) - GapDegrees;
        Point At(double radius, double angle)
        {
            var (x, y) = WheelLayout.PointAt(radius, angle);
            return new Point(Center + x, Center + y);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(InnerRadius, start), isFilled: true, isClosed: true);
            context.LineTo(At(OuterRadius, start), true, true);

            // A full circle can't be one arc, so a lone sector is drawn as two.
            var middle = (start + end) / 2;
            context.ArcTo(At(OuterRadius, middle), new Size(OuterRadius, OuterRadius), 0, false, SweepDirection.Clockwise, true, true);
            context.ArcTo(At(OuterRadius, end), new Size(OuterRadius, OuterRadius), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(At(InnerRadius, end), true, true);
            context.ArcTo(At(InnerRadius, middle), new Size(InnerRadius, InnerRadius), 0, false, SweepDirection.Counterclockwise, true, true);
            context.ArcTo(At(InnerRadius, start), new Size(InnerRadius, InnerRadius), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private void UpdateLabels()
    {
        for (var index = 0; index < _labels.Count; index++)
        {
            var sector = _sectors[index];
            var active = sector.IsChosen || sector.IsMouseOver || sector.IsKeyboardFocused;
            _labels[index].SetResourceReference(
                TextBlock.ForegroundProperty,
                active ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }
    }

    private void Pick(WheelChoice choice)
    {
        if (choice.Navigates)
        {
            Navigate(choice);
            _sectors.FirstOrDefault()?.Focus();
            return;
        }

        CloseOnce();
        _picked?.Invoke(choice);
    }

    /// <summary>Starts counting when the pointer reaches a slice that changes the wheel.</summary>
    private void Hover(int sector)
    {
        if (!_hover.MoveTo(sector))
        {
            return;
        }

        _navigationTimer.Stop();

        if (_hover.CanAct && _sectors[sector].Choice.Navigates)
        {
            _navigationTimer.Interval = _sectors[sector].Choice.GoesBack ? BackDelay : OpenDelay;
            _navigationTimer.Start();
        }
    }

    private void NavigateFromHover()
    {
        _navigationTimer.Stop();

        if (_hover.Current != WheelLayout.None)
        {
            Navigate(_sectors[_hover.Current].Choice);
        }
    }

    private void Navigate(WheelChoice choice)
    {
        BuildSectors(choice.Items is { } items ? [.. items, WheelChoice.Back($"‹ {Strings.WheelBack}")] : _rootChoices);

        _navigationTimer.Stop();
        NativeMethods.GetCursorPos(out var cursor);
        var position = Surface.PointFromScreen(new Point(cursor.X, cursor.Y));
        _hover.Landed(_layout.SectorAt(position.X - Center, position.Y - Center));

        if (_dropped is not null)
        {
            Highlight(_hover.Current);
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_choicesFor is null)
        {
            return;
        }

        var choices = _choicesFor(Files(e));
        if (choices.Count == 0)
        {
            // Text, a link, a folder or an unsupported file: this drag isn't for the wheel.
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            Visibility = Visibility.Hidden;
            Dispatcher.BeginInvoke(CloseOnce);
            return;
        }

        if (_dragCatcher is not null)
        {
            _dragCatcher = null;
            _rootChoices = choices;
            BuildSectors(choices);
        }

        Track(e);
    }

    private void Window_DragOver(object sender, DragEventArgs e) => Track(e);

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        Hover(WheelLayout.None);
        Highlight(WheelLayout.None);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _navigationTimer.Stop();

        var position = e.GetPosition(Surface);
        var sector = _layout.SectorAt(position.X - Center, position.Y - Center);
        var files = Files(e);
        var choice = sector == WheelLayout.None || _sectors[sector].Choice.Navigates ? null : _sectors[sector].Choice;

        // Always Copy, even though Shift means "move" in Explorer: the source must keep its files.
        e.Effects = choice is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        Visibility = Visibility.Hidden;

        // The app that started the drag waits for this handler, so the real work happens after it returns.
        Dispatcher.BeginInvoke(() =>
        {
            CloseOnce();

            if (choice is not null)
            {
                _dropped?.Invoke(files, choice);
            }
        });
    }

    private void Track(DragEventArgs e)
    {
        var position = e.GetPosition(Surface);
        var sector = _layout.SectorAt(position.X - Center, position.Y - Center);
        Hover(sector);
        Highlight(sector);
        e.Effects = sector == WheelLayout.None || _sectors[sector].Choice.Navigates ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Highlight(int chosen)
    {
        for (var index = 0; index < _sectors.Count; index++)
        {
            _sectors[index].IsChosen = index == chosen;
        }

        UpdateLabels();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Right or Key.Down => 1,
            Key.Left or Key.Up => -1,
            _ => 0,
        };

        if (e.Key == Key.Escape)
        {
            CloseOnce();
            e.Handled = true;
        }
        else if (step != 0 && _sectors.Count > 0)
        {
            var current = _sectors.FindIndex(sector => sector.IsKeyboardFocused);
            _sectors[(current + step + _sectors.Count) % _sectors.Count].Focus();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // The keyboard wheel behaves like a menu: clicking elsewhere dismisses it.
        if (_picked is not null)
        {
            CloseOnce();
        }
    }

    /// <summary>Closes once a drop that is already on its way has had time to arrive.</summary>
    public void CloseAfterDropWindow()
    {
        var timer = new DispatcherTimer { Interval = DropWindow };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CloseOnce();
        };
        timer.Start();
    }

    public void CloseOnce()
    {
        if (!_closing)
        {
            _closing = true;
            _navigationTimer.Stop();
            Close();
        }
    }

    private static string[] Files(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.All(File.Exists) ? paths : [];

    private void AddExtendedStyle(long style)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var current = NativeMethods.GetWindowLongPtr(handle, NativeMethods.ExtendedStyleIndex).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.ExtendedStyleIndex, new IntPtr(current | style));
    }
}
