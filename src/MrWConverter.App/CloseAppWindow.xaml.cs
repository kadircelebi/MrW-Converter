using System.Windows;

namespace MrWConverter.App;

public enum CloseChoice
{
    Cancel,
    Minimize,
    Exit,
}

/// <summary>Asks what the X button should do, because the wheel only works while the app stays open.</summary>
public partial class CloseAppWindow : Window
{
    public CloseAppWindow(bool jobRunning)
    {
        InitializeComponent();

        BusyText.Visibility = jobRunning ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => MinimizeButton.Focus();
    }

    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    private void Minimize_Click(object sender, RoutedEventArgs e) => Finish(CloseChoice.Minimize);

    private void Exit_Click(object sender, RoutedEventArgs e) => Finish(CloseChoice.Exit);

    private void Finish(CloseChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
