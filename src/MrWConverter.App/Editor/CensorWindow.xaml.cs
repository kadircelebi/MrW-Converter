using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MrWConverter.App.Resources;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user mark the parts of one image to cover, and choose how they are covered.</summary>
public partial class CensorWindow : Window
{
    private readonly CensorSurface _surface;

    public CensorWindow(ImagePreview preview)
    {
        InitializeComponent();

        _surface = new CensorSurface(preview);
        AutomationProperties.SetName(_surface, Strings.CensorArea);
        _surface.RegionsChanged += ShowRegions;
        SurfaceHost.Child = _surface;

        MosaicSizeSlider.Minimum = ImageCensor.MinimumMosaicTilePercent;
        MosaicSizeSlider.Maximum = ImageCensor.MaximumMosaicTilePercent;
        MosaicSizeSlider.Value = _surface.MosaicTilePercent;
        MosaicSizeSlider.ValueChanged += (_, e) => _surface.MosaicTilePercent = e.NewValue;

        // Noise first: it hides as completely as black and looks better.
        StyleList.ItemsSource = new[]
        {
            new StyleOption(Strings.CensorStyleNoise, CensorStyle.Noise),
            new StyleOption(Strings.CensorStyleBlack, CensorStyle.Black),
            new StyleOption(Strings.CensorStyleMosaic, CensorStyle.Mosaic),
        };
        StyleList.SelectedIndex = 0;

        ShowRegions();
        Loaded += (_, _) => _surface.Focus();
    }

    public IReadOnlyList<ImageRegion> Regions => _surface.Regions;

    public CensorStyle ChosenStyle => _surface.ChosenStyle;

    public double MosaicTilePercent => _surface.MosaicTilePercent;

    private void StyleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StyleList.SelectedItem is StyleOption option)
        {
            _surface.ChosenStyle = option.Style;
            var mosaic = option.Style == CensorStyle.Mosaic ? Visibility.Visible : Visibility.Collapsed;
            MosaicSizePanel.Visibility = mosaic;
            MosaicWarning.Visibility = mosaic;
        }
    }

    private void ShowRegions()
    {
        CountText.Text = Strings.CensorCount(Regions.Count);
        RemoveButton.IsEnabled = _surface.HasSelection;
        SaveButton.IsEnabled = Regions.Count > 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _surface.AddBox();
        _surface.Focus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        _surface.RemoveSelected();
        _surface.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record StyleOption(string Label, CensorStyle Style)
    {
        public override string ToString() => Label;
    }
}
