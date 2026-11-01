using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MrWConverter.App.Resources;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>
/// Cuts one audio or video file into clips and removes the ones not wanted. Windows plays the
/// file where it can; where it can't, FFmpeg shows frames and everything but playback still works.
/// </summary>
public partial class EditWindow : Window
{
    private const double SecondStep = 1;
    private const double FineStep = 0.1;
    private const int StripPictures = 12;
    private const int StripPictureSide = 160;
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    private static readonly TimeSpan PlaybackTick = TimeSpan.FromMilliseconds(40);

    private readonly MediaInfo _info;
    private readonly DispatcherTimer _playback = new() { Interval = PlaybackTick };
    private readonly CancellationTokenSource _closing = new();
    private readonly FrameLoader? _frames;
    private readonly Stack<MediaEdit> _undo = new();
    private readonly Stack<MediaEdit> _redo = new();
    private MediaEdit _edit;
    private double _position;
    private bool _playerReady;
    private bool _playing;
    private bool _showingState;
    private int _shownClip = -1;

    public EditWindow(MediaEditor media, string path, MediaInfo info)
    {
        InitializeComponent();

        _info = info;
        _edit = new MediaEdit(info.DurationSeconds);

        Timeline.Edit = _edit;
        Timeline.Seeked += Seek;
        Timeline.DragStarted += RememberForUndo;
        Timeline.EditDragged += Apply;
        _playback.Tick += (_, _) => FollowPlayer();

        if (info.HasVideo)
        {
            _frames = new FrameLoader((seconds, token) => media.ReadFrameAsync(path, seconds, token), image => Still.Source = image);
            _frames.Show(0);
        }

        ShowState();
        ShowPosition();

        Loaded += async (_, _) =>
        {
            Timeline.Focus();
            OpenPlayer(path);
            await LoadStripAsync(media, path);
        };
        Closed += (_, _) => CleanUp();
    }

    public MediaEdit Edit => _edit;

    public bool SeparateFiles => SeparateBox.IsChecked == true;

    private double FrameStep => _info.FramesPerSecond > 0 ? 1 / _info.FramesPerSecond : FineStep;

    private void OpenPlayer(string path)
    {
        // Windows' player ignores a file's rotation note, which phones use for upright video.
        if (_info.Rotation != 0)
        {
            Player.LayoutTransform = new RotateTransform(-_info.Rotation);
        }

        try
        {
            Player.ScrubbingEnabled = _info.HasVideo;
            Player.Source = new Uri(path);

            // In manual mode, this is what makes the player open the file. Sound also needs a start.
            if (!_info.HasVideo)
            {
                Player.Play();
            }

            Player.Pause();
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException)
        {
            Trace.TraceWarning("Windows can't open {0}: {1}", path, exception);
            ShowNoPlayback();
        }
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _playerReady = true;
        PlayButton.IsEnabled = true;

        if (_info.HasVideo)
        {
            Still.Visibility = Visibility.Collapsed;
        }

        Player.Position = TimeSpan.FromSeconds(_position);
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        Trace.TraceWarning("Windows can't play this file: {0}", e.ErrorException);
        _playerReady = false;
        ShowNoPlayback();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) => StopAtEnd();

    private void ShowNoPlayback()
    {
        Pause();
        PlayButton.IsEnabled = false;
        NoPlaybackText.Visibility = Visibility.Visible;
        Still.Visibility = Visibility.Visible;
        _frames?.Show(_position);
    }

