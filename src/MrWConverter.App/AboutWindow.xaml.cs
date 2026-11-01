using System.IO;
using System.Reflection;
using System.Windows;
using MrWConverter.App.Resources;

namespace MrWConverter.App;

/// <summary>Credits FFmpeg and the other components, as their licenses require.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        VersionText.Text = $"MrWConverter · {Strings.AboutVersion} {version}";
        NoticesText.Text = ReadNotices();
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
