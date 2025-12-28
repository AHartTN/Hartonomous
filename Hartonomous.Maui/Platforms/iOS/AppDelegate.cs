using System.Diagnostics.CodeAnalysis;
using Foundation;

namespace Hartonomous.Maui.Platforms.iOS;

[Register("AppDelegate")]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "AppDelegate is the required class name for iOS application delegates per Apple's UIKit API.")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
