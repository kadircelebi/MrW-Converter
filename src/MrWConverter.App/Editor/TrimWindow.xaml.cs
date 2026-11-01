using System.Diagnostics;
using System.Windows;
using MrWConverter.App.Resources;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user choose which part of one audio or video file to keep.</summary>
public partial class TrimWindow : Window
{
    private const double SmallStep = 0.1;
    private const double LargeStepShare = 0.02;

    private readonly double _duration;
    private readonly FrameLoader? _startFrames;
    private readonly FrameLoader? _endFrames;

    public TrimWindow(MediaEditor media, string path, MediaInfo info)
    {
        InitializeComponent();

        _duration = info.DurationSeconds;

        foreach (var slider in new[] { StartSlider, EndSlider })
        {
            slider.Maximum = _duration;
            slider.SmallChange = SmallStep;
            slider.LargeChange = Math.Max(SmallStep, _duration * LargeStepShare);
        }

        EndSlider.Value = _duration;

        if (info.HasVideo)
        {
            _startFrames = new FrameLoader((seconds, token) => media.ReadFrameAsync(path, seconds, token), image => StartFrame.Source = image);
            _endFrames = new FrameLoader((seconds, token) => media.ReadFrameAsync(path, seconds, token), image => EndFrame.Source = image);
            _startFrames.Show(0);
            _endFrames.Show(_duration);
        }
        else
        {
            StartFrameCard.Visibility = Visibility.Collapsed;
            EndFrameCard.Visibility = Visibility.Collapsed;
            WaveCard.Visibility = Visibility.Visible;
            _ = ShowWaveAsync(media, path);
        }

        ShowRange();
        Loaded += (_, _) => StartSlider.Focus();
        Closed += (_, _) =>
        {
            _startFrames?.Dispose();
            _endFrames?.Dispose();
        };
    }

    public double Start => StartSlider.Value;

    public double End => EndSlider.Value;

    private async Task ShowWaveAsync(MediaEditor media, string path)
    {
        try
        {
            Wave.Source = FrameLoader.Decode(await media.ReadWaveformAsync(path));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The times still work without the picture.
            Trace.TraceWarning("No waveform for {0}: {1}", path, exception);
        }
    }

    private void StartSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Loading: the other slider isn't there yet.
        if (EndSlider is null)
        {
            return;
        }

        var latest = End - MediaEditor.MinimumTrimSeconds;
        if (Start > latest)
        {
            StartSlider.Value = Math.Max(0, latest);
            return;
        }

        ShowRange();
        _startFrames?.Show(Start);
    }

    private void EndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var earliest = Start + MediaEditor.MinimumTrimSeconds;
        if (End < earliest)
        {
            EndSlider.Value = Math.Min(_duration, earliest);
            return;
        }

        ShowRange();
        _endFrames?.Show(End);
    }

    private void ShowRange()
    {
        if (SaveButton is null || _duration == 0)
        {
            return;
        }

        StartText.Text = FrameLoader.TimeText(Start, _duration);
        EndText.Text = FrameLoader.TimeText(End, _duration);
        KeptText.Text = Strings.TrimKept(FrameLoader.TimeText(End - Start, _duration));

        BeforeColumn.Width = new GridLength(Start, GridUnitType.Star);
        KeptColumn.Width = new GridLength(End - Start, GridUnitType.Star);
        AfterColumn.Width = new GridLength(_duration - End, GridUnitType.Star);

        SaveButton.IsEnabled = End - Start >= MediaEditor.MinimumTrimSeconds && End - Start < _duration;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
