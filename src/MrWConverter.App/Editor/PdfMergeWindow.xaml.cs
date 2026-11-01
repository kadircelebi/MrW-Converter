using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MrWConverter.App.Resources;
using MrWConverter.App.ViewModels;
using MrWConverter.Core.Pdf;

namespace MrWConverter.App.Editor;

/// <summary>Lets the user put the files in the order the PDF should have them.</summary>
public partial class PdfMergeWindow : Window
{
    private readonly ObservableCollection<FileEntry> _files;

    public PdfMergeWindow(IEnumerable<FileEntry> files)
    {
        InitializeComponent();

        _files = new ObservableCollection<FileEntry>(files);
        FileList.ItemsSource = _files;
        FileList.SelectedIndex = 0;

        SizeList.ItemsSource = new[]
        {
            new SizeOption(Strings.PdfMergeSizePicture, PdfPageSize.Picture),
            new SizeOption("A4", PdfPageSize.A4),
        };
        SizeList.SelectedIndex = 0;

        CountText.Text = Strings.FileCount(_files.Count);
        Loaded += (_, _) => FileList.Focus();
    }

    public IReadOnlyList<string> OrderedPaths => _files.Select(file => file.Path).ToArray();

    public PdfPageSize PageSize => ((SizeOption)SizeList.SelectedItem).Size;

    private void Move(int by)
    {
        var from = FileList.SelectedIndex;
        var to = from + by;

        if (from >= 0 && to >= 0 && to < _files.Count)
        {
            _files.Move(from, to);
            FileList.ScrollIntoView(FileList.SelectedItem);

            // The selection stays on the same file, so no selection change reports the new place.
            ShowMoves();
        }
    }

    private void ShowMoves()
    {
        UpButton.IsEnabled = FileList.SelectedIndex > 0;
        DownButton.IsEnabled = FileList.SelectedIndex >= 0 && FileList.SelectedIndex < _files.Count - 1;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowMoves();

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    /// <summary>In the order File Explorer shows, so "page 2" comes before "page 10".</summary>
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        var selected = FileList.SelectedItem;
        var sorted = _files.OrderBy(file => file.Name, Comparer<string>.Create(StrCmpLogicalW)).ToArray();

        for (var index = 0; index < sorted.Length; index++)
        {
            _files.Move(_files.IndexOf(sorted[index]), index);
        }

        FileList.SelectedItem = selected;
        ShowMoves();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // With Alt held, Windows reports the arrow as a system key.
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Up or Key.Down)
        {
            Move(e.SystemKey == Key.Up ? -1 : 1);
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string first, string second);

    private sealed record SizeOption(string Label, PdfPageSize Size)
    {
        public override string ToString() => Label;
    }
}
