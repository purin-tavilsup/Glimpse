using System.Drawing;
using System.Runtime.Versioning;
using Glimpse.Abstractions;
using Glimpse.Core;
using Interop.UIAutomationClient;

namespace Glimpse.Drive.Windows;

/// <summary>The desktop driver on Windows, through UI Automation, Win32 input and Glimpse.Core's window list and capture.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktopDriver : IDesktopDriver
{
    private const int InvokeLimitSeconds = 10;
    private const int MaxTabsClosed = 20;
    private const string ChromeAddressBar = "Address and search bar";
    private static readonly TimeSpan FindDefault = TimeSpan.FromSeconds(Cli.FindDefaultSeconds);

    private readonly Uia uia = new();

    public WindowsDesktopDriver() => Input.UsePhysicalPixels();

    public void ListWindows(string[]? processNames)
    {
        var i = 0;
        foreach (var w in WindowList.AppWindows())
        {
            if (processNames == null || processNames.Contains(w.OwnerName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"#{i} {w.OwnerName} '{w.Title}' rect={new Rectangle(w.X, w.Y, w.Width, w.Height)}");
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
        var condition = uia.AnyControlType(UIA_ControlTypeIds.UIA_EditControlTypeId, UIA_ControlTypeIds.UIA_ButtonControlTypeId,
                                           UIA_ControlTypeIds.UIA_DocumentControlTypeId);
        foreach (var e in WindowTitled(proc, titlePart).Find(TreeScope.TreeScope_Descendants, condition))
        {
            Console.WriteLine($"{e.ControlType()} name='{e.Name()}' id='{e.Id()}'");
        }
    }

    public void Wait(string proc, string key, TimeSpan timeout) => Find(proc, key, timeout);

    public IReadOnlyList<string> WindowLabels(string proc, IReadOnlyList<string> anchors) =>
        [.. TopWindows(proc).Select(w => anchors.FirstOrDefault(a => Match(w, a) != null) ?? $"unexpected window {w.Bounds()}")];

    public void Click(string proc, string key) => Click(Find(proc, key), right: false);

    // WinForms runs a button's click, ShowDialog included, inside the UIA Invoke call, so a modal makes Invoke block.
    public void Invoke(string proc, string key)
    {
        var invoke = Find(proc, key).Pattern<IUIAutomationInvokePattern>(UIA_PatternIds.UIA_InvokePatternId)
                     ?? throw new InvalidOperationException($"'{key}' can't be invoked; use `click`");
        try
        {
            Task.Run(invoke.Invoke).WaitAsync(TimeSpan.FromSeconds(InvokeLimitSeconds)).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"invoke got no answer in {InvokeLimitSeconds} s: the button probably opened a modal. " +
                                       "The press DID happen and the dialog is up now. Use `click` for buttons that open dialogs.");
        }
    }

    public void Type(string proc, string key, string text) => ValueOf(proc, key).SetValue(text);

    public string Value(string proc, string key) => ValueOf(proc, key).CurrentValue;

    public string Name(string proc, string key) => Find(proc, key).Name();

    public void Keys(string proc, string titlePart, IReadOnlyList<string> tokens)
    {
        BringToFront(proc, titlePart);
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "{ENTER}": Input.Keys(Input.Return); break;
                case "{TAB}": Input.Keys(Input.Tab); break;
                case "{ESC}": Input.Keys(Input.Escape); break;
                case var t when t.StartsWith("{WAITFOR:") && t.EndsWith('}'): Find(proc, t[9..^1]); break;
                default: Input.Text(token); break;
            }
            // Gives the app time to react (focus moves, a page loads) before the next key arrives.
            Thread.Sleep(300);
        }
    }

    public void ClickAt(int x, int y) => Input.Click(x, y);

    public void Front(string proc, string titlePart) => BringToFront(proc, titlePart);

    // Read shows a big shot scaled down, so the origin printed here is what turns an image pixel back into a clickat point.
    public void Shot(string file, string? proc, string? titlePart)
    {
        if (proc is null || titlePart is null)
        {
            Capture(file, windowId: null);
            var x = Native.GetSystemMetrics(Native.XVirtualScreen);
            var y = Native.GetSystemMetrics(Native.YVirtualScreen);
            Console.WriteLine($"saved {file}: whole desktop, origin ({x},{y})");
            return;
        }

        // The capture is cropped to the window's visible frame, which is the origin the window list reports.
        var window = BringToFront(proc, titlePart);
        var listed = WindowList.Of(proc).FirstOrDefault(w => w.WindowId == window.Handle().ToInt64())
                     ?? throw new InvalidOperationException($"the {proc} window titled *{titlePart}* closed");
        Capture(file, listed.WindowId);
        Console.WriteLine($"saved {file}: {proc} '{window.Name()}', origin ({listed.X},{listed.Y})");
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
        var window = uia.FromHandle(hwnd);
        var line = $"foreground: {owner} '{window?.Name()}' id='{window?.Id()}' " +
                   $"visible={visible} rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})";
        if (proc is null)
        {
            Console.WriteLine(line);
            return 0;
        }

        var pass = visible && owner.Equals(proc, StringComparison.OrdinalIgnoreCase)
                   && (anchor is null || (window is not null && MatchOne(window, anchor) is not null));
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {line}");
        return pass ? 0 : 1;
    }

    // Icons usually sit in the hidden-icons overflow, a separate explorer window that only exists while open.
    public void Tray(string tooltipPart, bool rightClick)
    {
        var icon = TrayIcon(tooltipPart);
        if (icon == null)
        {
            Click(Find("explorer", "~Hidden Icons"), right: false);
            Thread.Sleep(1000);
            icon = TrayIcon(tooltipPart) ?? throw new InvalidOperationException($"no tray icon named *{tooltipPart}*");
        }
        Click(icon, rightClick);
    }

    // Apps under test open their pages in the person's own Chrome, so a tab is closed only when its address proves
    // it is the app's; after a close the next tab becomes active and is checked the same way.
    public int CloseTabs(string urlParts)
    {
        var closed = 0;
        while (closed < MaxTabsClosed && ActiveTabMatching(urlParts) is { } window)
        {
            Input.BringToFront(window.Handle());
            Input.Keys(Input.Control, Input.W);
            Thread.Sleep(700);
            closed++;
        }
        return closed;
    }

    private static void Click(IUIAutomationElement element, bool right)
    {
        var point = element.ClickPoint();
        Input.Click(point.X, point.Y, right);
    }

    private static void Capture(string file, long? windowId)
    {
        var outcome = new WindowsAppCapturer()
            .CaptureAsync(new RenderRequest("", Path.GetFullPath(file), 0, 0, SnapshotTheme.Light, windowId))
            .GetAwaiter().GetResult();
        if (outcome.Status == "failed")
        {
            throw new InvalidOperationException($"capture failed: {string.Join("; ", outcome.Warnings)}");
        }
    }

    private IEnumerable<IUIAutomationElement> TopWindows(string proc) => WindowList.Of(proc)
        .Select(w => uia.FromHandle(new IntPtr(w.WindowId)))
        .OfType<IUIAutomationElement>()
        .Where(e => !e.IsOffscreen());

    private IUIAutomationElement Find(string proc, string key, TimeSpan? timeout = null)
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

    private IUIAutomationValuePattern ValueOf(string proc, string key) =>
        Find(proc, key).Pattern<IUIAutomationValuePattern>(UIA_PatternIds.UIA_ValuePatternId)
        ?? throw new InvalidOperationException($"'{key}' has no value to read or set");

    private IUIAutomationElement? Match(IUIAutomationElement root, string key) =>
        ElementKey.Path(key).Aggregate((IUIAutomationElement?)root, (scope, part) => scope is null ? null : MatchOne(scope, part));

    // The root counts too, so a window's own AutomationId works as a key.
    private IUIAutomationElement? MatchOne(IUIAutomationElement root, string key) => key.StartsWith('~')
        ? root.Find(TreeScope.TreeScope_Descendants, uia.All).FirstOrDefault(e => e.Name().Contains(key[1..], StringComparison.OrdinalIgnoreCase))
        : root.Id() == key ? root
        : root.FindFirst(TreeScope.TreeScope_Descendants, uia.IdOrName(key));

    private IUIAutomationElement WindowTitled(string proc, string titlePart) =>
        TopWindows(proc).FirstOrDefault(w => w.Name().Contains(titlePart))
        ?? throw new InvalidOperationException($"no {proc} window titled *{titlePart}*");

    private IUIAutomationElement BringToFront(string proc, string titlePart)
    {
        var window = WindowTitled(proc, titlePart);
        Input.BringToFront(window.Handle());
        return window;
    }

    // Taskbar buttons carry the app name too; only NotifyItemIcon elements are notification-area icons.
    private IUIAutomationElement? TrayIcon(string tooltipPart) => TopWindows("explorer")
        .SelectMany(w => w.Find(TreeScope.TreeScope_Descendants, uia.Id("NotifyItemIcon")))
        .FirstOrDefault(e => e.Name().Contains(tooltipPart, StringComparison.OrdinalIgnoreCase));

    private IUIAutomationElement? ActiveTabMatching(string urlParts) =>
        TopWindows("chrome").FirstOrDefault(w =>
            Match(w, ChromeAddressBar)?.Pattern<IUIAutomationValuePattern>(UIA_PatternIds.UIA_ValuePatternId)?.CurrentValue is { } url
            && urlParts.Split(',').Any(part => url.Contains(part, StringComparison.OrdinalIgnoreCase)));

    private void Dump(IUIAutomationElement e, int level, int depth)
    {
        Console.WriteLine($"{new string(' ', level * 2)}{e.ControlType()} name='{e.Name()}' " +
                          $"id='{e.Id()}' enabled={e.IsEnabled()} rect={e.Bounds()}");
        if (level >= depth)
        {
            return;
        }
        foreach (var c in e.Find(TreeScope.TreeScope_Children, uia.All))
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
