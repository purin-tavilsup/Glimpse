using Glimpse.Drive;

#if WINDOWS
if (OperatingSystem.IsWindows())
{
    return await Cli.RunAsync(args, new Glimpse.Drive.Windows.WindowsDesktopDriver(), Glimpse.Drive.Windows.Native.ReportVisibleWindows);
}
#endif

return await Cli.RunAsync(args, driver: null, onWatchdog: _ => { });
