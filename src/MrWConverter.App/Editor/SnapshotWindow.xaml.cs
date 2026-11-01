using System.Windows;
using MrWConverter.Core.Media;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user choose which moment of one video to save as a picture.</summary>
public partial class SnapshotWindow : Window
{
    private const double SmallStep = 0.1;
    private const double LargeStepShare = 0.02;

    public SnapshotWindow(MediaEditor media, string path, MediaInfo info)
    {
        InitializeComponent();

        var frames = new FrameLoader((seconds, token) => media.ReadFrameAsync(path, seconds, token), image => Frame.Source = image);

        TimeSlider.Maximum = info.DurationSeconds;
        TimeSlider.SmallChange = SmallStep;
        TimeSlider.LargeChange = Math.Max(SmallStep, info.DurationSeconds * LargeStepShare);
        TimeSlider.ValueChanged += (_, e) =>
        {
            TimeText.Text = FrameLoader.TimeText(e.NewValue, info.DurationSeconds);
            frames.Show(e.NewValue);
        };

        TimeText.Text = FrameLoader.TimeText(0, info.DurationSeconds);
        frames.Show(0);

        Loaded += (_, _) => TimeSlider.Focus();
        Closed += (_, _) => frames.Dispose();
    }

    public double Seconds => TimeSlider.Value;

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