    private async Task LoadStripAsync(MediaEditor media, string path)
    {
        var token = _closing.Token;

        try
        {
            if (!_info.HasVideo)
            {
                var wave = FrameLoader.Decode(await media.ReadWaveformAsync(path, token));
                Still.Source = wave;
                Still.Stretch = Stretch.Fill;
                Timeline.SetStrip([wave]);
                return;
            }

            // One at a time from both ends would show less; in order, the strip fills from the left.
            var pictures = new ImageSource?[StripPictures];
            for (var index = 0; index < StripPictures; index++)
            {
                var png = await media.ReadFrameAsync(path, _info.DurationSeconds * (index + 0.5) / StripPictures, token, StripPictureSide);
                pictures[index] = FrameLoader.Decode(png);
                Timeline.SetStrip(pictures);
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Editing works without the pictures.
            Trace.TraceWarning("No timeline pictures for {0}: {1}", path, exception);
        }
    }

    private void Seek(double seconds)
    {
        _position = Math.Clamp(seconds, 0, _info.DurationSeconds);

        if (_playerReady)
        {
            Player.Position = TimeSpan.FromSeconds(_position);
        }
        else
        {
            _frames?.Show(_position);
        }

        ShowPosition();
    }

    private void Play()
    {
        if (!_playerReady)
        {
            return;
        }

        // Plays what the result will be, so it starts somewhere that stays.
        var start = _edit.NextKept(_position) ?? _edit.KeptRanges[0].Start;
        if (_info.DurationSeconds - start < FrameStep)
        {
            start = _edit.KeptRanges[0].Start;
        }

        Seek(start);
        Player.Play();
        _playing = true;
        _playback.Start();
        ShowPlaying();
    }

    private void Pause()
    {
        if (_playerReady)
        {
            Player.Pause();
        }

        _playing = false;
        _playback.Stop();
        ShowPlaying();
    }

    private void FollowPlayer()
    {
        var position = Player.Position.TotalSeconds;

        if (_edit.NextKept(position) is not { } next)
        {
            StopAtEnd();
            return;
        }

        // Skips the removed clips, so playback sounds and looks like the saved file.
        if (next > position)
        {
            Player.Position = TimeSpan.FromSeconds(next);
            position = next;
        }

        _position = position;
        ShowPosition();
    }

    private void StopAtEnd()
    {
        Pause();
        Seek(_edit.KeptRanges[^1].End);
    }

    private void RememberForUndo()
    {
        _undo.Push(_edit);
        _redo.Clear();
    }

    private void Commit(MediaEdit edit)
    {
        if (!ReferenceEquals(edit, _edit))
        {
            RememberForUndo();
            Apply(edit);
        }
    }

    private void Apply(MediaEdit edit)
    {
        _edit = edit;
        Timeline.Edit = edit;
        _shownClip = -1;
        ShowState();
    }

    private void Split() => Commit(_edit.SplitAt(_position));

    private void ToggleCurrentClip()
    {
        var index = _edit.ClipIndexAt(_position);
        Commit(_edit.Clips[index].Kept ? _edit.Remove(index) : _edit.Restore(index));
    }

    private void Undo()
    {
        if (_undo.Count > 0)
        {
            _redo.Push(_edit);
            Apply(_undo.Pop());
        }
    }

    private void Redo()
    {
        if (_redo.Count > 0)
        {
            _undo.Push(_edit);
            Apply(_redo.Pop());
        }
    }

    private void ShowPosition()
    {
        Timeline.Position = _position;
        TimeText.Text = $"{FrameLoader.TimeText(_position, _info.DurationSeconds)} / {FrameLoader.TimeText(_info.DurationSeconds, _info.DurationSeconds)}";

        SplitButton.IsEnabled = _edit.CanSplitAt(_position);
        if (_edit.ClipIndexAt(_position) != _shownClip)
        {
            ShowState();
        }
    }

    /// <summary>Everything that depends on the clips or on which clip the playhead is in.</summary>
    private void ShowState()
    {
        var current = _edit.ClipIndexAt(_position);
        _shownClip = current;

        _showingState = true;
        ClipList.ItemsSource = _edit.Clips.Select((clip, index) => new ClipOption(index, ClipLabel(index, clip))).ToArray();
        ClipList.SelectedIndex = current;
        _showingState = false;

        var clipKept = _edit.Clips[current].Kept;
        RemoveButton.Content = clipKept ? Strings.EditRemove : Strings.EditRestore;
        RemoveButton.IsEnabled = !clipKept || _edit.CanRemove(current);
        SplitButton.IsEnabled = _edit.CanSplitAt(_position);
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;

        KeptText.Text = Strings.EditKept(FrameLoader.TimeText(_edit.KeptSeconds, _info.DurationSeconds));
        SeparateBox.Visibility = _edit.KeptCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        ShowCanSave();
    }

    private void ShowCanSave()
    {
        // Splitting alone only changes anything if the parts become separate files.
        SaveButton.IsEnabled = _edit.HasRemovals || (SeparateFiles && _edit.KeptCount > 1);
    }

    private string ClipLabel(int index, EditClip clip) => clip.Kept
        ? $"{index + 1} · {FrameLoader.TimeText(clip.Range.Length, _info.DurationSeconds)}"
        : $"{index + 1} · {Strings.EditRemovedClip}";

    private void ShowPlaying()
    {
        var label = _playing ? Strings.EditPause : Strings.EditPlay;
        PlayIcon.Text = _playing ? PauseGlyph : PlayGlyph;
        PlayButton.ToolTip = label;
        AutomationProperties.SetName(PlayButton, label);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Space and arrows already mean something on these, and taking them away would strand the keyboard.
        var focused = Keyboard.FocusedElement;
        var onButton = focused is ButtonBase;
        var onList = focused is ListBoxItem or ListBox;

        var handled = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.Space, ModifierKeys.None) when !onButton => Do(TogglePlaying),
            (Key.Left, ModifierKeys.None) when !onList => Do(() => Seek(_position - SecondStep)),
            (Key.Right, ModifierKeys.None) when !onList => Do(() => Seek(_position + SecondStep)),
            (Key.Left, ModifierKeys.Shift) when !onList => Do(() => Seek(_position - FrameStep)),
            (Key.Right, ModifierKeys.Shift) when !onList => Do(() => Seek(_position + FrameStep)),
            (Key.Home, ModifierKeys.None) when !onList => Do(() => Seek(0)),
            (Key.End, ModifierKeys.None) when !onList => Do(() => Seek(_info.DurationSeconds)),
            (Key.S or Key.M, ModifierKeys.None) => Do(Split),
            (Key.Delete, ModifierKeys.None) => Do(ToggleCurrentClip),
            (Key.I, ModifierKeys.None) => Do(() => Commit(_edit.TrimStart(_position))),
            (Key.O, ModifierKeys.None) => Do(() => Commit(_edit.TrimEnd(_position))),
            (Key.Z, ModifierKeys.Control) => Do(Undo),
            (Key.Y, ModifierKeys.Control) or (Key.Z, ModifierKeys.Control | ModifierKeys.Shift) => Do(Redo),
            _ => false,
        };

        e.Handled = handled;
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    private void TogglePlaying()
    {
        if (_playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void ClipList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showingState && ClipList.SelectedItem is ClipOption option)
        {
            Pause();
            Seek(_edit.Clips[option.Index].Range.Start);
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlaying();

    private void ToStart_Click(object sender, RoutedEventArgs e) => Seek(0);

    private void ToEnd_Click(object sender, RoutedEventArgs e) => Seek(_info.DurationSeconds);

    private void Split_Click(object sender, RoutedEventArgs e) => Split();

    private void Remove_Click(object sender, RoutedEventArgs e) => ToggleCurrentClip();

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void SeparateBox_Changed(object sender, RoutedEventArgs e) => ShowCanSave();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CleanUp()
    {
        _closing.Cancel();
        _closing.Dispose();
        _frames?.Dispose();
        _playback.Stop();
        Player.Stop();
        Player.Close();
        Player.Source = null;
    }

    private sealed record ClipOption(int Index, string Label)
    {
        public override string ToString() => Label;
    }
}
