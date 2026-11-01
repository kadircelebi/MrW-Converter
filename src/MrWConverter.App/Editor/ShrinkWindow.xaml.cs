using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user say how small the files should get.</summary>
public partial class ShrinkWindow : Window
{
    // Decimal, as services state their limits, so a "25 MB" file also passes a limit of 25,000,000 bytes.
    private const long BytesPerMegabyte = 1_000_000;

    private const double LargestLimitMegabytes = 1_000_000;

    private static readonly int[] Sides = [3840, 2560, 1920, 1280, 854, 640];

    private readonly long _bytes;

    public ShrinkWindow(IReadOnlyList<FileEntry> files)
    {
        InitializeComponent();

        _bytes = files.Sum(file => file.Bytes);
        SummaryText.Text = $"{Strings.FileCount(files.Count)} · {FileEntry.FormatSize(_bytes)}";

        SideList.ItemsSource = Sides.Select(side => new SideOption($"{side} px", side)).Prepend(new SideOption(Strings.ShrinkSideKeep, null)).ToArray();
        SideList.SelectedIndex = 0;
        LimitBox.Text = "25";

        ShowChoice();
        Loaded += (_, _) => ShareSlider.Focus();
    }

    public ShrinkOptions Options => new(
        LimitChoice.IsChecked == true ? LimitBytes : null,
        ShareChoice.IsChecked == true ? ShareSlider.Value / 100 : null,
        ChosenSide);

    private int? ChosenSide => (SideList.SelectedItem as SideOption)?.Side;

    private long? LimitBytes =>
        double.TryParse(LimitBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var megabytes)
        && megabytes > 0
        && megabytes <= LargestLimitMegabytes
            ? (long)(megabytes * BytesPerMegabyte)
            : null;

    private void ShowChoice()
    {
        // Events fire while the window is still being built.
        if (SaveButton is null || ShareText is null)
        {
            return;
        }

        var share = (int)ShareSlider.Value;
        ShareText.Text = $"{Strings.ShrinkShareValue(share)} · {FileEntry.FormatSize(_bytes * share / 100)}";

        SaveButton.IsEnabled = NoTargetChoice.IsChecked == true ? ChosenSide is not null
            : LimitChoice.IsChecked != true || LimitBytes is not null;
    }

    private void Choice_Changed(object sender, RoutedEventArgs e) => ShowChoice();

    private void Share_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowChoice();

    private void Limit_TextChanged(object sender, TextChangedEventArgs e) => ShowChoice();

    private void Side_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowChoice();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record SideOption(string Label, int? Side)
    {
        public override string ToString() => Label;
    }
}
