using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using MrWConverter.App.Resources;

namespace MrWConverter.App;

/// <summary>Credits FFmpeg and the other components, as their licenses require.</summary>
public partial class AboutWindow : Window
{
    private const string SupportLink = "https://buymeacoffee.com/mrworth";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        VersionText.Text = $"MrWConverter · {Strings.AboutVersion} {version}";
        NoticesText.Text = ReadNotices();
    }

    /// <summary>Store policy 10.8.2: a voluntary tip in the browser is fine as long as it unlocks nothing in the app.</summary>
    private void Support_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SupportLink) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // No browser is set up. Nothing depends on this, so the window just stays as it is.
            Trace.TraceWarning("Could not open the support link: {0}", exception);
        }
    }

    private static string ReadNotices()
    {
        try
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Strings.AboutNoticesUnavailable;
        }
    }
}
