using System.Diagnostics;

namespace Cantio.Services;

public static class HotspotService
{
    public static void OpenSettings()
        => Process.Start(new ProcessStartInfo("ms-settings:network-mobilehotspot") { UseShellExecute = true });
}
