using System.Windows;

namespace MrWConverter.App;

public enum CloseChoice
{
    Cancel,
    Hide,
    Exit,
}

/// <summary>Asks what the X button should do, because the wheel only works while the app keeps running.</summary>
public partial class CloseAppWindow : Window
{
    public CloseAppWindow(bool jobRunning)
    {
        InitializeComponent();

        BusyText.Visibility = jobRunning ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => HideButton.Focus();
    }

    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public bool DontAskAgain => DontAskAgainBox.IsChecked == true;

    private void Hide_Click(object sender, RoutedEventArgs e) => Finish(CloseChoice.Hide);

    private void Exit_Click(object sender, RoutedEventArgs e) => Finish(CloseChoice.Exit);

    private void Finish(CloseChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
