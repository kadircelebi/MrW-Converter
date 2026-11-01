using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MrWConverter.App.Resources;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user choose which part of one image to keep.</summary>
public partial class CropWindow : Window
{
    private readonly CropSurface _surface;

    public CropWindow(ImagePreview preview)
    {
        InitializeComponent();

        _surface = new CropSurface(preview);
        AutomationProperties.SetName(_surface, Strings.CropArea);
        _surface.RegionChanged += ShowRegion;
        SurfaceHost.Child = _surface;

        RatioList.ItemsSource = new[]
        {
            new RatioOption(Strings.CropFree, null),
            new RatioOption("1:1", 1),
            OrientedRatio(4, 3),
            OrientedRatio(3, 2),
            OrientedRatio(16, 9),
        };
        RatioList.SelectedIndex = 0;

        ShowRegion();
        Loaded += (_, _) => _surface.Focus();
    }

    public ImageRegion Region => _surface.Region;

    /// <summary>A tall picture is usually cropped tall, so its ratios are offered that way round.</summary>
    private RatioOption OrientedRatio(int longSide, int shortSide) => _surface.IsPortrait
        ? new RatioOption($"{shortSide}:{longSide}", (double)shortSide / longSide)
        : new RatioOption($"{longSide}:{shortSide}", (double)longSide / shortSide);

    private void RatioList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RatioList.SelectedItem is RatioOption option)
        {
            _surface.UseRatio(option.Ratio);
        }
    }

    private void ShowRegion()
    {
        SizeText.Text = string.Format(CultureInfo.CurrentCulture, "{0} × {1}", Region.Width, Region.Height);
        SaveButton.IsEnabled = !_surface.IsWholeImage;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record RatioOption(string Label, double? Ratio)
    {
        public override string ToString() => Label;
    }
}
