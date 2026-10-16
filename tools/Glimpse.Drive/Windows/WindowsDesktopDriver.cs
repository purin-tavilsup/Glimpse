using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Glimpse.Drive.Windows;

/// <summary>The desktop driver on Windows, through UI Automation (FlaUI) and a few Win32 calls.</summary>
internal sealed class WindowsDesktopDriver : IDesktopDriver
{
    private const int InvokeLimitSeconds = 10;
    private const int MaxTabsClosed = 20;
    private const string ChromeAddressBar = "Address and search bar";
    private static readonly TimeSpan FindDefault = TimeSpan.FromSeconds(Cli.FindDefaultSeconds);

    public void ListWindows(string[]? processNames)
    {
        var i = 0;
        foreach (var w in Automation().GetDesktop().FindAllChildren())
        {
            var p = w.Properties;
            var proc = ProcessName(p.ProcessId.ValueOrDefault);
            if (processNames == null || processNames.Contains(proc, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"#{i} {proc} '{p.Name.ValueOrDefault}' rect={p.BoundingRectangle.ValueOrDefault}");
            }
            i++;
        }
    }

    public void Tree(string proc, int depth)
    {
        foreach (var window in TopWindows(proc))
        {
            Dump(window, 0, depth);
        }
    }

    public void Controls(string proc, string titlePart)
    {
        var window = WindowTitled(proc, titlePart);
        var controls = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)
                                                         .Or(cf.ByControlType(ControlType.Button))
                                                         .Or(cf.ByControlType(ControlType.Document)));
        foreach (var e in controls)
        {
            Console.WriteLine($"{e.Properties.ControlType.ValueOrDefault} name='{e.Properties.Name.ValueOrDefault}' id='{e.Properties.AutomationId.ValueOrDefault}'");
        }
    }

    public void Wait(string proc, string key, TimeSpan timeout) => Find(proc, key, timeout);

    public IReadOnlyList<string> WindowLabels(string proc, IReadOnlyList<string> anchors) =>
        [.. TopWindows(proc).Select(w => anchors.FirstOrDefault(a => Match(w, a) != null)
                                         ?? $"unexpected window {w.Properties.BoundingRectangle.ValueOrDefault}")];

    public void Click(string proc, string key) => Find(proc, key).Click();

    // WinForms runs a button's click, ShowDialog included, inside the UIA Invoke call, so a modal makes Invoke block.
    public void Invoke(string proc, string key)
    {
        var element = Find(proc, key);
        try
        {
            Task.Run(() => element.AsButton().Invoke()).WaitAsync(TimeSpan.FromSeconds(InvokeLimitSeconds)).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"invoke got no answer in {InvokeLimitSeconds} s: the button probably opened a modal. " +
                                       "The press DID happen and the dialog is up now. Use `click` for buttons that open dialogs.");
        }
    }

    public void Type(string proc, string key, string text) => Find(proc, key).AsTextBox().Text = text;

    public string Value(string proc, string key) => Find(proc, key).Patterns.Value.Pattern.Value.Value;

    public string Name(string proc, string key) => Find(proc, key).Properties.Name.Value;

    public void Keys(string proc, string titlePart, IReadOnlyList<string> tokens)
    {
        BringToFront(proc, titlePart);
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "{ENTER}": Keyboard.Type(VirtualKeyShort.ENTER); break;
                case "{TAB}": Keyboard.Type(VirtualKeyShort.TAB); break;
                case "{ESC}": Keyboard.Type(VirtualKeyShort.ESCAPE); break;
                case var t when t.StartsWith("{WAITFOR:") && t.EndsWith('}'): Find(proc, t[9..^1]); break;
                default: Keyboard.Type(token); break;
            }
            // Gives the app time to react (focus moves, a page loads) before the next key arrives.
            Thread.Sleep(300);
        }
    }

    public void ClickAt(int x, int y) => Mouse.Click(new System.Drawing.Point(x, y));

    public void Front(string proc, string titlePart) => BringToFront(proc, titlePart);

    // Read shows a big shot scaled down, so the origin printed here is what turns an image pixel back into a clickat point.
    public void Shot(string file, string? proc, string? titlePart)
    {
        if (proc is null || titlePart is null)
        {
            Capture.Screen(-1).ToFile(file);
            var x = Native.GetSystemMetrics(Native.XVirtualScreen);
            var y = Native.GetSystemMetrics(Native.YVirtualScreen);
            Console.WriteLine($"saved {file}: whole desktop, origin ({x},{y})");
            return;
        }

        // The capture copies screen pixels, so whatever covers the window would be in the shot instead.
        var window = BringToFront(proc, titlePart);
        var r = window.BoundingRectangle;
        Capture.Element(window).ToFile(file);
        Console.WriteLine($"saved {file}: {proc} '{window.Properties.Name.ValueOrDefault}', origin ({r.X},{r.Y})");
    }

    // Taking the foreground never makes a window visible, so a hidden window can hold the keyboard while the user sees
    // another one; the PASS rule demands a visible window for that reason.
    public int Foreground(string? proc, string? anchor)
    {
        var hwnd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        Native.GetWindowRect(hwnd, out var r);
        var owner = ProcessName((int)processId);
        var visible = Native.IsWindowVisible(hwnd);
        var window = Automation().FromHandle(hwnd);
        var line = $"foreground: {owner} '{window.Properties.Name.ValueOrDefault}' id='{window.Properties.AutomationId.ValueOrDefault}' " +
                   $"visible={visible} rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})";
        if (proc is null)
        {
            Console.WriteLine(line);
            return 0;
        }

        var pass = visible && owner.Equals(proc, StringComparison.OrdinalIgnoreCase)
                   && (anchor is null || MatchOne(window, anchor) is not null);
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {line}");
        return pass ? 0 : 1;
    }

    // Icons usually sit in the hidden-icons overflow, a separate explorer window that only exists while open.
    public void Tray(string tooltipPart, bool rightClick)
    {
        var icon = TrayIcon(tooltipPart);
        if (icon == null)
        {
            Find("explorer", "~Hidden Icons").Click();
            Thread.Sleep(1000);
            icon = TrayIcon(tooltipPart) ?? throw new InvalidOperationException($"no tray icon named *{tooltipPart}*");
        }
        if (rightClick)
        {
            icon.RightClick();
        }
        else
        {
            icon.Click();
        }
    }

    // Apps under test open their pages in the person's own Chrome, so a tab is closed only when its address proves
    // it is the app's; after a close the next tab becomes active and is checked the same way.
    public int CloseTabs(string urlParts)
    {
        var closed = 0;
        while (closed < MaxTabsClosed && ActiveTabMatching(urlParts) is { } window)
        {
            window.AsWindow().SetForeground();
            Thread.Sleep(300);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Thread.Sleep(700);
            closed++;
        }
        return closed;
    }

    private static UIA3Automation Automation() => new();

    private static IEnumerable<AutomationElement> TopWindows(string proc)
    {
        var pids = System.Diagnostics.Process.GetProcessesByName(proc).Select(p => p.Id).ToHashSet();
        return Automation().GetDesktop().FindAllChildren()
            .Where(w => pids.Contains(w.Properties.ProcessId.ValueOrDefault) && !w.Properties.IsOffscreen.ValueOrDefault);
    }

    private static AutomationElement Find(string proc, string key, TimeSpan? timeout = null)
    {
        var (anchor, target) = ElementKey.Parse(key);
        var deadline = DateTime.UtcNow + (timeout ?? FindDefault);
        do
        {
            foreach (var window in TopWindows(proc))
            {
                if (anchor != null && Match(window, anchor) == null)
                {
                    continue;
                }
                if (Match(window, target) is { } hit)
                {
                    return hit;
                }
            }
            Thread.Sleep(Cli.Poll);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException($"'{key}' not found in {proc}");
    }

    private static AutomationElement? Match(AutomationElement root, string key) =>
        ElementKey.Path(key).Aggregate((AutomationElement?)root, (scope, part) => scope is null ? null : MatchOne(scope, part));

    // The root counts too, so a window's own AutomationId works as a key.
    private static AutomationElement? MatchOne(AutomationElement root, string key) => key.StartsWith('~')
        ? root.FindAllDescendants().FirstOrDefault(e => e.Properties.Name.ValueOrDefault?.Contains(key[1..], StringComparison.OrdinalIgnoreCase) == true)
        : root.Properties.AutomationId.ValueOrDefault == key ? root
        : root.FindFirstDescendant(cf => cf.ByAutomationId(key).Or(cf.ByName(key)));

    private static AutomationElement WindowTitled(string proc, string titlePart) =>
        TopWindows(proc).FirstOrDefault(w => w.Properties.Name.ValueOrDefault?.Contains(titlePart) == true)
        ?? throw new InvalidOperationException($"no {proc} window titled *{titlePart}*");

    private static AutomationElement BringToFront(string proc, string titlePart)
    {
        var window = WindowTitled(proc, titlePart);
        window.AsWindow().SetForeground();
        Thread.Sleep(Cli.Poll);
        return window;
    }

    // Taskbar buttons carry the app name too; only NotifyItemIcon elements are notification-area icons.
    private static AutomationElement? TrayIcon(string tooltipPart) => TopWindows("explorer")
        .SelectMany(w => w.FindAllDescendants(cf => cf.ByAutomationId("NotifyItemIcon")))
        .FirstOrDefault(e => e.Properties.Name.ValueOrDefault?.Contains(tooltipPart, StringComparison.OrdinalIgnoreCase) == true);

    private static AutomationElement? ActiveTabMatching(string urlParts) =>
        TopWindows("chrome").FirstOrDefault(w =>
            Match(w, ChromeAddressBar)?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault is { } url
            && urlParts.Split(',').Any(part => url.Contains(part, StringComparison.OrdinalIgnoreCase)));

    private static void Dump(AutomationElement e, int level, int depth)
    {
        var p = e.Properties;
        Console.WriteLine($"{new string(' ', level * 2)}{p.ControlType.ValueOrDefault} name='{p.Name.ValueOrDefault}' " +
                          $"id='{p.AutomationId.ValueOrDefault}' enabled={p.IsEnabled.ValueOrDefault} rect={p.BoundingRectangle.ValueOrDefault}");
        if (level >= depth)
        {
            return;
        }
        foreach (var c in e.FindAllChildren())
        {
            Dump(c, level + 1, depth);
        }
    }

    private static string ProcessName(int processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(processId).ProcessName;
        }
        catch (ArgumentException)
        {
            return ""; // the process exited between enumeration and lookup
        }
    }
}
