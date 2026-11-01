using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using MrWConverter.App.Resources;
using MrWConverter.Core.Media;

namespace MrWConverter.App.Editor;

/// <summary>
/// The file as a strip of frames or a sound wave, with its clips and the playhead on top.
/// Clicking seeks, and the lines between clips drag. To assistive tools it is a slider for
/// the playhead; the window's buttons and list offer everything the dragging does.
/// </summary>
public sealed class EditTimeline : FrameworkElement
{
    private const double SideMargin = 10;
    private const double VerticalMargin = 6;
    private const double HandleReach = 8;
    private const double HandleWidth = 6;
    private const double CornerRadius = 6;

    private static readonly Brush RemovedShade = Frozen(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)));
    private static readonly Pen RemovedMark = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1));
    private static readonly Pen PlayheadLine = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen PlayheadEdge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 4));
    private static readonly Pen ClipLine = Frozen(new Pen(Brushes.White, 1.5));

    private MediaEdit _edit = new(1);
    private double _position;
    private ImageSource?[] _strip = [];
    private DragMode _drag;
    private int _dragBoundary;
    private MediaEdit? _dragStartEdit;
    private bool _dragChanged;

    public EditTimeline()
    {
        Focusable = true;
        FocusVisualStyle = null;
    }

    private enum DragMode
    {
        None,
        Seek,
        TrimStart,
        TrimEnd,
        Boundary,
    }

    public event Action<double>? Seeked;

    /// <summary>A line is about to move, so the edit as it stands is the one to undo back to.</summary>
    public event Action? DragStarted;

    public event Action<MediaEdit>? EditDragged;

    public MediaEdit Edit
    {
        get => _edit;
        set
        {
            _edit = value;
            InvalidateVisual();
        }
    }

    public double Position
    {
        get => _position;
        set
        {
            var old = _position;
            _position = value;
            InvalidateVisual();

            if (UIElementAutomationPeer.FromElement(this) is { } peer)
            {
                peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old, value);
            }
        }
    }

    /// <summary>Pictures shown side by side across the whole length. A picture not loaded yet can be null.</summary>
    public void SetStrip(ImageSource?[] pictures)
    {
        _strip = pictures;
        InvalidateVisual();
    }

    private Rect Track => new(SideMargin, VerticalMargin, Math.Max(1, ActualWidth - SideMargin * 2), Math.Max(1, ActualHeight - VerticalMargin * 2));

    protected override AutomationPeer OnCreateAutomationPeer() => new EditTimelinePeer(this);

    protected override void OnRender(DrawingContext context)
    {
        var track = Track;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        context.PushClip(new RectangleGeometry(track, CornerRadius, CornerRadius));
        context.DrawRectangle(ThemeBrush("ControlFillColorSecondaryBrush", Brushes.DimGray), null, track);
        for (var index = 0; index < _strip.Length; index++)
        {
            if (_strip[index] is { } picture)
            {
                var width = track.Width / _strip.Length;
                var cell = new Rect(track.X + width * index, track.Y, width + 0.5, track.Height);
                DrawPicture(context, picture, cell);
            }
        }

        foreach (var clip in _edit.Clips.Where(clip => !clip.Kept))
        {
            var (left, right) = (ToX(clip.Range.Start), ToX(clip.Range.End));
            context.DrawRectangle(RemovedShade, null, new Rect(left, track.Y, right - left, track.Height));
            context.DrawLine(RemovedMark, new Point(left, track.Bottom), new Point(right, track.Y));
        }

        context.Pop();

        var accent = ThemeBrush("AccentFillColorDefaultBrush", Brushes.DeepSkyBlue);
        var current = _edit.Clips[_edit.ClipIndexAt(_position)].Range;
        context.DrawRoundedRectangle(null, new Pen(accent, IsKeyboardFocused ? 3 : 2), new Rect(ToX(current.Start), track.Y, ToX(current.End) - ToX(current.Start), track.Height), CornerRadius, CornerRadius);

        for (var index = 0; index < _edit.Clips.Count - 1; index++)
        {
            var x = ToX(_edit.Clips[index].Range.End);
            context.DrawLine(ClipLine, new Point(x, track.Y), new Point(x, track.Bottom));
        }

        DrawHandle(context, accent, ToX(_edit.Clips[_edit.FirstKeptIndex].Range.Start), track);
        DrawHandle(context, accent, ToX(_edit.Clips[_edit.LastKeptIndex].Range.End), track);

        var playhead = ToX(_position);
        context.DrawLine(PlayheadEdge, new Point(playhead, 0), new Point(playhead, ActualHeight));
        context.DrawLine(PlayheadLine, new Point(playhead, 0), new Point(playhead, ActualHeight));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var x = e.GetPosition(this).X;
        (_drag, _dragBoundary) = HandleAt(x) ?? (DragMode.Seek, 0);
        _dragStartEdit = _edit;
        _dragChanged = false;
        CaptureMouse();

        if (_drag == DragMode.Seek)
        {
            Seeked?.Invoke(ToTime(x));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var x = e.GetPosition(this).X;
        var time = ToTime(x);

        switch (_drag)
        {
            case DragMode.None:
                Cursor = HandleAt(x) is null ? Cursors.Arrow : Cursors.SizeWE;
                break;

            case DragMode.Seek:
                Seeked?.Invoke(time);
                break;

            case DragMode.TrimStart:
                MoveLine(_dragStartEdit!.TrimStart(time), time);
                break;

            case DragMode.TrimEnd:
                MoveLine(_dragStartEdit!.TrimEnd(time), time);
                break;

            case DragMode.Boundary:
                MoveLine(_dragStartEdit!.MoveBoundary(_dragBoundary, time), time);
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag = DragMode.None;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag = DragMode.None;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    internal void SeekFromAutomation(double seconds) => Seeked?.Invoke(seconds);

    private void MoveLine(MediaEdit edit, double time)
    {
        if (!_dragChanged)
        {
            _dragChanged = true;
            DragStarted?.Invoke();
        }

        EditDragged?.Invoke(edit);

        // Shows the frame being cut at, which is what the user is looking for while dragging.
        Seeked?.Invoke(Math.Clamp(time, 0, _edit.Duration));
    }

    /// <summary>The line near this point: the two outer ones trim, the ones between clips move.</summary>
    private (DragMode Mode, int Boundary)? HandleAt(double x)
    {
        var (first, last) = (_edit.FirstKeptIndex, _edit.LastKeptIndex);
        (DragMode Mode, int Boundary)? best = null;
        var bestDistance = HandleReach;

        void Consider(double time, DragMode mode, int boundary)
        {
            var distance = Math.Abs(ToX(time) - x);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = (mode, boundary);
            }
        }

        for (var index = first; index < last; index++)
        {
            Consider(_edit.Clips[index].Range.End, DragMode.Boundary, index);
        }

        Consider(_edit.Clips[first].Range.Start, DragMode.TrimStart, 0);
        Consider(_edit.Clips[last].Range.End, DragMode.TrimEnd, 0);
        return best;
    }

    /// <summary>A sound wave stretches to its full length. A frame fills its cell without being squashed.</summary>
    private void DrawPicture(DrawingContext context, ImageSource picture, Rect cell)
    {
        if (_strip.Length == 1)
        {
            context.DrawImage(picture, cell);
            return;
        }

        var scale = Math.Max(cell.Width / picture.Width, cell.Height / picture.Height);
        var (width, height) = (picture.Width * scale, picture.Height * scale);

        context.PushClip(new RectangleGeometry(cell));
        context.DrawImage(picture, new Rect(cell.X + (cell.Width - width) / 2, cell.Y + (cell.Height - height) / 2, width, height));
        context.Pop();
    }

    private void DrawHandle(DrawingContext context, Brush brush, double x, Rect track)
    {
        context.DrawRoundedRectangle(brush, null, new Rect(x - HandleWidth / 2, track.Y, HandleWidth, track.Height), 2, 2);
        context.DrawLine(ClipLine, new Point(x, track.Y + track.Height * 0.3), new Point(x, track.Bottom - track.Height * 0.3));
    }

    private Brush ThemeBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private double ToX(double seconds) => Track.X + Track.Width * seconds / _edit.Duration;

    private double ToTime(double x) => Math.Clamp((x - Track.X) / Track.Width, 0, 1) * _edit.Duration;

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private sealed class EditTimelinePeer(EditTimeline owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private const double FineStep = 0.1;
        private const double CoarseStep = 1;

        public bool IsReadOnly => false;

        public double Value => owner.Position;

        public double Minimum => 0;

        public double Maximum => owner.Edit.Duration;

        public double SmallChange => FineStep;

        public double LargeChange => CoarseStep;

        public void SetValue(double value)
        {
            if (!double.IsFinite(value) || value < Minimum || value > Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            owner.SeekFromAutomation(value);
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(EditTimeline);

        protected override string GetNameCore() => Strings.EditTimeline;
    }
}
