using MrWConverter.Core.Settings;

namespace MrWConverter.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DragWheel_IsOffByDefault()
    {
        using var folder = new TemporaryFolder();

        Assert.False(AppSettings.Load(folder.File("missing.json")).DragWheelEnabled);
    }

    [Fact]
    public void Save_CreatesTheFolderAndRoundTrips()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "nested", "settings.json");

        new AppSettings { DragWheelEnabled = true }.Save(path);

        Assert.True(AppSettings.Load(path).DragWheelEnabled);
    }

    [Fact]
    public void Load_WithADamagedFile_FallsBackToDefaults()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.False(AppSettings.Load(path).DragWheelEnabled);
    }
}
