using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageMagick;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.Core.Formats;
using MrWConverter.Core.Tools;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user give pictures a new pixel size, smaller or larger.</summary>
public partial class ResizeWindow : Window
{
    private readonly string _firstName;

    // The first file's size, so the window can show what the choice does to it. Null when it can't be read.
    private readonly (int Width, int Height)? _firstSize;

    public ResizeWindow(IReadOnlyList<FileEntry> files, FormatCatalog catalog)
    {
        InitializeComponent();

        _firstName = files[0].Name;
        _firstSize = ReadSize(files[0].Path, catalog);

        SummaryText.Text = files.Count == 1 && _firstSize is { } size
            ? $"{Strings.FileCount(1)} · {Pixels(size)}"
            : Strings.FileCount(files.Count);

        PercentBox.Text = "50";
        SideBox.Text = "1920";

        ShowChoice();
        // An editable box only reports its text once its template is in place.
        Loaded += (_, _) =>
        {
            ShowChoice();
            PercentBox.Focus();
        };
    }

    /// <summary>The size the user chose. Only valid while Save is enabled.</summary>
    public ResizeOptions Options =>
        SideChoice.IsChecked == true ? ResizeOptions.ByLongestSide(Whole(SideBox.Text) ?? 0)
        : BoxChoice.IsChecked == true ? ResizeOptions.ToBox(Whole(WidthBox.Text) ?? 0, Whole(HeightBox.Text) ?? 0, FillChoice.IsChecked == true)
        : ResizeOptions.ByPercent(Number(PercentBox.Text) ?? 0);

    private static (int Width, int Height)? ReadSize(string path, FormatCatalog catalog)
    {
        try
        {
            return ImageResizer.ReadSize(path, catalog.FormatOf(path));
        }
        catch (Exception exception) when (exception is MagickException or IOException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static string Pixels((int Width, int Height) size) => $"{size.Width} × {size.Height} px";

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ? value : null;

    private static int? Whole(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value : null;

    private void ShowChoice()
    {
        // Events fire while the window is still being built.
        if (SaveButton is null || PreviewText is null || LargerText is null)
        {
            return;
        }

        var options = Options;
        SaveButton.IsEnabled = options.IsValid;

        if (options.IsValid && _firstSize is { } size)
        {
            var plan = options.Plan(size.Width, size.Height);
            PreviewText.Text = Strings.ResizePreview(_firstName, Pixels(size), Pixels((plan.Width, plan.Height)));
            LargerText.Visibility = plan.ScaledWidth > size.Width ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            PreviewText.Text = "";
            LargerText.Visibility = Visibility.Collapsed;
        }
    }

    private void Choice_Changed(object sender, RoutedEventArgs e) => ShowChoice();

    private void Value_TextChanged(object sender, TextChangedEventArgs e) => ShowChoice();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
