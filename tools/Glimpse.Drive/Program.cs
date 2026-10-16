using Glimpse.Drive;

#if WINDOWS
if (OperatingSystem.IsWindows())
{
    return await Cli.RunAsync(args, new Glimpse.Drive.Windows.WindowsDesktopDriver(), Glimpse.Drive.Windows.WindowList.ReportVisibleWindows);
}
#else
if (OperatingSystem.IsMacOS())
{
    return await Cli.RunAsync(args, new Glimpse.Drive.MacOS.MacDesktopDriver(), Glimpse.Drive.MacOS.WindowServer.ReportVisibleWindows);
}
#endif

return await Cli.RunAsync(args, driver: null, onWatchdog: _ => { });
