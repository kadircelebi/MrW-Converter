using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using MrWConverter.App.Wheel;

namespace MrWConverter.App;

/// <summary>
/// Starting with Windows goes through the package's startup task, which stays off until the
/// user asks and which they can also turn off in Windows Settings. It needs the package, so
/// a build run outside the MSIX doesn't offer it.
/// </summary>
public static class WindowsStartup
{
    // The same as uap5:StartupTask TaskId in packaging\AppxManifest.xml.
    private const string TaskId = "MrWConverterStartup";

    private const int NoPackage = 15700;

    public static bool IsPackaged
    {
        get
        {
            var length = 0;
            return NativeMethods.GetCurrentPackageFullName(ref length, null) != NoPackage;
        }
    }

    public static bool LaunchedAtSignIn =>
        IsPackaged && AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;

    public static async Task<StartupTask?> FindTaskAsync() =>
        IsPackaged ? await StartupTask.GetAsync(TaskId) : null;
}
