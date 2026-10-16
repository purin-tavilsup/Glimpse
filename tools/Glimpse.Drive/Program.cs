using Glimpse.Drive;

if (OperatingSystem.IsWindows())
{
    return await Cli.RunAsync(args, new Glimpse.Drive.Windows.WindowsDesktopDriver(), Glimpse.Drive.Windows.WindowList.ReportVisibleWindows);
}

if (OperatingSystem.IsMacOS())
{
    return await Cli.RunAsync(args, new Glimpse.Drive.MacOS.MacDesktopDriver(), Glimpse.Drive.MacOS.WindowServer.ReportVisibleWindows);
}

return await Cli.RunAsync(args, driver: null, onWatchdog: _ => { });
