using PuppeteerSharp;

namespace Glimpse.Drive;

/// <summary>Starts and stops resources on a .NET Aspire dashboard, the same way on every OS.</summary>
public static class Aspire
{
    private static readonly TimeSpan PressTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(60);

    // Finds the resource's row and presses its button; the dashboard renders rows after the page loads, so the
    // caller polls this until it answers "pressed".
    private const string PressScript = """
        (resource, label) => {
            const row = [...document.querySelectorAll('[role=row]')].find(r => r.textContent.includes(resource));
            const button = row?.querySelector(`[aria-label='${label}']`);
            if (!button) return row ? 'no button' : 'no row';
            button.click();
            return 'pressed';
        }
        """;

    private const string StateScript = """
        (resource, pattern) => {
            const row = [...document.querySelectorAll('[role=row]')].find(r => r.textContent.includes(resource));
            return !!row && new RegExp(pattern).test(row.textContent);
        }
        """;

    // Drives the installed Edge or Chrome over the DevTools protocol, so nothing is downloaded.
    public static async Task Run(string action, string resource, string loginUrl)
    {
        var executable = InstalledBrowser.Find(InstalledBrowser.Candidates(), File.Exists);
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            ExecutablePath = executable,
            Headless = true,
            AcceptInsecureCerts = true,
        });
        await using var page = await browser.NewPageAsync();
        await page.GoToAsync(loginUrl);

        await Press(page, resource, $"{(action == "start" ? "Start" : "Stop")} resource");

        var state = action == "start" ? "Running" : "Finished|Exited";
        await page.WaitForFunctionAsync(StateScript, new WaitForFunctionOptions { Timeout = (int)StateTimeout.TotalMilliseconds }, resource, state);
        Console.WriteLine($"{resource}: {state}");
    }

    private static async Task Press(IPage page, string resource, string label)
    {
        var deadline = DateTime.UtcNow + PressTimeout;
        var answer = "";
        while (DateTime.UtcNow < deadline)
        {
            answer = await page.EvaluateFunctionAsync<string>(PressScript, resource, label);
            if (answer == "pressed")
            {
                return;
            }
            await Task.Delay(Cli.Poll);
        }
        throw new InvalidOperationException($"could not press '{label}' for {resource}: {answer}");
    }
}
