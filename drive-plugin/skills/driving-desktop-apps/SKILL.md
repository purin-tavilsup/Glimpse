---
name: driving-desktop-apps
description: Use when an agent must operate a running desktop app itself — click buttons, type into fields, close dialogs, click a tray icon, start a .NET Aspire resource, check which windows are visible or in front — so an end-to-end run needs no human clicks. Also use when asked to "run the test yourself", "drive the UI", "automate the manual test", or when a person is doing clicks for you that a script could do.
---

# Driving desktop apps

`glimpse-drive` is one CLI with the same commands on every OS, backed by that OS's accessibility API. The agent
runs each step as one command, checks it, then runs the next. The person only watches. Their mouse moves, so
they must not use the computer during a run.

| OS | Driver | Status |
|---|---|---|
| Windows | UI Automation (FlaUI) | ready |
| macOS | Accessibility API | not built yet: every window command fails with "no desktop driver"; `aspire` and `run` work |

```bash
glimpse-drive            # prints every command
```

The plugin's wrapper builds the driver from your clone the first time and again whenever its source changes.

## Each step = act, then assert the whole window set

| Step | Command |
|---|---|
| Find names/ids first | `glimpse-drive tree <proc> 3`, `glimpse-drive windows`, `glimpse-drive controls <proc> <titlePart>` |
| Wait for the app (never fixed sleeps) | `glimpse-drive wait <proc> <key> 90` |
| Act | `invoke` (preferred, no mouse) · `click` · `type` · `keys` · `tray` (`tray <name> right` opens its menu) · `aspire start/stop` |
| Read one value | `name <proc> <key>` (a WinForms label's text is its Name) · `value <proc> <key>` (a Value pattern, e.g. a browser address bar). Both beat grepping a `tree` |
| Assert (waits up to 10 s, or `[sec]`) | `expect <proc> <anchorA,anchorB\|none>`, e.g. `expect MyApp EmailTextBox` = exactly one visible window, the one holding that element |
| Assert it *stays* so ("doesn't reappear") | `hold <proc> <anchors\|none> 30`. `expect` passes the moment the set matches; `hold` fails if it changes during the window |
| Front-most | `zorder <procA,procB>`, lower `#` = nearer the front |
| Who has keyboard focus | `foreground <proc> [anchor]`: PASS only if a **visible** window of `<proc>` (holding anchor) has it. Taking the foreground never makes a window visible, so a hidden window can swallow keystrokes while another one shows |
| Look | `shot <file.png> [<proc> <titlePart>]` (brings that window to the front first; prints its origin), then Read the PNG |
| Many steps in one session | `run flow.steps [key=value]...`: one command per line, `#` comments, `$key` filled in; stops at the first failure |

Every command has a **hard watchdog** (its own timeout + 30 s). Past it the stuck accessibility call can't be
cancelled, so the driver lists the app's visible windows through calls that don't need its UI thread and exits
**2** — a blocked UI thread fails the step instead of hanging your shell. `invoke` has its own 10 s limit
(see below).

Name every window by an **anchor**, an element only that window has (an email box, a menu button). `expect`
fails on any extra visible window. That is the point: a window that should have stayed hidden is the bug you're
looking for, and skimming a partial dump misses it.

## Keys

`AutomationId` or exact `Name`; `~text` = Name contains; `@Anchor/Key` = Key inside the window holding Anchor;
`Parent>Key` = Key inside the element Parent (chains, and combines with `@`).
Use `@` whenever ids repeat across windows, and `>` when they repeat inside one (WinForms custom text boxes all
wrap an Edit named `textBox1`). Apps with a custom window template often give every window the same
title-bar ids (e.g. `CloseButton`). `keys` and `controls` pick a window by **title**, so use them only on titled
windows like browsers (`keys chrome "Google Chrome" ...`). App windows are often untitled: use anchors.
On Windows, ids come from WPF `x:Name`, WinForms `Name`, or Avalonia `x:Name` unless
`AutomationProperties.AutomationId` is set.

`keys` tokens: `{ENTER}` `{TAB}` `{ESC}`, and `{WAITFOR:key}` — block until that element exists (a
condition, never a fixed sleep).

## `invoke` opens a modal = `invoke` hangs

WinForms answers UI Automation on its UI thread, and a button that calls `ShowDialog` runs the dialog loop
*inside* the Invoke call, so the call never returns while the modal is up. `invoke` gives up after **10 s**
and fails with "the button probably opened a modal — the press DID happen, use `click`". The dialog is real and
on screen; carry on against it (close it with `invoke`, which is fine for a button that *closes* a dialog).
Rule of thumb: **`click` a button that opens a modal, `invoke` one that closes it or stays on the same screen.**

## Browsers

- **Pages the agent opens itself** (the .NET Aspire dashboard, local admin pages): Playwright with
  `Channel = "msedge"`, so there's no browser download. `aspire` already does the dashboard. Its login URL
  (`/login?t=<token>`) is in the AppHost console and is new every run.
- **Pages the app opens in the default browser** (OAuth/OIDC sign-in): Chrome hides web content from UI
  Automation, and the page is usually tied to that browser's cookies (`/interaction/<id>`), so it can't be
  replayed in Playwright. Read the URL with `value chrome "Address and search bar"`. Shoot the page's window
  with `shot <file> chrome "<tab title>"`: it brings that window to the front, so no other window covers the
  button you then click. Screen pixel = printed origin + image pixel; if Read downscaled the shot ("multiply by
  2.24"), multiply first. `clickat x y` the button, `keys` into the focused field, and re-shoot after each page
  (email → password → maybe consent). A password manager badge can take focus, so click the field before typing.
  Lasting fix: make the app open a Playwright-controlled browser in its dev profile.
- The default browser may still be signed in, so the sign-in page can skip itself. Check the address bar
  (`/callback` = already done).
- **Close every tab the app opened once its step is checked**: `closetab <urlPart[,urlPart]>`. It is the
  person's own browser, so tabs pile up across runs. It closes only while the *active* tab's address matches,
  so name the app's own hosts and ports (never a bare `localhost:`, which can match the person's tabs).

## Common mistakes

| Symptom | Fix |
|---|---|
| Clicked ✕ but the dialog stayed | hit another window's `CloseButton`; use `@Anchor/CloseButton` |
| Dialog ignores ✕ | the app is busy (e.g. waiting for a browser callback); finish or cancel that first |
| `'Show Hidden Icons' not found` | the name flips while the overflow is open; `tray` matches `~Hidden Icons` |
| Step "passed" but the person saw an extra window | you asserted one element, not the window set; use `expect` |
| `clickat` hit a different window | another window covered the spot; `front` (or `shot` with a window) right before it |
| The app's own launch didn't take focus | Windows refuses the foreground to a process started from a background shell; that's the OS, not the app. Judge focus only after a real user action (a click on the window) |
| Command exits 2 with "watchdog ... no answer" | the app's UI thread is wedged, usually a modal `invoke` opened; the printed window list shows what's up |
| Antivirus / EDR flags the driver | expected — see below |

## Antivirus / EDR flags the driver

The driver reads other apps through the accessibility API, which an endpoint-protection product can read as
cross-process memory access. An unsigned exe doing that from a temp folder also matches a classic malware
heuristic, which is why the wrapper builds into the clone's `bin/` folder rather than a temp folder.

If a managed machine's security tool still flags it, do **not** disable or evade it — it's a sanctioned control.
Report it to whoever runs that tool as a false positive and ask for the driver's path or hash to be allowed.
