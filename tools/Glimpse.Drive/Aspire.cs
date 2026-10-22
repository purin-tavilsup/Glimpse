using PuppeteerSharp;

namespace Glimpse.Drive;

/// <summary>Starts and stops resources on a .NET Aspire dashboard, the same way on every OS.</summary>
public static class Aspire
{
    private static readonly TimeSpan PressTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(60);
    private const string RunningState = "Running";
    private const string StoppedState = "Finished|Exited";

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

    // The row's cells as the dashboard shows them, so a timeout can say what state the resource is stuck in.
    private const string RowScript = """
        (resource) => {
            const row = [...document.querySelectorAll('[role=row]')].find(r => r.textContent.includes(resource));
            if (!row) return '';
            return [...row.querySelectorAll('[role=gridcell]')].map(c => c.innerText.trim()).filter(t => t).join(' | ');
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

        var state = action == "start" ? RunningState : StoppedState;
        try
        {
            await page.WaitForFunctionAsync(StateScript, new WaitForFunctionOptions { Timeout = (int)StateTimeout.TotalMilliseconds }, resource, state);
        }
        catch (WaitTaskTimeoutException)
        {
            throw new InvalidOperationException(TimeoutMessage(resource, state, await page.EvaluateFunctionAsync<string>(RowScript, resource)));
        }
        Console.WriteLine($"{resource}: {state}");
    }

    /// <summary>
    /// Names the state the resource is stuck in. A waiting one is held by a resource it depends on; one still running
    /// after a stop has usually relaunched itself into a process the dashboard did not start and cannot stop.
    /// </summary>
    public static string TimeoutMessage(string resource, string state, string row)
    {
        var message = $"{resource} did not reach {state} in {StateTimeout.TotalSeconds:0} s; its dashboard row: {(row.Length > 0 ? row : "(not found)")}";
        if (row.Contains("Waiting", StringComparison.OrdinalIgnoreCase))
        {
            return message + ". A waiting resource starts only once the resources it waits for are up (WaitFor/WaitForStart): " +
                   "check their state";
        }
        if (state == StoppedState && row.Contains(RunningState, StringComparison.OrdinalIgnoreCase))
        {
            return message + ". If the app restarted itself, the dashboard no longer owns that process: quit it from the " +
                   "app, for example its tray menu";
        }
        return message;
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
