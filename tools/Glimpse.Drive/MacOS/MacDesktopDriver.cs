using System.Drawing;
using System.Runtime.Versioning;
using Glimpse.Core;

namespace Glimpse.Drive.MacOS;

/// <summary>The desktop driver on macOS, through the Accessibility API, CoreGraphics events and the window server.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacDesktopDriver : IDesktopDriver
{
    private const int MaxTabsClosed = 20;
    private const string ChromeProcess = "Google Chrome";
    private const string ChromeAddressBar = "Address and search bar";
    private const string AccessibilitySettings = "System Settings > Privacy & Security > Accessibility";
    private static readonly TimeSpan FindDefault = TimeSpan.FromSeconds(Cli.FindDefaultSeconds);
    private static readonly string[] ControlRoles = ["AXTextField", "AXTextArea", "AXButton", "AXWebArea"];

    // Titles come from the window server only with the Screen Recording permission, so a missing one is read
    // from the window's accessibility element instead.
    public void ListWindows(string[]? processNames)
    {
        var i = 0;
        foreach (var w in WindowServer.AppWindows())
        {
            var proc = WindowServer.ProcessName(w.OwnerPid);
            if (processNames == null || processNames.Contains(proc, StringComparer.OrdinalIgnoreCase))
            {
                var bounds = new Rectangle(w.X, w.Y, w.Width, w.Height);
                Console.WriteLine($"#{i} {proc} '{w.Title ?? AxTitle(w.OwnerPid, bounds)}' rect={bounds}");
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
        foreach (var e in WindowTitled(proc, titlePart).Descendants().Where(e => ControlRoles.Contains(e.Role)))
        {
            Console.WriteLine($"{e.Role} name='{Label(e)}' id='{e.Identifier}'");
        }
    }

    public void Wait(string proc, string key, TimeSpan timeout) => Find(proc, key, timeout);

    public IReadOnlyList<string> WindowLabels(string proc, IReadOnlyList<string> anchors) =>
        [.. TopWindows(proc).Select(w => anchors.FirstOrDefault(a => Match(w, a) != null)
                                         ?? $"unexpected window {w.Frame ?? Rectangle.Empty}")];

    // A click on a background app's window only brings that app forward, so it comes forward first.
    public void Click(string proc, string key)
    {
        var element = Find(proc, key);
        var box = element.Frame ?? throw new InvalidOperationException($"'{key}' in {proc} has no position on screen");
        Raise(element.Element("AXWindow") ?? element);
        Input.Click(box.X + box.Width / 2.0, box.Y + box.Height / 2.0);
    }

    // An app answers accessibility calls on its main thread, so an action that runs a modal loop there leaves the
    // press unanswered; the call then gives up with CannotComplete rather than hanging.
    public void Invoke(string proc, string key)
    {
        switch (Find(proc, key).Perform("AXPress"))
        {
            case Native.Success:
                return;
            case Native.CannotComplete:
                throw new TimeoutException("invoke got no answer: the button probably opened a modal. " +
                                           "The press DID happen and the dialog is up now. Use `click` for buttons that open dialogs.");
            case var error:
                throw new InvalidOperationException($"'{key}' in {proc} refused AXPress (AX error {error}); try `click`");
        }
    }

    public void Type(string proc, string key, string text)
    {
        if (Find(proc, key).Set("AXValue", text) is var error and not Native.Success)
        {
            throw new InvalidOperationException($"'{key}' in {proc} refused a new AXValue (AX error {error}); click it and use `keys`");
        }
    }

    public string Value(string proc, string key) => Find(proc, key).Value ?? "";

    public string Name(string proc, string key) => Label(Find(proc, key)) ?? "";

    public void Keys(string proc, string titlePart, IReadOnlyList<string> tokens)
    {
        var pid = BringToFront(proc, titlePart).Pid;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "{ENTER}": Input.Key(pid, Input.Return); break;
                case "{TAB}": Input.Key(pid, Input.Tab); break;
                case "{ESC}": Input.Key(pid, Input.Escape); break;
                case var t when t.StartsWith("{WAITFOR:") && t.EndsWith('}'): Find(proc, t[9..^1]); break;
                default: Input.Text(pid, token); break;
            }
            // Gives the app time to react (focus moves, a page loads) before the next key arrives.
            Thread.Sleep(300);
        }
    }

    public void ClickAt(int x, int y) => Input.Click(x, y);

    public void Front(string proc, string titlePart) => BringToFront(proc, titlePart);

    // The image is scaled to screen points, the unit `clickat` takes, so screen point = origin + image pixel
    // holds on a Retina display too.
    public void Shot(string file, string? proc, string? titlePart)
    {
        if (proc is null || titlePart is null)
        {
            var display = Native.CGDisplayBounds(Native.CGMainDisplayID());
            Capture(CaptureMode.FullScreen, file, windowId: null, (int)display.Size.Width);
            Console.WriteLine($"saved {file}: main display, origin ({(int)display.Origin.X},{(int)display.Origin.Y})");
            return;
        }

        var window = BringToFront(proc, titlePart);
        var r = window.Frame ?? throw new InvalidOperationException($"{proc} window *{titlePart}* has no position on screen");
        Capture(CaptureMode.Window, file, ServerWindowId(window.Pid, r), r.Width);
        Console.WriteLine($"saved {file}: {proc} '{window.Title}', origin ({r.X},{r.Y})");
    }

    // Raising a window never un-hides its app, so a hidden app's window can hold the keyboard while the person sees
    // another one; the PASS rule demands a visible window for that reason.
    public int Foreground(string? proc, string? anchor)
    {
        var app = FrontmostApp();
        var window = app?.Element("AXFocusedWindow");
        var owner = app is null ? "" : WindowServer.ProcessName(app.Pid);
        var visible = window is { Minimized: false } && app is { Hidden: false };
        var r = window?.Frame ?? Rectangle.Empty;
        var line = $"foreground: {owner} '{window?.Title}' id='{window?.Identifier}' " +
                   $"visible={visible} rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})";
        if (proc is null)
        {
            Console.WriteLine(line);
            return 0;
        }

        var pass = visible && owner.Equals(proc, StringComparison.OrdinalIgnoreCase)
                   && (anchor is null || MatchOne(window!, anchor) is not null);
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {line}");
        return pass ? 0 : 1;
    }

    // A status item's tooltip is its AXHelp, but many items expose no tooltip, title or description at all; the
    // name of the process that owns the item then identifies it, as an app's name does a Windows tray icon.
    public void Tray(string tooltipPart, bool rightClick)
    {
        bool Holds(string? text) => text?.Contains(tooltipPart, StringComparison.OrdinalIgnoreCase) == true;
        var items = StatusItems().ToList();
        var item = items.FirstOrDefault(i => Holds(i.Item.Help) || Holds(i.Item.Title) || Holds(i.Item.Description)).Item
                   ?? items.FirstOrDefault(i => Holds(i.Proc)).Item
                   ?? throw new InvalidOperationException($"no status-bar item named *{tooltipPart}*");
        // An open menu runs the app's menu loop, which leaves the action that opened it unanswered.
        var result = item.Perform(rightClick ? "AXShowMenu" : "AXPress");
        if (result == Native.ActionUnsupported && rightClick)
        {
            result = item.Perform("AXPress");
        }
        if (result is not (Native.Success or Native.CannotComplete))
        {
            throw new InvalidOperationException($"status-bar item *{tooltipPart}* refused the press (AX error {result})");
        }
    }

    // Apps under test open their pages in the person's own Chrome, so a tab is closed only when its address proves
    // it is the app's; after a close the next tab becomes active and is checked the same way.
    public int CloseTabs(string urlParts)
    {
        var closed = 0;
        while (closed < MaxTabsClosed && ActiveTabMatching(urlParts) is { } window)
        {
            Raise(window);
            Input.Key(window.Pid, Input.W, Input.Command);
            Thread.Sleep(700);
            closed++;
        }
        return closed;
    }

    private static AxElement App(int pid) => Trusted() ? AxElement.Application(pid) : throw NoPermission();

    // Without the permission every accessibility call reports "no value", which would read as "not found".
    private static bool Trusted() => Native.AXIsProcessTrusted();

    private static InvalidOperationException NoPermission() => new(
        $"no Accessibility permission: allow the app that runs glimpse-drive (your terminal) in {AccessibilitySettings}");

    private static IEnumerable<AxElement> Apps(string proc) => WindowServer.ProcessIds(proc).Select(App);

    // A hidden window is ordered out and leaves AXWindows; a minimized one stays in it, and a hidden app keeps its list.
    private static IEnumerable<AxElement> TopWindows(string proc) =>
        Apps(proc).Where(app => !app.Hidden).SelectMany(app => app.Windows).Where(w => !w.Minimized);

    // An open menu is not a window on macOS but a child of the app, or of its status item; the Dock's icons sit in
    // a list the same way. Lookups search those too, so a flow can pick a menu item as it would on Windows.
    private static IEnumerable<AxElement> Scopes(string proc) => TopWindows(proc).Concat(Apps(proc).SelectMany(app =>
        app.Children.Where(c => c.Role is not ("AXWindow" or "AXMenuBar"))
           .Concat(app.Element("AXExtrasMenuBar") is { } extras ? [extras] : [])));

    private static AxElement Find(string proc, string key, TimeSpan? timeout = null)
    {
        var (anchor, target) = ElementKey.Parse(key);
        var deadline = DateTime.UtcNow + (timeout ?? FindDefault);
        do
        {
            foreach (var scope in Scopes(proc))
            {
                if (anchor != null && Match(scope, anchor) == null)
                {
                    continue;
                }
                if (Match(scope, target) is { } hit)
                {
                    return hit;
                }
            }
            Thread.Sleep(Cli.Poll);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException($"'{key}' not found in {proc}");
    }

    private static AxElement? Match(AxElement root, string key) =>
        ElementKey.Path(key).Aggregate((AxElement?)root, (scope, part) => scope is null ? null : MatchOne(scope, part));

    // The root counts too, so a window's own identifier works as a key.
    private static AxElement? MatchOne(AxElement root, string key) => key.StartsWith('~')
        ? root.Descendants().FirstOrDefault(e => Names(e).Any(n => n.Contains(key[1..], StringComparison.OrdinalIgnoreCase)))
        : root.Identifier == key ? root
        : root.Descendants().FirstOrDefault(e => Names(e).Contains(key));

    // A window's own buttons carry no identifier or title, only a subrole; without its AX prefix it reads like
    // the Windows title-bar ids, so `@Anchor/CloseButton` works on both. A label's text is its value, but a text
    // field's value is what was typed into it, which is no name. Lazy, so a match stops reading early.
    private static IEnumerable<string> Names(AxElement e)
    {
        if (e.Identifier is { } id)
        {
            yield return id;
        }
        if (e.Title is { } title)
        {
            yield return title;
        }
        if (e.Description is { } description)
        {
            yield return description;
        }
        if (e.Role == "AXStaticText" && e.Value is { } text)
        {
            yield return text;
        }
        if (e.Subrole is { } subrole)
        {
            yield return subrole[2..];
        }
    }

    // A label's text is its value on macOS, where a Windows label carries it as its name.
    private static string? Label(AxElement e) => e.Title ?? e.Description ?? (e.Role == "AXStaticText" ? e.Value : null);

    private static AxElement WindowTitled(string proc, string titlePart) =>
        TopWindows(proc).FirstOrDefault(w => w.Title?.Contains(titlePart) == true)
        ?? throw new InvalidOperationException($"no {proc} window titled *{titlePart}*");

    private static AxElement BringToFront(string proc, string titlePart)
    {
        var window = WindowTitled(proc, titlePart);
        Raise(window);
        return window;
    }

    private static void Raise(AxElement window)
    {
        App(window.Pid).Set("AXFrontmost", true);
        window.Perform("AXRaise");
        Thread.Sleep(Cli.Poll);
    }

    // The system-wide element's AXFocusedApplication answers CannotComplete to a command-line caller, so the apps
    // with a window on screen are asked instead; only one of them is frontmost, and an app whose last window
    // closed is none of them.
    private static AxElement? FrontmostApp() => WindowServer.AppWindows()
        .Select(w => w.OwnerPid).Distinct().Select(App).FirstOrDefault(app => app.Frontmost);

    private static string AxTitle(int pid, Rectangle bounds) =>
        App(pid).Windows.FirstOrDefault(w => w.Frame == bounds)?.Title ?? "";

    // The window server draws status items itself, so no window names their owners: every process is asked.
    private static IEnumerable<(string Proc, AxElement Item)> StatusItems() => WindowServer.Processes()
        .SelectMany(p => (App(p.Pid).Element("AXExtrasMenuBar")?.Children ?? []).Select(item => (p.Name, item)));

    private static AxElement? ActiveTabMatching(string urlParts) =>
        TopWindows(ChromeProcess).FirstOrDefault(w =>
            Match(w, ChromeAddressBar)?.Value is { } url
            && urlParts.Split(',').Any(part => url.Contains(part, StringComparison.OrdinalIgnoreCase)));

    // The capture takes the window server's id, which no accessibility attribute reports; the owner and the bounds
    // pick the same window out of its list.
    private static int ServerWindowId(int pid, Rectangle bounds) => (int)(WindowServer.AppWindows()
        .FirstOrDefault(w => w.OwnerPid == pid && new Rectangle(w.X, w.Y, w.Width, w.Height) == bounds)?.WindowId
        ?? throw new InvalidOperationException($"the window server lists no window of process {pid} at {bounds}"));

    private static void Capture(CaptureMode mode, string file, int? windowId, int widthInPoints)
    {
        new ScreenCapture().CaptureAsync(mode, file, windowId).GetAwaiter().GetResult();
        var resized = new ProcessRunner().RunAsync("sips", ["--resampleWidth", $"{widthInPoints}", file]).GetAwaiter().GetResult();
        if (resized.ExitCode != 0)
        {
            throw new InvalidOperationException($"sips could not scale {file} to screen points: {resized.StdErr}");
        }
    }

    private static void Dump(AxElement e, int level, int depth)
    {
        var value = e.Value is { } v ? $" value='{(v.Length > 60 ? v[..60] : v)}'" : "";
        var subrole = e.Subrole is { } s ? $"({s})" : "";
        Console.WriteLine($"{new string(' ', level * 2)}{e.Role}{subrole} name='{Label(e)}' id='{e.Identifier}'{value} " +
                          $"enabled={e.Enabled} rect={e.Frame}");
        if (level >= depth)
        {
            return;
        }
        foreach (var c in e.Children)
        {
            Dump(c, level + 1, depth);
        }
    }
}
