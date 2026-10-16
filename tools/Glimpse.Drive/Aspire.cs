using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Glimpse.Drive;

/// <summary>Starts and stops resources on a .NET Aspire dashboard, the same way on every OS.</summary>
public static class Aspire
{
    // Playwright drives the installed Edge, so nothing is downloaded.
    public static async Task Run(string action, string resource, string loginUrl)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
        var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await page.GotoAsync(loginUrl);

        var row = page.Locator("[role=row]").Filter(new() { HasText = resource }).First;
        var button = $"[aria-label='{(action == "start" ? "Start" : "Stop")} resource']";
        await row.Locator(button).First.ClickAsync(new() { Timeout = 15000 });

        var state = action == "start" ? "Running" : "Finished|Exited";
        await row.Filter(new() { HasTextRegex = new Regex(state) }).WaitForAsync(new() { Timeout = 60000 });
        Console.WriteLine($"{resource}: {state}");
    }
}
