namespace Glimpse.Drive;

/// <summary>
/// The command layer shared by every OS: parsing, the watchdog, <c>run</c> flows, <c>aspire</c>, and the
/// window-set assertions. Everything that touches a window goes through the OS's <see cref="IDesktopDriver"/>.
/// </summary>
public static class Cli
{
    public const int FindDefaultSeconds = 20;
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);
    private const int WaitDefaultSeconds = 60, ExpectDefaultSeconds = 10, HoldDefaultSeconds = 30;

    public const string Usage = """
        glimpse-drive <command> ...    (exit 0 = ok, 1 = assertion/lookup failed, 2 = watchdog)

        Window keys: an id or exact name; "~text" = name contains text;
                     "@Anchor/Key" = Key inside the window that contains Anchor;
                     "Parent>Key" = Key inside the element Parent.

          windows                              all top-level windows, front to back
          tree <proc> [depth]                  accessibility tree of the process's windows
          controls <proc> <titlePart>          text boxes, buttons and documents in one window
          wait <proc> <key> [sec]              wait until key exists (default 60 s); fails after 10 s if <proc> has no process
          expect <proc> <anchor,anchor|none> [sec]
                                               visible windows must become exactly those holding the anchors;
                                               fails after 10 s if <proc> has no process (unless "none")
          hold <proc> <anchor,anchor|none> [sec]
                                               ...and must stay that way for the whole duration (default 30 s)
          click <proc> <key>                   real mouse click on the element
          invoke <proc> <key>                  press it through the accessibility API (no mouse)
          type <proc> <key> <text>             set a text box value
          value <proc> <key>                   print the element's value (e.g. a browser address bar)
          name <proc> <key>                    print the element's name (WinForms labels carry their text there)
          keys <proc> <titlePart> <tok>...     bring the window to the front, then type; tokens {ENTER} {TAB} {ESC} {WAITFOR:key}
          closetab <urlPart[,urlPart]>         close the browser's active tab while its address contains one of them
          clickat <x> <y>                      click screen pixel (last resort)
          front <proc> <titlePart>             bring that window to the front (before a clickat: another window may cover it)
          shot <file.png> [<proc> <titlePart>] screenshot the whole desktop, or that window brought to the front (file first or last);
                                               prints the origin: screen pixel = origin + image pixel
          foreground [<proc> [anchor]]         print the window holding keyboard focus; with <proc>, PASS only if
                                               that process's VISIBLE window (holding anchor) has it
          tray <tooltipPart> [right]           click (or right-click) a status-area icon
          zorder <proc>[,proc]                 front-to-back order of those processes' windows
          aspire <start|stop> <resource> <dashboardLoginUrl>
                                               press the .NET Aspire dashboard's Start/Stop and wait for the state
          run <flow.steps>... [key=value]...   run each file's steps in ONE session; one command per line,
                                               # comments, $var filled from key=value; stops on the first failure

        Every command has a hard limit (its own timeout + 30 s). Past it the driver exits 2, so a blocked
        UI thread fails the step instead of hanging it.
        """;

    /// <param name="driver">The OS's driver, or null where none exists yet; only <c>aspire</c> and <c>run</c> work then.</param>
    /// <param name="onWatchdog">Reports what it can about a step that got no answer, without touching the blocked app.</param>
    public static async Task<int> RunAsync(string[] args, IDesktopDriver? driver, Action<string[]> onWatchdog)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        // `run` drives many steps in one process, so it can't be watchdog-wrapped as a whole; it guards each step itself.
        return args[0] == "run"
            ? await RunScript(args[1..], driver, onWatchdog)
            : await WithWatchdog(args, driver, onWatchdog);
    }

    // A hung accessibility call is stuck in the OS and can't be cancelled, so the watchdog abandons it and exits.
    private static async Task<int> WithWatchdog(string[] args, IDesktopDriver? driver, Action<string[]> onWatchdog)
    {
        var work = Task.Run(() => Run(args, driver));
        if (await Task.WhenAny(work, Task.Delay(HardLimit(args))) == work)
        {
            return await work;
        }
        Console.Error.WriteLine($"FAIL: watchdog: '{string.Join(' ', args)}' got no answer in {HardLimit(args).TotalSeconds:0} s. " +
                                "The app's UI thread is probably blocked, often by a modal that `invoke` opened.");
        onWatchdog(args);
        return 2;
    }

    private static async Task<int> Run(string[] args, IDesktopDriver? driver)
    {
        try
        {
            if (args[0] == "aspire")
            {
                await Aspire.Run(args[1], args[2], args[3]);
                return 0;
            }
            return Execute(args, driver ?? throw new PlatformNotSupportedException(
                $"no desktop driver for {System.Runtime.InteropServices.RuntimeInformation.OSDescription} yet"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // CLI boundary: one readable FAIL line beats a stack trace in a step log; the exit code carries the result.
            Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return 1;
        }
    }

    private static int Execute(string[] args, IDesktopDriver driver)
    {
        switch (args[0])
        {
            case "windows": driver.ListWindows(null); break;
            case "zorder": driver.ListWindows(args[1].Split(',')); break;
            case "tree": driver.Tree(args[1], args.Length > 2 ? int.Parse(args[2]) : 6); break;
            case "controls": driver.Controls(args[1], args[2]); break;
            case "wait":
                AppPresence.For(args[1]).Wait(slice => driver.Wait(args[1], args[2], slice), Seconds(args, 3, WaitDefaultSeconds));
                Console.WriteLine($"found {args[2]}");
                break;
            case "expect":
                return WindowSet.Expect(a => driver.WindowLabels(args[1], a), args[2], Seconds(args, 3, ExpectDefaultSeconds), Poll,
                                        AppPresence.For(args[1]));
            case "hold": return WindowSet.Hold(a => driver.WindowLabels(args[1], a), args[2], Seconds(args, 3, HoldDefaultSeconds), Poll);
            case "click": driver.Click(args[1], args[2]); break;
            case "invoke": driver.Invoke(args[1], args[2]); break;
            case "type": driver.Type(args[1], args[2], args[3]); break;
            case "value": Console.WriteLine(driver.Value(args[1], args[2])); break;
            case "name": Console.WriteLine(driver.Name(args[1], args[2])); break;
            case "keys": driver.Keys(args[1], args[2], args[3..]); break;
            case "closetab": Console.WriteLine($"closed {driver.CloseTabs(args[1])} tab(s)"); break;
            case "clickat": driver.ClickAt(int.Parse(args[1]), int.Parse(args[2])); break;
            case "front": driver.Front(args[1], args[2]); break;
            case "shot":
                var shot = ShotArguments.Parse(args[1..]);
                driver.Shot(shot.File, shot.Proc, shot.TitlePart);
                break;
            case "foreground": return driver.Foreground(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            case "tray": driver.Tray(args[1], args.Length > 2 && args[2] == "right"); break;
            default: Console.Error.WriteLine($"unknown command {args[0]}"); return 1;
        }
        return 0;
    }

    private static TimeSpan HardLimit(string[] a) => TimeSpan.FromSeconds(30) + a[0] switch
    {
        "wait" => Seconds(a, 3, WaitDefaultSeconds),
        "expect" => Seconds(a, 3, ExpectDefaultSeconds),
        "hold" => Seconds(a, 3, HoldDefaultSeconds),
        "tree" or "aspire" => TimeSpan.FromSeconds(150), // a deep tree of a busy screen, or a resource start
        _ => TimeSpan.FromSeconds(FindDefaultSeconds),
    };

    private static TimeSpan Seconds(string[] a, int i, int fallback) => TimeSpan.FromSeconds(a.Length > i ? int.Parse(a[i]) : fallback);

    // A flow is one CLI command per line: the same one session drives them all, so a long flow pays startup once.
    private static async Task<int> RunScript(string[] args, IDesktopDriver? driver, Action<string[]> onWatchdog)
    {
        var (files, variables) = StepScript.SplitRunArgs(args);
        if (files.Length == 0)
        {
            Console.Error.WriteLine("FAIL: run needs at least one .steps file");
            return 1;
        }

        foreach (var file in files)
        {
            var code = await RunFile(file, variables, driver, onWatchdog);
            if (code != 0)
            {
                return code;
            }
        }
        return 0;
    }

    private static async Task<int> RunFile(string file, Dictionary<string, string> variables, IDesktopDriver? driver, Action<string[]> onWatchdog)
    {
        var name = Path.GetFileName(file);
        var n = 0;
        foreach (var raw in await File.ReadAllLinesAsync(file))
        {
            n++;
            string[]? step;
            try
            {
                step = StepScript.ParseLine(raw, variables);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"FAIL {name}:{n}: {ex.Message}");
                return 1;
            }
            if (step is null)
            {
                continue;
            }

            Console.WriteLine($"[{name}:{n}] {string.Join(' ', step)}");
            var code = await WithWatchdog(step, driver, onWatchdog);
            if (code != 0)
            {
                Console.Error.WriteLine($"stopped at {name}:{n} (exit {code})");
                return code;
            }
        }
        return 0;
    }
}
