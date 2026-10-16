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
  Aspire.cs           shared: .NET Aspire dashboard start/stop (Playwright)
  Windows/            Windows driver: UI Automation (FlaUI) + Win32
  MacOS/              macOS driver (not built yet)
drive-plugin/         the Claude Code plugin: bin/ wrappers + the driving-desktop-apps skill
```

The project multi-targets `net10.0;net10.0-windows`. `Windows/` and FlaUI build only into `net10.0-windows`,
because UI Automation needs a Windows target. Every other OS runs the `net10.0` build. The test project binds to
`net10.0`, so the shared logic is tested on every CI leg.

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
- **Output lines are an interface.** Skills and people read them, so keep these shapes:
  - `windows` / `zorder`: `#<index> <proc> '<title>' rect=<bounds>`, front to back, index over all windows
  - `shot`: `saved <file>: ... origin (<x>,<y>)`, where screen pixel = origin + image pixel
  - `foreground`: `[PASS |FAIL ]foreground: <proc> '<title>' id='<id>' visible=<bool> rect=...`
  - an unmatched window's label: `unexpected window <bounds>`
- **Exit codes** come from `Cli`: 0 ok, 1 assertion or lookup failed, 2 watchdog. A driver throws; it never exits.
- **The watchdog** abandons a step whose accessibility call stopped answering. `Program.cs` hands `Cli` an
  `onWatchdog` report for the OS that lists the process's visible windows **without** going through the
  accessibility API, which is what is stuck (Windows uses `EnumWindows`).

## Adding a driver (macOS next)

1. `MacOS/MacDesktopDriver.cs` implementing `IDesktopDriver` over the Accessibility API (AXUIElement), and pick
   it in `Program.cs` when `OperatingSystem.IsMacOS()`.
2. A watchdog report for macOS that does not touch the app's accessibility tree (for example the window list
   from CoreGraphics).
3. `drive-plugin/bin/glimpse-drive` (bash, kept LF in `.gitattributes`) building and running the `net10.0`
   build, the way `plugin/bin/glimpse` does, and `scripts/install.sh` linking `drive-plugin`.
4. A row in the skill's OS table, plus any macOS-only traps it finds (permissions first).

Where the two APIs differ, keep the command's meaning and note the mapping in the skill:

| Command needs | Windows | macOS starting point |
|---|---|---|
| element id / name | AutomationId / Name | `AXIdentifier` / `AXTitle` or `AXDescription` |
| `invoke` | Invoke pattern | `AXPress` action |
| `value` / `type` | Value pattern | `AXValue` |
| `tray` | notification-area `NotifyItemIcon`, overflow window | status-bar items (`AXMenuBarItem` of the app's extras) |
| `foreground` | `GetForegroundWindow` | frontmost app + its `AXFocusedWindow` |
| `shot` / window list | FlaUI capture | the window finder and capturer `Glimpse.Core` already has for macOS |
| `closetab` | Chrome's address bar via UIA | Chrome's address bar via AX |

The process running the driver needs the Accessibility permission (System Settings → Privacy & Security →
Accessibility) on macOS. A driver should fail with a message naming that permission rather than "not found".

## Not in this change

- **Marketplace install.** The plugin works from a clone only. A marketplace install has no clone to build, so
  it needs a released package (like `Glimpse.Capture` through `dotnet dnx`); that and the `marketplace.json`
  entry come together in a release change.
- **Driver tests.** Drivers talk to real windows; like the capture code's `RealDesktop` tests, they would run
  locally, not on CI. The shared logic is what the unit tests cover.
