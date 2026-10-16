# glimpse-drive: one desktop-driving CLI, one driver per OS

## Goal

An agent checking a desktop app end to end has to *act* on it (click, type, close a dialog, use the tray) and then
*assert* what is on screen. `glimpse-drive` gives it one CLI for that, with the same commands, keys, output and exit
codes on every OS, so a skill or a `.steps` flow written on one OS runs unchanged on another.

## Shape

```
tools/Glimpse.Drive/
  Program.cs          picks the driver for the running OS
  Cli.cs              shared: command dispatch, usage, watchdog, `run` flows
  IDesktopDriver.cs   the per-OS contract
  ElementKey.cs       shared: the key grammar
  StepScript.cs       shared: `.steps` parsing
  WindowSet.cs        shared: the `expect` / `hold` assertions
  Aspire.cs           shared: .NET Aspire dashboard start/stop (installed Edge or Chrome, over DevTools)
  Windows/            Windows driver: UI Automation (COM interop) + Win32 input
  MacOS/              macOS driver: Accessibility API (AXUIElement) + CoreGraphics
drive-plugin/         the Claude Code plugin: bin/ wrappers + the driving-desktop-apps skill
```

There is one build, `net10.0`, holding every driver; each driver's classes are marked with the OS they run on
(`[SupportedOSPlatform]`), so the platform analyzer catches a call that would reach the wrong OS. One build is what
lets the CLI ship as a .NET tool, which can't hold an OS-targeted build. That rules out FlaUI, which ships only
Windows-targeted builds, so the Windows driver calls UI Automation through `Interop.UIAutomationClient`, which
targets plain .NET. Both drivers read the `Glimpse.Core` window list. `Glimpse.Drive.Tests` runs on every CI leg;
`Glimpse.Drive.Windows.Tests` holds the Windows driver's desktop tests and is skipped elsewhere.

**Why the assertions are shared:** `expect` and `hold` decide whether a step passed. If each driver implemented
them, the same flow could pass on one OS and fail on another for reasons that have nothing to do with the app.
A driver only reports one label per visible window (`IDesktopDriver.WindowLabels`); `WindowSet` decides.

## The contract

Each `IDesktopDriver` member backs one command; its XML docs are the detailed contract. Rules every driver keeps:

- **Process names** are the OS's process name without an extension (`MyApp`, not `MyApp.exe`).
- **Keys** follow `ElementKey`: an id or exact name; `~text` = name contains text (case-insensitive);
  `@Anchor/Key` = Key inside the top-level window holding Anchor; `Parent>Key` = Key inside Parent. A window's
  own id matches too.
- **Lookups wait.** A lookup polls every `Cli.Poll` until its timeout (`Cli.FindDefaultSeconds` unless the
  command passes one) and then throws `InvalidOperationException` naming the key and process.
- **Only visible, on-screen top-level windows count** for `WindowLabels`, `Tree` and lookups.
- **Windows come from the OS's own window list**, never from asking the accessibility API to list the desktop. On
  Windows, UI Automation's desktop listing asks every app and sometimes takes tens of seconds, past the watchdog;
  the window list asks no app. A driver builds accessibility elements only for the windows of the process it drives,
  so another app that is stuck or suspended can't stall it. The list must hold the windows the accessibility API
  would: on Windows that leaves out cloaked and empty windows, and a desktop test checks it against UI Automation.
- **Output lines are an interface.** Skills and people read them, so keep these shapes:
  - `windows` / `zorder`: `#<index> <proc> '<title>' rect=<bounds>`, front to back, index over all windows
  - `shot`: `saved <file>: ... origin (<x>,<y>)`, where screen pixel = origin + image pixel
  - `foreground`: `[PASS |FAIL ]foreground: <proc> '<title>' id='<id>' visible=<bool> rect=...`
  - an unmatched window's label: `unexpected window <bounds>`
- **Exit codes** come from `Cli`: 0 ok, 1 assertion or lookup failed, 2 watchdog. A driver throws; it never exits.
- **The watchdog** abandons a step whose accessibility call stopped answering. `Program.cs` hands `Cli` an
  `onWatchdog` report for the OS that lists the process's visible windows **without** going through the
  accessibility API, which is what is stuck; both drivers print it from the window list.

## Adding a driver

1. `<OS>/<OS>DesktopDriver.cs` implementing `IDesktopDriver` over that OS's accessibility API, picked in
   `Program.cs` by `OperatingSystem.Is<OS>()`.
2. A watchdog report that does not touch the app's accessibility tree.
3. A wrapper in `drive-plugin/bin/` that builds and runs the driver's target, and a line in the install script
   that links `drive-plugin`.
4. A row in the skill's OS table, plus that OS's traps (permissions first).

Where the APIs differ, a command keeps its meaning. The mapping as built:

| Command needs | Windows | macOS |
|---|---|---|
| element id / name | AutomationId / Name | `AXIdentifier` / `AXTitle`, `AXDescription`, `AXValue`, or the subrole without `AX` |
| lookups search | top-level windows (menus are windows) | windows, then the app's open menus, its Dock list and its status-item menus |
| `invoke` | Invoke pattern | `AXPress` |
| `value` / `type` | Value pattern | `AXValue` |
| `tray` | notification-area `NotifyItemIcon`, overflow window | every process's `AXExtrasMenuBar` items, by tooltip (`AXHelp`), title, description, then owner name |
| `foreground` | `GetForegroundWindow` | the app answering `AXFrontmost`, and its `AXFocusedWindow` |
| window list | `Glimpse.Core`'s window finder (`EnumWindows`), visible frame bounds | `Glimpse.Core`'s window finder (the window server) |
| `shot` | `Glimpse.Core`'s capturer (PrintWindow), cropped to the visible frame | `screencapture`, scaled to screen points |
| `closetab` | Chrome's address bar via UIA | Chrome's address bar via AX |

Three macOS answers are not the obvious API. The system-wide element's `AXFocusedApplication` refuses a
command-line caller, so `foreground` asks the apps that have windows on screen. The window server draws status
items itself, so no window names their owner and `tray` asks every process. And a `screencapture` image is in
backing pixels, so `shot` scales it to points, the unit mouse events use, which keeps
"screen pixel = origin + image pixel" true on a Retina display.

The process running the driver needs the Accessibility permission (System Settings → Privacy & Security →
Accessibility) on macOS, and Screen Recording for `shot`. The driver fails with a message naming the
Accessibility permission rather than "not found".

## Not in this change

- **Marketplace install.** The plugin works from a clone only. A marketplace install has no clone to build, so
  it needs a released package (like `Glimpse.Capture` through `dotnet dnx`); that and the `marketplace.json`
  entry come together in a release change.
- **Driver tests.** Drivers talk to real windows; like the capture code's `RealDesktop` tests, they would run
  locally, not on CI. The shared logic is what the unit tests cover.
