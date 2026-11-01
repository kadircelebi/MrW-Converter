using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MrWConverter.Core.Tests;

/// <summary>Reads the app's .resx files directly, so a missing translation fails here and not on a user's screen.</summary>
public partial class LocalizationTests
{
    private static readonly string ResourcesFolder = FindResourcesFolder();

    [Fact]
    public void Turkish_HasEveryEnglishString()
    {
        Assert.Equal(Load("Strings.resx").Keys.Order(), Load("Strings.tr.resx").Keys.Order());
    }

    [Fact]
    public void Turkish_UsesTheSamePlaceholders()
    {
        var english = Load("Strings.resx");
        var turkish = Load("Strings.tr.resx");

        foreach (var (key, value) in english)
        {
            Assert.Equal(Placeholders(value), Placeholders(turkish[key]));
        }
    }

    [Fact]
    public void EveryStringIsExposedToTheApp()
    {
        var source = File.ReadAllText(Path.Combine(ResourcesFolder, "Strings.cs"));

        foreach (var key in Load("Strings.resx").Keys)
        {
            Assert.Contains($"string {key}", source);
        }
    }

    private static IEnumerable<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).Order();

    private static Dictionary<string, string> Load(string fileName) =>
        XDocument.Load(Path.Combine(ResourcesFolder, fileName)).Root!
            .Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

    private static string FindResourcesFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MrWConverter.slnx")))
            {
                return Path.Combine(folder.FullName, "src", "MrWConverter.App", "Resources");
            }
        }

        throw new DirectoryNotFoundException("Could not find the solution folder.");
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
