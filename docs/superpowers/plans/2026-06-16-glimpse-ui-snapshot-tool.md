# Glimpse — UI Snapshot Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a generic, headless Avalonia rendering engine + a conventions core so Claude can render UI to PNGs and `Read` them, closing the blind edit→guess→ask loop.

**Architecture:** A framework-free abstractions layer, a reusable `Glimpse.Avalonia` engine that boots headless Avalonia + Skia once per process and turns any `Control` into PNG pixels, and a `Glimpse.Core` that owns all disk I/O (output-dir resolution, `<scene>.<theme>.png` naming, atomic `manifest.json`, macOS `screencapture`). A thin in-repo **scratch console** wires engine + core together and is the end-to-end gate.

**Tech Stack:** .NET 10 (`net10.0`), Avalonia 11.3.x (`Avalonia.Skia`, `Avalonia.Headless`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`), xUnit, System.Text.Json, Central Package Management.

## Global Constraints

> Every task's requirements implicitly include this section. Values copied verbatim from the spec (`docs/superpowers/specs/2026-06-15-ui-snapshot-tool-design.md`).

- **Target framework:** `net10.0` (must match Recorder; can't load a net10 assembly into an older-runtime process).
- **Avalonia version:** engine + all consumers pin the **same Avalonia 11.3.x**; enforced by a startup check that fails fast with the required version.
- **Layering:** the engine returns **pixels (`byte[]`)**, never a path; **Core owns ALL disk I/O** (PNG write + manifest).
- **Process model:** configure the headless platform **once per process** (platform is global, cannot re-init); `SnapshotSession` is single-threaded, one-shot per process, non-reentrant.
- **Naming:** stable latest name `<scene>.<theme>.png`; the **manifest is the source of truth** for whether a PNG is current — the agent must cross-check PNG ↔ manifest, never read a PNG blind.
- **Manifest writes:** atomic (write temp file, then rename); one `manifest.json` **per output dir** so concurrent repos don't clash; entry identity/upsert key is `(scene, theme)`.
- **Output location:** per-repo `.claude/tmp/ui-snapshots/` when a repo root (`.git`) is detected; fall back to `~/.claude/ui-snapshots/` for repo-less ad-hoc design.
- **Names (decided):** `Glimpse.Abstractions`, `Glimpse.Avalonia.Abstractions`, `Glimpse.Avalonia`, `Glimpse.Core`. Repo stays `ui-snapshot-tool`.

## Test Framework & Conventions

- **xUnit** (`[Fact]` / `[Theory]` + `[InlineData]`). Rendering tests use a shared `SnapshotSessionFixture` (xUnit collection fixture) because the headless platform initializes **once per process** — never `new SnapshotSession()` per test.
- Test naming: `Method_Condition_ShouldExpectedBehavior`. Arrange–Act–Assert separated by blank lines. No `#region`.
- `InternalsVisibleTo` exposes internal pure helpers (e.g. `EnvironmentGuard.EnsureCompatible(Version,Version)`) to the test assembly.

## Central Risk (read before Task 4)

The headless rendering pipeline (Tasks 4–8) is the project's main integration risk.

**Verified against the installed Avalonia 11.3.17 packages (triumvirate review, 2026-06-16):**
- ✅ `HeadlessUnitTestSession.StartNew(Type)` reflectively finds + invokes the type's static `BuildAvaloniaApp()` — so `HeadlessSnapshotApp.BuildAvaloniaApp()` with Skia/Inter **is** honored.
- ✅ `Dispatch<T>(Func<T>, CancellationToken)` and the async `Dispatch<T>(Func<Task<T>>, …)` overload both exist.
- ✅ `CaptureRenderedFrame(this TopLevel) → WriteableBitmap?`; `Bitmap.Save(stream)` writes PNG; `frame.PixelSize.{Width,Height}`.
- ✅ `AvaloniaHeadlessPlatform.ForceRenderTimerTick()`; `Dispatcher.UIThread.RunJobs()`; `ILockedFramebuffer.{Address,RowBytes,Size}`.
- ❌ **No render-scaling knob exists** — `IWindowImpl.SetRenderScaling` is not a member, `RenderScaling` is get-only, and `AvaloniaHeadlessPlatformOptions` has no DPI field. **Task 6 scaling is descoped to 1.0** (see Task 6).
- ⚠️ `WithInterFont()` registers Inter as a separate embedded collection — **not** in `FontManager.Current.SystemFonts` (see Task 8).

The tests in each task remain the binding contract; if any other signature differs, adjust the call to satisfy the test — do not change the assertion.

---

## File Structure

```
ui-snapshot-tool/
  Glimpse.sln
  Directory.Build.props            # net10.0, nullable enable, shared props
  Directory.Packages.props         # Central Package Management — pins Avalonia 11.3.x once
  src/
    Glimpse.Abstractions/          # framework-free: SnapshotTheme, SnapshotSize
    Glimpse.Avalonia.Abstractions/ # IScene (returns Avalonia Control)
    Glimpse.Avalonia/              # engine: HeadlessSnapshotApp, SnapshotSession, RenderOptions/Result, guards, analysis
    Glimpse.Core/                  # OutputLocator, Manifest, SnapshotWriter, ManifestMerge, ScreenCapture
  tests/
    Glimpse.Avalonia.Tests/        # engine: render/theme/scaling/determinism/analysis/scene
    Glimpse.Core.Tests/            # locator/manifest/merge/runner/screencapture-args
  samples/
    Glimpse.ScratchConsole/        # in-repo e2e gate: inline scenes + CLI
```

---

## Task 1: Repo scaffold + `Glimpse.Abstractions`

**Files:**
- Create: `Glimpse.sln`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/Glimpse.Abstractions/Glimpse.Abstractions.csproj`
- Create: `src/Glimpse.Abstractions/SnapshotTheme.cs`
- Create: `src/Glimpse.Abstractions/SnapshotSize.cs`
- Test: `tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj` + `tests/Glimpse.Core.Tests/AbstractionsTests.cs`

**Interfaces:**
- Produces: `enum SnapshotTheme { Light, Dark }`; `record SnapshotSize(int Width, int Height, double Scaling = 1.0)`.

- [ ] **Step 1: Create solution + shared build files**

```bash
cd /Users/purin/dev/ui-snapshot-tool
dotnet new sln -n Glimpse
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props` (pins Avalonia 11.3.x once — the "same version" constraint):

```xml
<Project>
  <PropertyGroup>
    <AvaloniaVersion>11.3.17</AvaloniaVersion><!-- pinned to the patch already cached/used by Recorder -->
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Avalonia" Version="$(AvaloniaVersion)" />
    <PackageVersion Include="Avalonia.Skia" Version="$(AvaloniaVersion)" />
    <PackageVersion Include="Avalonia.Headless" Version="$(AvaloniaVersion)" />
    <PackageVersion Include="Avalonia.Themes.Fluent" Version="$(AvaloniaVersion)" />
    <PackageVersion Include="Avalonia.Fonts.Inter" Version="$(AvaloniaVersion)" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test (abstractions exist + defaults)**

Create the test project and `tests/Glimpse.Core.Tests/AbstractionsTests.cs`:

```csharp
using Glimpse.Abstractions;
using Xunit;

namespace Glimpse.Core.Tests;

public class AbstractionsTests
{
    [Fact]
    public void SnapshotSize_DefaultScaling_ShouldBeOne()
    {
        var size = new SnapshotSize(1024, 768);

        Assert.Equal(1.0, size.Scaling);
    }

    [Fact]
    public void SnapshotTheme_ShouldHaveLightAndDark()
    {
        Assert.Equal(0, (int)SnapshotTheme.Light);
        Assert.Equal(1, (int)SnapshotTheme.Dark);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests`
Expected: FAIL — `Glimpse.Abstractions` / `SnapshotSize` does not exist (build error).

- [ ] **Step 4: Create the abstractions project + types**

```bash
dotnet new classlib -n Glimpse.Abstractions -o src/Glimpse.Abstractions -f net10.0
rm src/Glimpse.Abstractions/Class1.cs
dotnet new xunit -n Glimpse.Core.Tests -o tests/Glimpse.Core.Tests -f net10.0
rm tests/Glimpse.Core.Tests/UnitTest1.cs
dotnet sln add src/Glimpse.Abstractions tests/Glimpse.Core.Tests
dotnet add tests/Glimpse.Core.Tests reference src/Glimpse.Abstractions
```

In `tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj`, ensure package refs have **no Version** attribute (CPM supplies it):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio" />
</ItemGroup>
```

Create `src/Glimpse.Abstractions/SnapshotTheme.cs`:

```csharp
namespace Glimpse.Abstractions;

public enum SnapshotTheme
{
    Light = 0,
    Dark = 1,
}
```

Create `src/Glimpse.Abstractions/SnapshotSize.cs`:

```csharp
namespace Glimpse.Abstractions;

/// <summary>Framework-free render size. Scaling is the DPI factor (1.0 = 96 DPI).</summary>
public sealed record SnapshotSize(int Width, int Height, double Scaling = 1.0);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: scaffold Glimpse solution + framework-free abstractions"
```

---

## Task 2: `Glimpse.Avalonia.Abstractions` — `IScene`

**Files:**
- Create: `src/Glimpse.Avalonia.Abstractions/Glimpse.Avalonia.Abstractions.csproj`
- Create: `src/Glimpse.Avalonia.Abstractions/IScene.cs`
- Test: `tests/Glimpse.Avalonia.Tests/Glimpse.Avalonia.Tests.csproj` + `tests/Glimpse.Avalonia.Tests/SceneContractTests.cs`

**Interfaces:**
- Consumes: Avalonia `Control` (package ref).
- Produces: `interface IScene { string Name; Control Build(); Task ReadyAsync() }` with a default `ReadyAsync` returning `Task.CompletedTask`.

- [ ] **Step 1: Write the failing test (default ReadyAsync is completed)**

Create the test project, then `tests/Glimpse.Avalonia.Tests/SceneContractTests.cs`:

```csharp
using Avalonia.Controls;
using Glimpse.Avalonia.Abstractions;
using Xunit;

namespace Glimpse.Avalonia.Tests;

public class SceneContractTests
{
    private sealed class MinimalScene : IScene
    {
        public string Name => "minimal";
        public Control Build() => new TextBlock { Text = "hi" };
    }

    [Fact]
    public void ReadyAsync_WhenNotOverridden_ShouldBeAlreadyCompleted()
    {
        var scene = new MinimalScene();

        Assert.True(scene.ReadyAsync().IsCompletedSuccessfully);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Avalonia.Tests`
Expected: FAIL — `Glimpse.Avalonia.Abstractions` / `IScene` does not exist.

- [ ] **Step 3: Create project + interface**

```bash
dotnet new classlib -n Glimpse.Avalonia.Abstractions -o src/Glimpse.Avalonia.Abstractions -f net10.0
rm src/Glimpse.Avalonia.Abstractions/Class1.cs
dotnet add src/Glimpse.Avalonia.Abstractions package Avalonia
dotnet add src/Glimpse.Avalonia.Abstractions reference src/Glimpse.Abstractions
dotnet new xunit -n Glimpse.Avalonia.Tests -o tests/Glimpse.Avalonia.Tests -f net10.0
rm tests/Glimpse.Avalonia.Tests/UnitTest1.cs
dotnet add tests/Glimpse.Avalonia.Tests package Avalonia
dotnet add tests/Glimpse.Avalonia.Tests reference src/Glimpse.Avalonia.Abstractions
dotnet sln add src/Glimpse.Avalonia.Abstractions tests/Glimpse.Avalonia.Tests
```

In both new `.csproj` files, strip any `Version=` from `<PackageReference>` entries (CPM owns versions).

Create `src/Glimpse.Avalonia.Abstractions/IScene.cs`:

```csharp
using Avalonia.Controls;

namespace Glimpse.Avalonia.Abstractions;

/// <summary>A renderable unit: a view + its (stub-by-default) DataContext.</summary>
public interface IScene
{
    /// <summary>Stable identity; becomes the PNG file stem.</summary>
    string Name { get; }

    /// <summary>Builds the control to render. Default scenes set a hand-built stub DataContext.</summary>
    Control Build();

    /// <summary>Real-VM scenes that load asynchronously await completion here before capture.</summary>
    Task ReadyAsync() => Task.CompletedTask;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add IScene contract (Glimpse.Avalonia.Abstractions)"
```

---

## Task 3: `Glimpse.Avalonia` — environment guard (version/TF fail-fast)

**Files:**
- Create: `src/Glimpse.Avalonia/Glimpse.Avalonia.csproj`
- Create: `src/Glimpse.Avalonia/GlimpseEnvironmentException.cs`
- Create: `src/Glimpse.Avalonia/EnvironmentGuard.cs`
- Modify: `src/Glimpse.Avalonia/Glimpse.Avalonia.csproj` (add `InternalsVisibleTo`)
- Test: `tests/Glimpse.Avalonia.Tests/EnvironmentGuardTests.cs`

**Interfaces:**
- Produces: `class GlimpseEnvironmentException(string) : Exception`; `static class EnvironmentGuard` with `void EnsureCompatible()` (reads real assembly/runtime versions) and `internal void EnsureCompatible(Version avaloniaVersion, Version runtimeVersion)` (pure, testable). Constants `RequiredRuntimeMajor=10`, `RequiredAvaloniaMajor=11`, `RequiredAvaloniaMinor=3`.

- [ ] **Step 1: Write the failing tests (pure guard)**

Create `tests/Glimpse.Avalonia.Tests/EnvironmentGuardTests.cs`:

```csharp
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

public class EnvironmentGuardTests
{
    [Fact]
    public void EnsureCompatible_WithMatchingVersions_ShouldNotThrow()
    {
        EnvironmentGuard.EnsureCompatible(new Version(11, 3, 0), new Version(10, 0, 0));
    }

    [Theory]
    [InlineData(11, 2, 9, 0)]   // wrong Avalonia minor
    [InlineData(12, 0, 10, 0)]  // wrong Avalonia major
    public void EnsureCompatible_WithWrongAvalonia_ShouldThrow(int aMaj, int aMin, int rtMaj, int rtMin)
    {
        Assert.Throws<GlimpseEnvironmentException>(() =>
            EnvironmentGuard.EnsureCompatible(new Version(aMaj, aMin, 0), new Version(rtMaj, rtMin)));
    }

    [Fact]
    public void EnsureCompatible_WithWrongRuntimeMajor_ShouldThrow()
    {
        Assert.Throws<GlimpseEnvironmentException>(() =>
            EnvironmentGuard.EnsureCompatible(new Version(11, 3, 0), new Version(9, 0)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter EnvironmentGuardTests`
Expected: FAIL — `Glimpse.Avalonia` / `EnvironmentGuard` does not exist.

- [ ] **Step 3: Create the engine project + guard**

```bash
dotnet new classlib -n Glimpse.Avalonia -o src/Glimpse.Avalonia -f net10.0
rm src/Glimpse.Avalonia/Class1.cs
dotnet add src/Glimpse.Avalonia package Avalonia
dotnet add src/Glimpse.Avalonia package Avalonia.Skia
dotnet add src/Glimpse.Avalonia package Avalonia.Headless
dotnet add src/Glimpse.Avalonia package Avalonia.Themes.Fluent
dotnet add src/Glimpse.Avalonia package Avalonia.Fonts.Inter
dotnet add src/Glimpse.Avalonia reference src/Glimpse.Abstractions src/Glimpse.Avalonia.Abstractions
dotnet sln add src/Glimpse.Avalonia
dotnet add tests/Glimpse.Avalonia.Tests reference src/Glimpse.Avalonia
```

Strip `Version=` from the new `<PackageReference>` entries.

Add to `src/Glimpse.Avalonia/Glimpse.Avalonia.csproj` inside a `<PropertyGroup>` or `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Glimpse.Avalonia.Tests" />
</ItemGroup>
```

Create `src/Glimpse.Avalonia/GlimpseEnvironmentException.cs`:

```csharp
namespace Glimpse.Avalonia;

/// <summary>Thrown when the runtime or Avalonia version does not match Glimpse's hard requirement.</summary>
public sealed class GlimpseEnvironmentException(string message) : Exception(message);
```

Create `src/Glimpse.Avalonia/EnvironmentGuard.cs`:

```csharp
using Avalonia;

namespace Glimpse.Avalonia;

/// <summary>Fails fast when the host's .NET or Avalonia version is incompatible.</summary>
public static class EnvironmentGuard
{
    public const int RequiredRuntimeMajor = 10;
    public const int RequiredAvaloniaMajor = 11;
    public const int RequiredAvaloniaMinor = 3;

    public static void EnsureCompatible()
        => EnsureCompatible(
            typeof(Application).Assembly.GetName().Version!,
            Environment.Version);

    internal static void EnsureCompatible(Version avaloniaVersion, Version runtimeVersion)
    {
        if (runtimeVersion.Major != RequiredRuntimeMajor)
            throw new GlimpseEnvironmentException(
                $"Glimpse requires .NET {RequiredRuntimeMajor}.x; found {runtimeVersion}.");

        if (avaloniaVersion.Major != RequiredAvaloniaMajor ||
            avaloniaVersion.Minor != RequiredAvaloniaMinor)
            throw new GlimpseEnvironmentException(
                $"Glimpse requires Avalonia {RequiredAvaloniaMajor}.{RequiredAvaloniaMinor}.x; " +
                $"found {avaloniaVersion}.");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter EnvironmentGuardTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add Glimpse.Avalonia env guard (version/TF fail-fast)"
```

---

## Task 4: `Glimpse.Avalonia` — headless render walking skeleton

> **This is the central-risk task.** It proves the headless + Skia pipeline end-to-end: a `Control` renders to a non-empty PNG of the correct logical dimensions. Confirm the Avalonia 11.3.x signatures named in "Central Risk" above; the test is the binding contract.

**Files:**
- Create: `src/Glimpse.Avalonia/HeadlessSnapshotApp.cs`
- Create: `src/Glimpse.Avalonia/RenderOptions.cs`
- Create: `src/Glimpse.Avalonia/RenderResult.cs`
- Create: `src/Glimpse.Avalonia/ISnapshotRenderer.cs`
- Create: `src/Glimpse.Avalonia/GlimpseRenderException.cs`
- Create: `src/Glimpse.Avalonia/CultureScope.cs`
- Create: `src/Glimpse.Avalonia/SnapshotSession.cs`
- Test: `tests/Glimpse.Avalonia.Tests/SnapshotSessionFixture.cs`
- Test: `tests/Glimpse.Avalonia.Tests/RenderSmokeTests.cs`

**Interfaces:**
- Produces:
  - `record RenderOptions(int Width = 1024, int Height = 768, double Scaling = 1.0, ThemeVariant? Theme = null, IBrush? Background = null)`
  - `record RenderResult(byte[] Png, int PixelWidth, int PixelHeight, IReadOnlyList<string> Warnings)`
  - `interface ISnapshotRenderer { RenderResult Render(Func<Control> build, RenderOptions); Task<RenderResult> RenderSceneAsync(IScene, RenderOptions); }` — **the sync `Render` takes a factory** so the control is constructed on the Avalonia UI thread (controls like `TextBlock` throw "Call from invalid thread" if built off-thread). Tests call `Render(() => new X{…}, opts)`.
  - `sealed class SnapshotSession : ISnapshotRenderer, IDisposable` — ctor runs `EnvironmentGuard.EnsureCompatible()` then starts one headless session.
  - `class GlimpseRenderException(string) : Exception`
- Consumes: `EnvironmentGuard` (Task 3), `IScene` (Task 2).

- [ ] **Step 1: Write the failing smoke test (via shared fixture)**

Create `tests/Glimpse.Avalonia.Tests/SnapshotSessionFixture.cs`:

```csharp
using Xunit;

namespace Glimpse.Avalonia.Tests;

public sealed class SnapshotSessionFixture : IDisposable
{
    public global::Glimpse.Avalonia.SnapshotSession Session { get; } = new();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("snapshot")]
public sealed class SnapshotCollection : ICollectionFixture<SnapshotSessionFixture>;
```

Create `tests/Glimpse.Avalonia.Tests/RenderSmokeTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class RenderSmokeTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SimpleControl_ShouldProduceNonEmptyPngOfRequestedSize()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.CornflowerBlue },
            new RenderOptions(Width: 320, Height: 240));

        Assert.NotEmpty(result.Png);
        Assert.Equal(320, result.PixelWidth);
        Assert.Equal(240, result.PixelHeight);
        Assert.Equal(0x89, result.Png[0]); // PNG magic byte
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter RenderSmokeTests`
Expected: FAIL — `SnapshotSession` / `RenderOptions` do not exist.

- [ ] **Step 3: Create the records, interface, exception, culture scope**

Create `src/Glimpse.Avalonia/RenderOptions.cs`:

```csharp
using Avalonia.Media;
using Avalonia.Styling;

namespace Glimpse.Avalonia;

/// <summary>Render parameters. Theme null = Light. Background null = themed default.</summary>
public sealed record RenderOptions(
    int Width = 1024,
    int Height = 768,
    double Scaling = 1.0,
    ThemeVariant? Theme = null,
    IBrush? Background = null);
```

Create `src/Glimpse.Avalonia/RenderResult.cs`:

```csharp
namespace Glimpse.Avalonia;

/// <summary>Engine output: PNG bytes + actual pixel dims + non-fatal warnings. No file paths (Core owns I/O).</summary>
public sealed record RenderResult(
    byte[] Png,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<string> Warnings);
```

Create `src/Glimpse.Avalonia/ISnapshotRenderer.cs`:

```csharp
using Avalonia.Controls;
using Glimpse.Avalonia.Abstractions;

namespace Glimpse.Avalonia;

public interface ISnapshotRenderer
{
    // Factory (not a bare Control) so the control is built on the UI thread inside the dispatch.
    RenderResult Render(Func<Control> build, RenderOptions options);

    Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options);
}
```

Create `src/Glimpse.Avalonia/GlimpseRenderException.cs`:

```csharp
namespace Glimpse.Avalonia;

public sealed class GlimpseRenderException(string message) : Exception(message);
```

Create `src/Glimpse.Avalonia/CultureScope.cs`:

```csharp
using System.Globalization;

namespace Glimpse.Avalonia;

/// <summary>Pins invariant culture for a render so stub dates/numbers are portable; restores on dispose.</summary>
internal readonly struct CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture;
    private readonly CultureInfo _previousUiCulture;

    private CultureScope(CultureInfo previousCulture, CultureInfo previousUiCulture)
    {
        _previousCulture = previousCulture;
        _previousUiCulture = previousUiCulture;
    }

    public static CultureScope Invariant()
    {
        var scope = new CultureScope(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return scope;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
```

- [ ] **Step 4: Create `HeadlessSnapshotApp`**

Create `src/Glimpse.Avalonia/HeadlessSnapshotApp.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace Glimpse.Avalonia;

/// <summary>The headless Avalonia application booted once per process. Skia on, headless drawing off (real pixels).</summary>
public sealed class HeadlessSnapshotApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    // HeadlessUnitTestSession.StartNew(Type) reflectively invokes this static method to get the builder.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessSnapshotApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
```

- [ ] **Step 5: Create `SnapshotSession` (skeleton: render → pixels)**

Create `src/Glimpse.Avalonia/SnapshotSession.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Glimpse.Avalonia.Abstractions;

namespace Glimpse.Avalonia;

/// <summary>
/// One-shot, non-reentrant, single-threaded headless render session.
/// The headless platform is global, so create exactly one per process.
/// </summary>
public sealed class SnapshotSession : ISnapshotRenderer, IDisposable
{
    private const int MaxSettleIterations = 20;

    private static int _instantiated;

    private readonly HeadlessUnitTestSession _session;

    public SnapshotSession()
    {
        // The headless platform is process-global; a second session would throw a confusing,
        // order-dependent error inside StartNew. Fail fast and clearly instead.
        if (Interlocked.Exchange(ref _instantiated, 1) == 1)
            throw new GlimpseRenderException(
                "SnapshotSession is one-shot per process (the Avalonia headless platform is global).");

        EnvironmentGuard.EnsureCompatible();
        _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessSnapshotApp));
    }

    // Sync entry point for tests/simple callers; the async path is canonical. The control is built
    // INSIDE the dispatch so it is constructed on the Avalonia UI thread — many controls (e.g. TextBlock)
    // touch thread-affined services at construction and throw "Call from invalid thread" if built off-thread.
    // Safe from deadlock because the session owns its own dispatcher on a dedicated thread.
    public RenderResult Render(Func<Control> build, RenderOptions options)
        => _session.Dispatch(() => RenderCore(build(), options), CancellationToken.None)
            .GetAwaiter().GetResult();

    public Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options)
        => _session.Dispatch(async () =>
        {
            var control = scene.Build();
            await scene.ReadyAsync();
            return RenderCore(control, options);
        }, CancellationToken.None);

    private static RenderResult RenderCore(Control control, RenderOptions options)
    {
        using var _ = CultureScope.Invariant();

        var window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            SizeToContent = SizeToContent.Manual,
            CanResize = false,
            Width = options.Width,
            Height = options.Height,
            Content = control,
            RequestedThemeVariant = options.Theme ?? ThemeVariant.Light,
        };
        if (options.Background is { } background)
            window.Background = background;

        try
        {
            window.Show();

            var (settled, iterations) = Settle(window);

            var frame = window.CaptureRenderedFrame()
                ?? throw new GlimpseRenderException(
                    "CaptureRenderedFrame returned null — engine needs UseSkia() + UseHeadlessDrawing=false.");

            using var stream = new MemoryStream();
            frame.Save(stream);

            var warnings = new List<string>();
            if (!settled)
                warnings.Add($"settle-cap-hit:{iterations}");
            if (options.Scaling != 1.0)
                warnings.Add("scaling-ignored"); // headless 11.3.x has no DPI knob — see Task 6

            return new RenderResult(stream.ToArray(), frame.PixelSize.Width, frame.PixelSize.Height, warnings);
        }
        finally
        {
            window.Close(); // always tear down so a throw can't leak a window into the next render
        }
    }

    /// <summary>Pumps the dispatcher + render timer until two consecutive frames are byte-identical (settled).</summary>
    private static (bool Settled, int Iterations) Settle(Window window)
    {
        string? previousHash = null;
        for (var i = 1; i <= MaxSettleIterations; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var frame = window.CaptureRenderedFrame();
            if (frame is null)
                continue;

            using var stream = new MemoryStream();
            frame.Save(stream);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
            if (hash == previousHash)
                return (true, i);
            previousHash = hash;
        }

        return (false, MaxSettleIterations);
    }

    public void Dispose() => _session.Dispose();
}
```

> **Settle design note:** stability-based settling doubles as animation handling — a finite transition settles to its final frame; an infinite spinner never stabilizes → `settle-cap-hit` warning. This satisfies the spec's "disable animations / deterministic frame / warn if cap hit" intent without a (non-existent) global animation switch.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter RenderSmokeTests`
Expected: PASS. If `CaptureRenderedFrame` / `StartNew` signatures differ in the installed 11.3.x, adjust the calls to satisfy the assertions; do not change the test.

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "feat: headless render walking skeleton (Control -> PNG pixels)"
```

---

## Task 5: Theme variant rendering + theme-differential test

**Files:**
- Test: `tests/Glimpse.Avalonia.Tests/ThemeDifferentialTests.cs`
- (No new src expected — `RequestedThemeVariant` is already wired in `RenderCore`. This task proves it and guards against "dark silently renders light".)

**Interfaces:**
- Consumes: `SnapshotSession.Render`, `RenderOptions.Theme` (Task 4).

- [ ] **Step 1: Write the failing/guarding test**

Create `tests/Glimpse.Avalonia.Tests/ThemeDifferentialTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Styling;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class ThemeDifferentialTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SameControlLightVsDark_ShouldProduceDifferentPixels()
    {
        // A themed control: FluentTheme paints the window background per variant.
        Control NewControl() => new TextBlock { Text = "Glimpse" };

        var light = fixture.Session.Render(NewControl, new RenderOptions(Theme: ThemeVariant.Light));
        var dark = fixture.Session.Render(NewControl, new RenderOptions(Theme: ThemeVariant.Dark));

        Assert.NotEqual(Convert.ToHexString(light.Png), Convert.ToHexString(dark.Png));
    }
}
```

- [ ] **Step 2: Run test to verify it passes (behavior already wired)**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter ThemeDifferentialTests`
Expected: PASS. If it FAILS (identical pixels), the theme variant isn't reaching the render — in `RenderCore`, ensure `window.RequestedThemeVariant` is set **before** `Show()` and that the settle loop re-runs per render (it does, since each `Render` builds a fresh window). Do not weaken the assertion.

> **Why whole-PNG inequality is a sound theme assertion (not AA noise):** determinism (Task 7) guarantees that the *same* control rendered twice in the *same* theme is byte-identical. So if light vs dark differ at all, the difference is theme-driven, not antialiasing jitter. The full-window themed background (light ≈ near-white, dark ≈ near-black) dominates the frame, so "dark silently renders light" surfaces as *equal* hashes → the test fails correctly.

- [ ] **Step 3: Commit**

```bash
git add tests/Glimpse.Avalonia.Tests/ThemeDifferentialTests.cs
git commit -m "test: theme-differential guard (light vs dark must differ)"
```

---

## Task 6: Scaling — descoped to 1.0 (document the limitation + warning)

> **Decision (triumvirate-verified against Avalonia 11.3.17):** headless Avalonia 11.3.x exposes **no** render-scaling/DPI knob — `IWindowImpl.SetRenderScaling` does not exist, `RenderScaling` is get-only, and `AvaloniaHeadlessPlatformOptions` has no DPI field. Scaling is therefore descoped to 1.0 for v1. `RenderOptions.Scaling` and the manifest `scaling` field remain (forward seam); `RenderCore` already emits a `"scaling-ignored"` warning when `Scaling != 1.0` (added in Task 4) so the manifest never claims a 2x capture it didn't produce.

**Files:**
- Test: `tests/Glimpse.Avalonia.Tests/ScalingTests.cs`

**Interfaces:**
- Consumes: `RenderOptions.Scaling`, `RenderResult.Warnings` (Task 4).

- [ ] **Step 1: Write the skipped contract test + the warning test**

Create `tests/Glimpse.Avalonia.Tests/ScalingTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class ScalingTests(SnapshotSessionFixture fixture)
{
    [Fact(Skip = "Avalonia 11.3.x headless exposes no per-window render scaling; scaling is descoped to 1.0 in v1.")]
    public void Render_WithScalingTwo_ShouldDoubleOutputPixels()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.White },
            new RenderOptions(Width: 100, Height: 80, Scaling: 2.0));

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(160, result.PixelHeight);
    }

    [Fact]
    public void Render_WithNonUnitScaling_ShouldWarnScalingIgnored()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.White },
            new RenderOptions(Width: 100, Height: 80, Scaling: 2.0));

        Assert.Contains("scaling-ignored", result.Warnings);
        Assert.Equal(100, result.PixelWidth); // honestly reports the 1x dimensions actually produced
    }
}
```

- [ ] **Step 2: Run test to verify the warning test passes (skipped test is reported skipped)**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter ScalingTests`
Expected: 1 PASS (`Render_WithNonUnitScaling_ShouldWarnScalingIgnored`), 1 SKIPPED. The `"scaling-ignored"` warning is already emitted by `RenderCore` (Task 4), so no production change is needed here.

> **If you later need true 2x output:** the only 11.3 route is to render at 1x then upscale the bitmap, or wrap content in a `ScaleTransform` (scales *layout*, not DPI — won't give crisp 2x). Both are out of v1 scope. Revisit only on a concrete need.

- [ ] **Step 3: Commit**

```bash
git add tests/Glimpse.Avalonia.Tests/ScalingTests.cs
git commit -m "test: document scaling descope (no headless DPI knob in Avalonia 11.3.x)"
```

---

## Task 7: Determinism test (render N times → identical bytes)

> The most important property for a screenshot tool.

**Files:**
- Test: `tests/Glimpse.Avalonia.Tests/DeterminismTests.cs`

**Interfaces:**
- Consumes: `SnapshotSession.Render` (Task 4).

- [ ] **Step 1: Write the failing/guarding test**

Create `tests/Glimpse.Avalonia.Tests/DeterminismTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class DeterminismTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SameSceneThreeTimes_ShouldProduceByteIdenticalPng()
    {
        Control NewControl() => new TextBlock { Text = "deterministic", Foreground = Brushes.Black };

        var results = Enumerable.Range(0, 3)
            .Select(_ => fixture.Session.Render(NewControl, new RenderOptions(Width: 200, Height: 100)))
            .ToList();

        // Guard: determinism must not be satisfied by three identical *blank* frames.
        Assert.DoesNotContain(results[0].Warnings, w => w.StartsWith("single-color-frame"));

        var hashes = results.Select(r => Convert.ToHexString(r.Png)).Distinct().ToList();
        Assert.Single(hashes);
    }

    [Fact]
    public void Render_PerpetuallyAnimatingControl_ShouldWarnSettleCapHit()
    {
        // An indeterminate ProgressBar animates forever, so no two consecutive frames match → cap hit.
        var result = fixture.Session.Render(
            () => new ProgressBar { IsIndeterminate = true, Width = 160, Height = 8 },
            new RenderOptions(Width: 200, Height: 100));

        Assert.Contains(result.Warnings, w => w.StartsWith("settle-cap-hit"));
    }
}
```

> **If the perpetual-animation test does NOT warn** (i.e. the headless render-timer doesn't advance the indeterminate animation, so the frame stabilizes), the settle loop is still correct — convert this to a documented limitation (`[Fact(Skip = "headless render timer does not drive indeterminate animations")]`) rather than forcing it. The determinism test above is the load-bearing assertion; this one guards the cap-hit warning path opportunistically.

> **Coverage limitation (documented, by design):** this is **in-process** determinism only — all three renders share one warm session/Skia/font cache. It catches per-render nondeterminism (animation clock, layout race, unpinned culture). Cross-process / cross-run byte-stability is *not* automated; it relies on the pinned invariant culture, the stability-based settle loop, and the pinned Avalonia 11.3.17 version.

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter DeterminismTests`
Expected: PASS. If it FAILS (non-identical), the most likely cause is an unsettled animation/clock — confirm the settle loop reaches stability (no `settle-cap-hit` warning) and that culture is pinned. Investigate via superpowers:systematic-debugging rather than loosening the assertion.

- [ ] **Step 3: Commit**

```bash
git add tests/Glimpse.Avalonia.Tests/DeterminismTests.cs
git commit -m "test: determinism guard (repeated renders are byte-identical)"
```

---

## Task 8: Frame analysis warnings (blank/single-color + font/tofu proxy)

**Files:**
- Create: `src/Glimpse.Avalonia/FrameAnalysis.cs`
- Modify: `src/Glimpse.Avalonia/SnapshotSession.cs` (call analysis; add font-resolution warning)
- Test: `tests/Glimpse.Avalonia.Tests/FrameAnalysisTests.cs`

**Interfaces:**
- Produces: `static class FrameAnalysis` with `IReadOnlyList<string> Inspect(WriteableBitmap frame)` → returns `["single-color-frame:<hex>"]` when every sampled pixel is identical, else empty.
- Consumes: capture frame in `RenderCore` (Task 4).

- [ ] **Step 1: Write the failing test**

Create `tests/Glimpse.Avalonia.Tests/FrameAnalysisTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class FrameAnalysisTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SolidColorControl_ShouldWarnSingleColorFrame()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.Red },
            new RenderOptions(Width: 64, Height: 64));

        Assert.Contains(result.Warnings, w => w.StartsWith("single-color-frame"));
    }

    [Fact]
    public void Render_ControlWithText_ShouldNotWarnSingleColorFrame()
    {
        var result = fixture.Session.Render(
            () => new TextBlock { Text = "content", Foreground = Brushes.Black, FontSize = 32 },
            new RenderOptions(Width: 200, Height: 80));

        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
    }

    [Fact]
    public void Render_NormalRender_ShouldNotWarnFontInterUnresolved()
    {
        // WithInterFont() registers Inter; the resolution check must NOT false-positive (see Task 8 caveat).
        var result = fixture.Session.Render(
            () => new TextBlock { Text = "content", Foreground = Brushes.Black },
            new RenderOptions(Width: 200, Height: 80));

        Assert.DoesNotContain("font-inter-unresolved", result.Warnings);
    }
}
```

> **Single-color negative-case note:** the heuristic samples a stride-8 grid, so the text case uses a large `FontSize` to guarantee glyphs cross sample points (avoids a flaky false "single-color" on sparse antialiased text).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter FrameAnalysisTests`
Expected: FAIL — no `single-color-frame` warning emitted.

- [ ] **Step 3: Create `FrameAnalysis`**

Create `src/Glimpse.Avalonia/FrameAnalysis.cs`:

```csharp
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Glimpse.Avalonia;

/// <summary>Heuristics that flag misleading frames an agent might trust (blank / single-color).</summary>
internal static class FrameAnalysis
{
    private const int SampleStride = 8; // sample a grid, not every pixel

    public static IReadOnlyList<string> Inspect(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var first = ReadPixel(buffer, 0, 0);
        var uniform = true;

        for (var y = 0; y < buffer.Size.Height && uniform; y += SampleStride)
            for (var x = 0; x < buffer.Size.Width; x += SampleStride)
                if (ReadPixel(buffer, x, y) != first)
                {
                    uniform = false;
                    break;
                }

        return uniform
            ? [$"single-color-frame:{first:X8}"]
            : [];
    }

    private static unsafe uint ReadPixel(ILockedFramebuffer buffer, int x, int y)
    {
        var row = (byte*)buffer.Address + (y * buffer.RowBytes);
        return ((uint*)row)[x]; // Bgra8888 / Rgba8888 — exact channel order irrelevant for equality
    }
}
```

Enable `unsafe` in `src/Glimpse.Avalonia/Glimpse.Avalonia.csproj`:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

- [ ] **Step 4: Wire analysis + font check into `RenderCore`**

In `src/Glimpse.Avalonia/SnapshotSession.cs`, inside the `try` block, extend the existing `warnings` list (which already holds `settle-cap-hit` / `scaling-ignored`) with frame analysis and a font-resolution check, before the `return`:

```csharp
warnings.AddRange(FrameAnalysis.Inspect(frame));
if (!FontManager.Current.TryGetGlyphTypeface(new Typeface("Inter"), out _))
    warnings.Add("font-inter-unresolved"); // tofu proxy: requested font did not resolve to a real typeface
```

Add `using Avalonia.Media;` for `FontManager` / `Typeface` at the top of the file.

> **Font-resolution caveat:** `WithInterFont()` registers Inter as a separate embedded collection, **not** into `FontManager.Current.SystemFonts` — so a `SystemFonts` scan would be a guaranteed false positive. If `new Typeface("Inter")` does not resolve against the embedded collection in 11.3.x (the absent-warning test in Step 5 will reveal this), switch to `new Typeface(FontFamily.Parse("fonts:Inter#Inter"))`, or drop the check entirely (real tofu detection is out of v1 scope). The binding contract is the Step-5 test: **no `font-inter-unresolved` on a normal render.**

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter FrameAnalysisTests`
Expected: PASS (3 tests). If `Render_NormalRender_ShouldNotWarnFontInterUnresolved` FAILS, `new Typeface("Inter")` isn't resolving the embedded font — switch the check to `FontManager.Current.TryGetGlyphTypeface(new Typeface(FontFamily.Parse("fonts:Inter#Inter")), out _)` or drop the font check (per the Task 8 caveat). Do not weaken the assertion.

- [ ] **Step 6: Commit**

```bash
git add src/Glimpse.Avalonia tests/Glimpse.Avalonia.Tests/FrameAnalysisTests.cs
git commit -m "feat: frame-analysis warnings (single-color + font/tofu proxy)"
```

---

## Task 9: `IScene` rendering + async-ready test

**Files:**
- Test: `tests/Glimpse.Avalonia.Tests/SceneRenderTests.cs`
- (No new src — `RenderSceneAsync` is wired in Task 4. This proves `ReadyAsync()` is awaited before capture.)

**Interfaces:**
- Consumes: `SnapshotSession.RenderSceneAsync`, `IScene` (Tasks 2, 4).

- [ ] **Step 1: Write the failing/guarding test**

Create `tests/Glimpse.Avalonia.Tests/SceneRenderTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class SceneRenderTests(SnapshotSessionFixture fixture)
{
    private sealed class AsyncScene : TextBlock, IScene
    {
        public string Name => "async";

        public Control Build() => this;

        public async Task ReadyAsync()
        {
            await Task.Delay(10);
            Text = "loaded"; // only present if the engine awaited ReadyAsync before capture
            Foreground = Brushes.Black;
        }
    }

    [Fact]
    public async Task RenderSceneAsync_WithAsyncReadyScene_ShouldCaptureLoadedContent()
    {
        var scene = new AsyncScene();

        var result = await fixture.Session.RenderSceneAsync(scene, new RenderOptions(Width: 200, Height: 80));

        Assert.NotEmpty(result.Png);
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
    }
}
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Avalonia.Tests --filter SceneRenderTests`
Expected: PASS — the rendered frame is non-blank because `ReadyAsync` ran (setting visible text) before the settle/capture. If it FAILS with a `single-color-frame` warning, the engine captured before awaiting `ReadyAsync`; fix `RenderSceneAsync` to `await scene.ReadyAsync()` before `RenderCore`.

- [ ] **Step 3: Commit**

```bash
git add tests/Glimpse.Avalonia.Tests/SceneRenderTests.cs
git commit -m "test: scene render awaits ReadyAsync before capture"
```

---

## Task 10: `Glimpse.Core` — output-dir resolution

**Files:**
- Create: `src/Glimpse.Core/Glimpse.Core.csproj`
- Create: `src/Glimpse.Core/OutputLocator.cs`
- Test: `tests/Glimpse.Core.Tests/OutputLocatorTests.cs`

**Interfaces:**
- Produces: `static class OutputLocator` — `string Resolve(string startDirectory, string homeDirectory)` (pure, testable) + `string Resolve()` (uses CWD + user profile). Constants `RepoSubPath = ".claude/tmp/ui-snapshots"`, `CentralSubPath = ".claude/ui-snapshots"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/OutputLocatorTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class OutputLocatorTests
{
    [Fact]
    public void Resolve_InsideRepo_ShouldReturnPerRepoPath()
    {
        var repo = Directory.CreateTempSubdirectory("glimpse-repo-");
        Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(repo.FullName, "src", "app"));

        var result = OutputLocator.Resolve(nested.FullName, "/home/user");

        Assert.Equal(Path.Combine(repo.FullName, ".claude/tmp/ui-snapshots"), result);
    }

    [Fact]
    public void Resolve_OutsideRepo_ShouldReturnCentralPath()
    {
        var loose = Directory.CreateTempSubdirectory("glimpse-loose-");

        var result = OutputLocator.Resolve(loose.FullName, "/home/user");

        Assert.Equal(Path.Combine("/home/user", ".claude/ui-snapshots"), result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter OutputLocatorTests`
Expected: FAIL — `Glimpse.Core` / `OutputLocator` does not exist.

- [ ] **Step 3: Create project + locator**

```bash
dotnet new classlib -n Glimpse.Core -o src/Glimpse.Core -f net10.0
rm src/Glimpse.Core/Class1.cs
dotnet add src/Glimpse.Core reference src/Glimpse.Abstractions
dotnet sln add src/Glimpse.Core
dotnet add tests/Glimpse.Core.Tests reference src/Glimpse.Core
```

Create `src/Glimpse.Core/OutputLocator.cs`:

```csharp
namespace Glimpse.Core;

/// <summary>Resolves where PNGs + manifest are written: per-repo when in a repo, central otherwise.</summary>
public static class OutputLocator
{
    public const string RepoSubPath = ".claude/tmp/ui-snapshots";
    public const string CentralSubPath = ".claude/ui-snapshots";

    public static string Resolve()
        => Resolve(
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string Resolve(string startDirectory, string homeDirectory)
    {
        var repoRoot = FindRepoRoot(startDirectory);
        return repoRoot is not null
            ? Path.Combine(repoRoot, RepoSubPath)
            : Path.Combine(homeDirectory, CentralSubPath);
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;

        return null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests --filter OutputLocatorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: Glimpse.Core output-dir resolution (per-repo vs central)"
```

---

## Task 11: `Glimpse.Core` — manifest types, merge, writer

**Files:**
- Create: `src/Glimpse.Core/Manifest.cs` (records: `SceneEntry`, `Failure`, `Manifest`)
- Create: `src/Glimpse.Core/ManifestMerge.cs`
- Create: `src/Glimpse.Core/SnapshotWriter.cs`
- Test: `tests/Glimpse.Core.Tests/ManifestMergeTests.cs`
- Test: `tests/Glimpse.Core.Tests/SnapshotWriterTests.cs`

**Interfaces:**
- Produces:
  - `record SceneEntry(string Scene, string Theme, string Path, int Width, int Height, double Scaling, string Status, DateTimeOffset RenderedAt, IReadOnlyList<string> Warnings)`
  - `record Failure(string Scene, string Theme, string Error)`
  - `record Manifest(int SchemaVersion, string RunId, DateTimeOffset GeneratedAt, IReadOnlyList<SceneEntry> Scenes, IReadOnlyList<Failure> Failures)`
  - `static class ManifestMerge { Manifest Merge(Manifest? existing, IReadOnlyList<SceneEntry> entries, IReadOnlyList<Failure> failures, string runId, DateTimeOffset generatedAt) }` — upsert by `(Scene, Theme)`, sorted, `SchemaVersion = 1`.
  - `sealed class SnapshotWriter(string outputDir)` — `string SavePng(string scene, string theme, byte[] png)` (returns `<scene>.<theme>.png`), `Manifest? ReadExisting()`, `void WriteManifest(Manifest manifest)` (atomic temp-then-rename).

- [ ] **Step 1: Write the failing merge tests**

Create `tests/Glimpse.Core.Tests/ManifestMergeTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ManifestMergeTests
{
    private static SceneEntry Entry(string scene, string theme, string status = "ok") =>
        new(scene, theme, $"{scene}.{theme}.png", 100, 100, 1.0, status,
            DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void Merge_WithNoExisting_ShouldContainNewEntriesSorted()
    {
        var result = ManifestMerge.Merge(null,
            [Entry("Beta", "dark"), Entry("Alpha", "light")], [], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal("run-1", result.RunId);
        Assert.Collection(result.Scenes,
            e => Assert.Equal("Alpha", e.Scene),
            e => Assert.Equal("Beta", e.Scene));
    }

    [Fact]
    public void Merge_WithSameKey_ShouldReplaceEntryAndCarryOthers()
    {
        var existing = ManifestMerge.Merge(null,
            [Entry("Login", "dark", "ok"), Entry("Home", "light", "ok")], [], "run-1", DateTimeOffset.UnixEpoch);

        var result = ManifestMerge.Merge(existing,
            [Entry("Login", "dark", "failed")], [], "run-2", DateTimeOffset.UnixEpoch);

        Assert.Equal("run-2", result.RunId);
        Assert.Equal("failed", result.Scenes.Single(e => e.Scene == "Login" && e.Theme == "dark").Status);
        Assert.Contains(result.Scenes, e => e.Scene == "Home"); // carried over
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter ManifestMergeTests`
Expected: FAIL — `Manifest` / `ManifestMerge` do not exist.

- [ ] **Step 3: Create manifest records + merge**

Create `src/Glimpse.Core/Manifest.cs`:

```csharp
namespace Glimpse.Core;

public sealed record SceneEntry(
    string Scene,
    string Theme,
    string Path,
    int Width,
    int Height,
    double Scaling,
    string Status,                       // "ok" | "failed" | "stale"
    DateTimeOffset RenderedAt,
    IReadOnlyList<string> Warnings);

public sealed record Failure(string Scene, string Theme, string Error);

public sealed record Manifest(
    int SchemaVersion,
    string RunId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SceneEntry> Scenes,
    IReadOnlyList<Failure> Failures);
```

Create `src/Glimpse.Core/ManifestMerge.cs`:

```csharp
namespace Glimpse.Core;

/// <summary>Upserts this run's entries into the prior manifest, keyed by (Scene, Theme).</summary>
public static class ManifestMerge
{
    public const int CurrentSchemaVersion = 1;

    public static Manifest Merge(
        Manifest? existing,
        IReadOnlyList<SceneEntry> entries,
        IReadOnlyList<Failure> failures,
        string runId,
        DateTimeOffset generatedAt)
    {
        var replacedKeys = entries.Select(e => (e.Scene, e.Theme)).ToHashSet();
        var carried = existing?.Scenes.Where(e => !replacedKeys.Contains((e.Scene, e.Theme)))
            ?? Enumerable.Empty<SceneEntry>();

        var merged = carried
            .Concat(entries)
            .OrderBy(e => e.Scene, StringComparer.Ordinal)
            .ThenBy(e => e.Theme, StringComparer.Ordinal)
            .ToList();

        return new Manifest(CurrentSchemaVersion, runId, generatedAt, merged, failures);
    }
}
```

- [ ] **Step 4: Run merge test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests --filter ManifestMergeTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Write the failing writer tests**

Create `tests/Glimpse.Core.Tests/SnapshotWriterTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class SnapshotWriterTests
{
    [Fact]
    public void SavePng_ShouldWriteSceneThemeFileAndReturnRelativeName()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-out-").FullName;
        var writer = new SnapshotWriter(dir);

        var name = writer.SavePng("LoginWindow", "dark", [1, 2, 3]);

        Assert.Equal("LoginWindow.dark.png", name);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dir, name)));
    }

    [Fact]
    public void WriteManifest_ThenReadExisting_ShouldRoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-out-").FullName;
        var writer = new SnapshotWriter(dir);
        var manifest = ManifestMerge.Merge(null,
            [new SceneEntry("Home", "light", "Home.light.png", 10, 10, 1.0, "ok", DateTimeOffset.UnixEpoch, [])],
            [], "run-1", DateTimeOffset.UnixEpoch);

        writer.WriteManifest(manifest);
        var read = writer.ReadExisting();

        Assert.NotNull(read);
        Assert.Equal("run-1", read!.RunId);
        Assert.Single(read.Scenes);
        Assert.False(File.Exists(Path.Combine(dir, "manifest.json.run-1.tmp"))); // temp cleaned up
    }
}
```

- [ ] **Step 6: Run writer test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter SnapshotWriterTests`
Expected: FAIL — `SnapshotWriter` does not exist.

- [ ] **Step 7: Create `SnapshotWriter`**

Create `src/Glimpse.Core/SnapshotWriter.cs`:

```csharp
using System.Text.Json;

namespace Glimpse.Core;

/// <summary>Owns all disk I/O: PNG files + atomic manifest. One instance per output dir.</summary>
public sealed class SnapshotWriter(string outputDir)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private string ManifestPath => Path.Combine(outputDir, "manifest.json");

    public string SavePng(string scene, string theme, byte[] png)
    {
        Directory.CreateDirectory(outputDir);
        var name = $"{scene}.{theme}.png";
        File.WriteAllBytes(Path.Combine(outputDir, name), png);
        return name;
    }

    public Manifest? ReadExisting()
        => File.Exists(ManifestPath)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath), JsonOptions)
            : null;

    public void WriteManifest(Manifest manifest)
    {
        Directory.CreateDirectory(outputDir);
        var temp = Path.Combine(outputDir, $"manifest.json.{manifest.RunId}.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temp, ManifestPath, overwrite: true); // atomic replace
    }
}
```

- [ ] **Step 8: Run writer test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests --filter SnapshotWriterTests`
Expected: PASS (2 tests).

- [ ] **Step 9: Commit**

```bash
git add src/Glimpse.Core tests/Glimpse.Core.Tests
git commit -m "feat: manifest records + merge + atomic SnapshotWriter"
```

---

## Task 12: `Glimpse.ScratchConsole` — CLI + runner (in-repo pipeline + resource gate)

> This is the in-repo gate: inline scenes → real engine → real Core write → manifest. It proves the headless pipeline, Core I/O, **and** (via a runtime-XAML `DynamicResource` scene) that themed resource resolution works against the registered FluentTheme. It does **not** exercise an external app's `App.axaml`/merged dictionaries — that's the deferred Recorder console (see "Deferred"). The runner is testable with a fake renderer; `Program.cs` stays thin.
>
> **Test-project layout (avoids dragging Avalonia into the framework-free Core tests):** CLI + runner + gate tests live in a dedicated `Glimpse.ScratchConsole.Tests`. `Glimpse.Core.Tests` stays pure (Abstractions + Core only); `Glimpse.Avalonia.Tests` does not reference the console.

**Files:**
- Create: `samples/Glimpse.ScratchConsole/Glimpse.ScratchConsole.csproj`
- Create: `samples/Glimpse.ScratchConsole/CliOptions.cs`
- Create: `samples/Glimpse.ScratchConsole/SnapshotRunner.cs`
- Create: `samples/Glimpse.ScratchConsole/Scenes/SampleScenes.cs`
- Create: `samples/Glimpse.ScratchConsole/Program.cs`
- Test: `tests/Glimpse.ScratchConsole.Tests/Glimpse.ScratchConsole.Tests.csproj`
- Test: `tests/Glimpse.ScratchConsole.Tests/CliOptionsTests.cs`
- Test: `tests/Glimpse.ScratchConsole.Tests/SnapshotRunnerTests.cs` (fake `ISnapshotRenderer`)
- Test: `tests/Glimpse.ScratchConsole.Tests/SceneSmokeTests.cs` (real session, own fixture)
- Test: `tests/Glimpse.ScratchConsole.Tests/EndToEndGateTests.cs` (real session, own fixture)

**Interfaces:**
- Produces:
  - `record CliOptions(IReadOnlyList<string> Scenes, IReadOnlyList<SnapshotTheme> Themes, string? OutDir)` + `static CliOptions Parse(string[] args)` (`--scene <name|all>`, `--theme light|dark|both`, `--out <dir>`; defaults: `all`, `both`, null).
  - `sealed class SnapshotRunner(ISnapshotRenderer renderer, SnapshotWriter writer)` + `Task<int> RunAsync(IReadOnlyList<IScene> scenes, IReadOnlyList<SnapshotTheme> themes, string runId, DateTimeOffset now)` — returns exit code: `0` all ok, `1` some failed, `2` empty/duplicate scene names. Throws nothing to the caller; maps duplicates/empty to exit `2`.
- Consumes: `SnapshotSession` (Task 4), `SnapshotWriter` + `ManifestMerge` (Task 11), `OutputLocator` (Task 10), `IScene` (Task 2), `SnapshotTheme` (Task 1).

- [ ] **Step 1: Write the failing CLI-parse tests**

Create `tests/Glimpse.ScratchConsole.Tests/CliOptionsTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.ScratchConsole;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_WithNoArgs_ShouldDefaultToAllScenesBothThemes()
    {
        var options = CliOptions.Parse([]);

        Assert.Equal(["all"], options.Scenes);
        Assert.Equal([SnapshotTheme.Light, SnapshotTheme.Dark], options.Themes);
        Assert.Null(options.OutDir);
    }

    [Theory]
    [InlineData("light", new[] { SnapshotTheme.Light })]
    [InlineData("dark", new[] { SnapshotTheme.Dark })]
    [InlineData("both", new[] { SnapshotTheme.Light, SnapshotTheme.Dark })]
    public void Parse_WithThemeArg_ShouldSelectExpectedThemes(string theme, SnapshotTheme[] expected)
    {
        var options = CliOptions.Parse(["--theme", theme]);

        Assert.Equal(expected, options.Themes);
    }

    [Fact]
    public void Parse_WithSceneAndOut_ShouldCaptureBoth()
    {
        var options = CliOptions.Parse(["--scene", "LoginWindow", "--out", "/tmp/x"]);

        Assert.Equal(["LoginWindow"], options.Scenes);
        Assert.Equal("/tmp/x", options.OutDir);
    }

    [Fact]
    public void Parse_WithFlagMissingItsValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--scene"]));
    }

    [Fact]
    public void Parse_WithUnknownTheme_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--theme", "sepia"]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.ScratchConsole.Tests --filter CliOptionsTests`
Expected: FAIL — `Glimpse.ScratchConsole` / `CliOptions` do not exist (project not created yet).

- [ ] **Step 3: Create the console + test projects + `CliOptions`**

```bash
dotnet new console -n Glimpse.ScratchConsole -o samples/Glimpse.ScratchConsole -f net10.0
dotnet add samples/Glimpse.ScratchConsole reference src/Glimpse.Avalonia src/Glimpse.Core
dotnet sln add samples/Glimpse.ScratchConsole
dotnet new xunit -n Glimpse.ScratchConsole.Tests -o tests/Glimpse.ScratchConsole.Tests -f net10.0
rm tests/Glimpse.ScratchConsole.Tests/UnitTest1.cs
dotnet add tests/Glimpse.ScratchConsole.Tests reference samples/Glimpse.ScratchConsole
dotnet sln add tests/Glimpse.ScratchConsole.Tests
```

Strip `Version=` from the new test project's `<PackageReference>` entries (CPM owns versions).

Create `samples/Glimpse.ScratchConsole/CliOptions.cs`:

```csharp
using Glimpse.Abstractions;

namespace Glimpse.ScratchConsole;

public sealed record CliOptions(
    IReadOnlyList<string> Scenes,
    IReadOnlyList<SnapshotTheme> Themes,
    string? OutDir)
{
    public static CliOptions Parse(string[] args)
    {
        var scene = "all";
        var theme = "both";
        string? outDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scene": scene = NextValue(args, ref i); break;
                case "--theme": theme = NextValue(args, ref i); break;
                case "--out": outDir = NextValue(args, ref i); break;
                default: throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }
        }

        return new CliOptions([scene], ThemesFor(theme), outDir);
    }

    private static string NextValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for '{args[i]}'.");
        return args[++i];
    }

    private static IReadOnlyList<SnapshotTheme> ThemesFor(string theme) => theme switch
    {
        "light" => [SnapshotTheme.Light],
        "dark" => [SnapshotTheme.Dark],
        "both" => [SnapshotTheme.Light, SnapshotTheme.Dark],
        _ => throw new ArgumentException($"Unknown --theme '{theme}' (expected light|dark|both)."),
    };
}
```

- [ ] **Step 4: Run CLI test to verify it passes**

Run: `dotnet test tests/Glimpse.ScratchConsole.Tests --filter CliOptionsTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Write the failing runner tests (fake renderer)**

Create `tests/Glimpse.ScratchConsole.Tests/SnapshotRunnerTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Abstractions;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Glimpse.Core;
using Glimpse.ScratchConsole;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

public class SnapshotRunnerTests
{
    private sealed class StubScene(string name) : IScene
    {
        public string Name => name;
        public Control Build() => new Border { Background = Brushes.White };
    }

    private sealed class FakeRenderer(Func<IScene, RenderResult> render) : ISnapshotRenderer
    {
        public RenderResult Render(Func<Control> build, RenderOptions options) => throw new NotSupportedException();
        public Task<RenderResult> RenderSceneAsync(IScene scene, RenderOptions options) => Task.FromResult(render(scene));
    }

    private static SnapshotRunner NewRunner(Func<IScene, RenderResult> render, out string dir)
    {
        dir = Directory.CreateTempSubdirectory("glimpse-run-").FullName;
        return new SnapshotRunner(new FakeRenderer(render), new SnapshotWriter(dir));
    }

    [Fact]
    public async Task RunAsync_AllScenesSucceed_ShouldReturnZeroAndWriteManifest()
    {
        var runner = NewRunner(_ => new RenderResult([0x89, 1], 10, 10, []), out var dir);

        var exit = await runner.RunAsync([new StubScene("Home")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(dir, "Home.light.png")));
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        Assert.Equal("ok", manifest.Scenes.Single().Status);
        Assert.Empty(manifest.Failures);
    }

    [Fact]
    public async Task RunAsync_WhenASceneThrows_ShouldMarkEntryFailedAndRecordFailure()
    {
        var runner = NewRunner(
            scene => scene.Name == "Bad" ? throw new InvalidOperationException("boom")
                                          : new RenderResult([0x89], 10, 10, []),
            out var dir);

        var exit = await runner.RunAsync(
            [new StubScene("Good"), new StubScene("Bad")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(1, exit);
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        // The safety property: a failed scene must NOT masquerade as ok in the manifest.
        Assert.Equal("failed", manifest.Scenes.Single(e => e.Scene == "Bad").Status);
        Assert.Equal("ok", manifest.Scenes.Single(e => e.Scene == "Good").Status);
        Assert.Contains(manifest.Failures, f => f.Scene == "Bad");
    }

    [Fact]
    public async Task RunAsync_WhenAPreviouslyOkSceneFails_ShouldFlipEntryToFailed()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-run-").FullName;
        var writer = new SnapshotWriter(dir);

        var ok = new SnapshotRunner(new FakeRenderer(_ => new RenderResult([0x89], 10, 10, [])), writer);
        await ok.RunAsync([new StubScene("Flaky")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        var fails = new SnapshotRunner(new FakeRenderer(_ => throw new InvalidOperationException("boom")), writer);
        await fails.RunAsync([new StubScene("Flaky")], [SnapshotTheme.Light], "run-2", DateTimeOffset.UnixEpoch);

        var manifest = writer.ReadExisting()!;
        Assert.Equal("failed", manifest.Scenes.Single(e => e.Scene == "Flaky").Status); // not stale "ok"
    }

    [Fact]
    public async Task RunAsync_WithDuplicateSceneNames_ShouldReturnTwo()
    {
        var runner = NewRunner(_ => new RenderResult([0x89], 10, 10, []), out _);

        var exit = await runner.RunAsync(
            [new StubScene("Dup"), new StubScene("Dup")], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task RunAsync_WithNoScenes_ShouldReturnTwo()
    {
        var runner = NewRunner(_ => new RenderResult([0x89], 10, 10, []), out _);

        var exit = await runner.RunAsync([], [SnapshotTheme.Light], "run-1", DateTimeOffset.UnixEpoch);

        Assert.Equal(2, exit);
    }
}
```

- [ ] **Step 6: Run runner test to verify it fails**

Run: `dotnet test tests/Glimpse.ScratchConsole.Tests --filter SnapshotRunnerTests`
Expected: FAIL — `SnapshotRunner` does not exist.

- [ ] **Step 7: Create `SnapshotRunner`**

Create `samples/Glimpse.ScratchConsole/SnapshotRunner.cs`:

```csharp
using Avalonia.Styling;
using Glimpse.Abstractions;
using Glimpse.Avalonia;
using Glimpse.Avalonia.Abstractions;
using Glimpse.Core;

namespace Glimpse.ScratchConsole;

/// <summary>Renders each (scene, theme), writes PNGs + a merged manifest. Fail-soft per scene.</summary>
public sealed class SnapshotRunner(ISnapshotRenderer renderer, SnapshotWriter writer)
{
    private const int ExitOk = 0;
    private const int ExitSomeFailed = 1;
    private const int ExitBadInput = 2;

    public async Task<int> RunAsync(
        IReadOnlyList<IScene> scenes,
        IReadOnlyList<SnapshotTheme> themes,
        string runId,
        DateTimeOffset now)
    {
        if (scenes.Count == 0)
            return ExitBadInput; // empty discovery is an error (spec §4.4)

        if (scenes.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != scenes.Count)
            return ExitBadInput; // duplicate names would collide on <scene>.png

        var entries = new List<SceneEntry>();
        var failures = new List<Failure>();

        foreach (var scene in scenes)
            foreach (var theme in themes)
                await RenderOneAsync(scene, theme, now, entries, failures);

        writer.WriteManifest(ManifestMerge.Merge(writer.ReadExisting(), entries, failures, runId, now));
        return failures.Count > 0 ? ExitSomeFailed : ExitOk;
    }

    private async Task RenderOneAsync(
        IScene scene, SnapshotTheme theme, DateTimeOffset now,
        List<SceneEntry> entries, List<Failure> failures)
    {
        var themeName = theme.ToString().ToLowerInvariant();
        try
        {
            var options = new RenderOptions(Theme: ThemeVariantFor(theme));
            var result = await renderer.RenderSceneAsync(scene, options);
            var path = writer.SavePng(scene.Name, themeName, result.Png);
            entries.Add(new SceneEntry(scene.Name, themeName, path,
                result.PixelWidth, result.PixelHeight, options.Scaling, "ok", now, result.Warnings));
        }
        catch (Exception ex)
        {
            failures.Add(new Failure(scene.Name, themeName, ex.Message));
            entries.Add(new SceneEntry(scene.Name, themeName, $"{scene.Name}.{themeName}.png",
                0, 0, 1.0, "failed", now, [ex.Message]));
        }
    }

    private static ThemeVariant ThemeVariantFor(SnapshotTheme theme)
        => theme == SnapshotTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
}
```

- [ ] **Step 8: Run runner test to verify it passes**

Run: `dotnet test tests/Glimpse.ScratchConsole.Tests --filter SnapshotRunnerTests`
Expected: PASS (6 tests).

- [ ] **Step 9: Add sample scenes + wire `Program.cs`**

Create `samples/Glimpse.ScratchConsole/Scenes/SampleScenes.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Glimpse.Avalonia.Abstractions;

namespace Glimpse.ScratchConsole.Scenes;

/// <summary>Inline scenes — no app references, deterministic stub content.</summary>
public static class SampleScenes
{
    public static IReadOnlyList<IScene> All => [new HelloScene(), new CardScene(), new ThemedControlsScene()];

    private sealed class HelloScene : IScene
    {
        public string Name => "Hello";
        public Control Build() => new TextBlock
        {
            Text = "Hello, Glimpse",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private sealed class CardScene : IScene
    {
        public string Name => "Card";
        public Control Build() => new Border
        {
            Margin = new Thickness(40),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Background = Brushes.SlateBlue,
            Child = new TextBlock { Text = "Card body", Foreground = Brushes.White },
        };
    }

    // Templated FluentTheme controls (Button/TextBox) pull their control templates + theme brushes
    // from the registered theme. This is the in-repo proxy for the resource-resolution risk the spec
    // flags (§4.2): if theme resources weren't applied, these render unstyled or throw at template build.
    private sealed class ThemedControlsScene : IScene
    {
        public string Name => "ThemedControls";
        public Control Build() => new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBox { Watermark = "Username", Width = 220 },
                new Button { Content = "Sign in" },
            },
        };
    }
}
```

Replace `samples/Glimpse.ScratchConsole/Program.cs`:

```csharp
using Glimpse.Avalonia;
using Glimpse.Core;
using Glimpse.ScratchConsole;
using Glimpse.ScratchConsole.Scenes;

var options = CliOptions.Parse(args);
var outDir = options.OutDir ?? OutputLocator.Resolve();

var scenes = options.Scenes is ["all"]
    ? SampleScenes.All
    : SampleScenes.All.Where(s => options.Scenes.Contains(s.Name)).ToList();

using var session = new SnapshotSession();
var runner = new SnapshotRunner(session, new SnapshotWriter(outDir));

var exit = await runner.RunAsync(scenes, options.Themes, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);

Console.WriteLine($"Glimpse wrote to: {outDir} (exit {exit})");
return exit;
```

- [ ] **Step 10: Write the failing scene-smoke + automated e2e gate tests**

These use the **real** `SnapshotSession`, so add a once-per-process fixture in this assembly.

Create `tests/Glimpse.ScratchConsole.Tests/RealSessionFixture.cs`:

```csharp
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

public sealed class RealSessionFixture : IDisposable
{
    public SnapshotSession Session { get; } = new();

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("real-session")]
public sealed class RealSessionCollection : ICollectionFixture<RealSessionFixture>;
```

Create `tests/Glimpse.ScratchConsole.Tests/SceneSmokeTests.cs`:

```csharp
using Glimpse.Avalonia;
using Glimpse.ScratchConsole.Scenes;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

[Collection("real-session")]
public class SceneSmokeTests(RealSessionFixture fixture)
{
    [Fact]
    public void SampleScenes_ShouldHaveUniqueNames()
    {
        var names = SampleScenes.All.Select(s => s.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task EverySampleScene_ShouldRenderWithoutThrowingOrBlank()
    {
        foreach (var scene in SampleScenes.All)
        {
            var result = await fixture.Session.RenderSceneAsync(scene, new RenderOptions());

            Assert.NotEmpty(result.Png);
            Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
        }
    }
}
```

Create `tests/Glimpse.ScratchConsole.Tests/EndToEndGateTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.Core;
using Glimpse.ScratchConsole.Scenes;
using Xunit;

namespace Glimpse.ScratchConsole.Tests;

[Collection("real-session")]
public class EndToEndGateTests(RealSessionFixture fixture)
{
    [Fact]
    public async Task FullPipeline_RealSessionToManifest_ShouldWriteAllScenesOk()
    {
        var dir = Directory.CreateTempSubdirectory("glimpse-e2e-").FullName;
        var runner = new SnapshotRunner(fixture.Session, new SnapshotWriter(dir));

        var exit = await runner.RunAsync(
            SampleScenes.All, [SnapshotTheme.Light, SnapshotTheme.Dark], "gate-run", DateTimeOffset.UnixEpoch);

        Assert.Equal(0, exit);
        var manifest = new SnapshotWriter(dir).ReadExisting()!;
        Assert.Equal(SampleScenes.All.Count * 2, manifest.Scenes.Count);
        Assert.All(manifest.Scenes, e =>
        {
            Assert.Equal("ok", e.Status);
            Assert.True(File.Exists(Path.Combine(dir, e.Path)));
            Assert.True(new FileInfo(Path.Combine(dir, e.Path)).Length > 0);
        });
        Assert.Empty(manifest.Failures);
    }
}
```

- [ ] **Step 11: Run the smoke + gate tests to verify they pass**

Run: `dotnet test tests/Glimpse.ScratchConsole.Tests`
Expected: PASS (all CLI + runner + smoke + gate tests). This is the automated gate — the whole graph (real session → real writer → manifest on disk) is now asserted, not eyeballed.

- [ ] **Step 12: (Optional) Run the console for a visual sanity check**

Run:
```bash
dotnet run --project samples/Glimpse.ScratchConsole -- --scene all --theme both --out /tmp/glimpse-e2e
```
Expected: exit 0; `/tmp/glimpse-e2e/` contains `Hello.{light,dark}.png`, `Card.{light,dark}.png`, `ThemedControls.{light,dark}.png`, and `manifest.json` with 6 `ok` scenes. `Read` `ThemedControls.dark.png` to confirm the Button/TextBox are themed (proves resource/template resolution).

- [ ] **Step 13: Commit**

```bash
git add samples tests Glimpse.sln
git commit -m "feat: scratch console + runner + automated pipeline/resource gate"
```

---

## Task 13: `Glimpse.Core` — macOS `screencapture` wrapper

**Files:**
- Create: `src/Glimpse.Core/ScreenCaptureCommand.cs` (pure arg builder)
- Create: `src/Glimpse.Core/ScreenCapture.cs` (process runner)
- Test: `tests/Glimpse.Core.Tests/ScreenCaptureCommandTests.cs`

**Interfaces:**
- Produces:
  - `enum CaptureMode { FullScreen, Window, Interactive }`
  - `static class ScreenCaptureCommand { IReadOnlyList<string> BuildArgs(CaptureMode mode, string outputPath, int? windowId = null) }`
  - `sealed class ScreenCapture { Task<string> CaptureAsync(CaptureMode mode, string outputPath, int? windowId = null) }` — runs `screencapture`, throws `GlimpseCaptureException` on non-zero exit or missing output file.

- [ ] **Step 1: Write the failing arg-builder tests**

Create `tests/Glimpse.Core.Tests/ScreenCaptureCommandTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ScreenCaptureCommandTests
{
    [Fact]
    public void BuildArgs_FullScreen_ShouldBeSilentCapture()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.FullScreen, "/tmp/s.png");

        Assert.Equal(["-x", "/tmp/s.png"], args);
    }

    [Fact]
    public void BuildArgs_Window_ShouldTargetWindowIdWithoutShadow()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.Window, "/tmp/w.png", windowId: 42);

        Assert.Equal(["-x", "-o", "-l42", "/tmp/w.png"], args);
    }

    [Fact]
    public void BuildArgs_Interactive_ShouldUseInteractiveFlag()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.Interactive, "/tmp/i.png");

        Assert.Equal(["-i", "/tmp/i.png"], args);
    }

    [Fact]
    public void BuildArgs_WindowWithoutId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenCaptureCommand.BuildArgs(CaptureMode.Window, "/tmp/w.png"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter ScreenCaptureCommandTests`
Expected: FAIL — `ScreenCaptureCommand` does not exist.

- [ ] **Step 3: Create the arg builder + runner**

Create `src/Glimpse.Core/ScreenCaptureCommand.cs`:

```csharp
namespace Glimpse.Core;

public enum CaptureMode
{
    FullScreen,
    Window,
    Interactive,
}

/// <summary>Builds macOS `screencapture` arguments. Pure — unit-testable without invoking the binary.</summary>
public static class ScreenCaptureCommand
{
    public static IReadOnlyList<string> BuildArgs(CaptureMode mode, string outputPath, int? windowId = null)
        => mode switch
        {
            CaptureMode.FullScreen => ["-x", outputPath],
            CaptureMode.Interactive => ["-i", outputPath],
            CaptureMode.Window when windowId is { } id => ["-x", "-o", $"-l{id}", outputPath],
            CaptureMode.Window => throw new ArgumentException("Window capture requires a windowId."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
}
```

Create `src/Glimpse.Core/ScreenCapture.cs`:

```csharp
using System.Diagnostics;

namespace Glimpse.Core;

public sealed class GlimpseCaptureException(string message) : Exception(message);

/// <summary>Thin wrapper over macOS `screencapture`. Needs Screen Recording (TCC) permission for window/screen modes.</summary>
public sealed class ScreenCapture
{
    public async Task<string> CaptureAsync(CaptureMode mode, string outputPath, int? windowId = null)
    {
        var startInfo = new ProcessStartInfo("screencapture") { RedirectStandardError = true };
        foreach (var arg in ScreenCaptureCommand.BuildArgs(mode, outputPath, windowId))
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new GlimpseCaptureException("Failed to start 'screencapture' (macOS only).");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
            throw new GlimpseCaptureException(
                $"screencapture failed (exit {process.ExitCode}): {await process.StandardError.ReadToEndAsync()}");

        return outputPath;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests --filter ScreenCaptureCommandTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Final full-suite run + commit**

```bash
dotnet test
git add src/Glimpse.Core tests/Glimpse.Core.Tests
git commit -m "feat: macOS screencapture wrapper (arg builder + process runner)"
```
Expected: all tests green.

---

## Deferred (explicitly out of this plan)

- **Recorder preview console** (spec §4.5 / build-order step 5): separate repo (`~/dev/Recorder` under `Tools/`), blocked on the cross-repo distribution decision (spec §9.5). Its own plan once `Glimpse.Avalonia` is referenceable cross-repo. This is the *real* app-graph e2e gate; the scratch console gates the engine + theme-resource resolution within this repo.
- **`IScene.Styles` / app-resource-attachment seam** (spec §4.2): an external app's `App.axaml` `Styles`/merged dictionaries are not auto-applied to the rendered `Window`. Deferred to the Recorder-console plan — adding a *defaulted* interface member (`IReadOnlyList<IStyle> Styles => []`) later is **non-breaking** to existing scenes, so designing it now buys nothing (YAGNI). The `ThemedControls` scene (Task 12) already proves FluentTheme template/brush resolution in-repo.
- Window-id enumeration helper (`CGWindowList`/AppleScript) for `screencapture -l` — noted in spec §4.4; add when the live-capture flow needs it.
- Skill packaging (`snapshot-ui` / `glimpse`), web/mobile modules, visual-diff baselines, Windows/Linux capture providers (spec §8).

## Self-Review (against the spec)

- **§2 Goals** — snapshot core (Tasks 10–11, 13), Avalonia engine (Tasks 3–9), per-project console pattern (Task 12 scratch console; Recorder console deferred), CLI surface (Task 12). ✅
- **§4.1 Core** — output dir (Task 10), `<scene>.<theme>.png` naming (Task 11 `SavePng`), atomic manifest + upsert by `(scene,theme)` + `failed` status (Tasks 11–12). ✅
- **§4.2 Engine** — configure-once + one-shot guard (Task 4 fixture/ctor), settle loop (Task 4), scaling **descoped** with `scaling-ignored` warning (Tasks 4, 6), theme variants (Task 5), culture pin (Task 4 `CultureScope`), capture→pixels with `try/finally` teardown (Task 4). ✅
- **§4.3 Scene** — `IScene` + `ReadyAsync` (Tasks 2, 9), stub VM default + templated/themed scene (Task 12 sample scenes). ✅
- **§6 Error handling** — null-frame cause (Task 4), single-color heuristic + font/tofu proxy (Task 8), version/TF fail-fast (Task 3), empty/duplicate scene errors (Task 12), failed-entry-not-stale (Task 12 runner tests). ✅
- **§7 Testing** — determinism + non-blank guard + settle-cap (Task 7), engine integration (Task 4), scaling descope documented (Task 6), theme differential (Task 5), font/tofu absent-warning (Task 8), async-ready (Task 9), **real scene smoke** (Task 12), **automated** in-repo pipeline/resource gate (Task 12). ✅
- **Two contract layers** (§4.3): `Glimpse.Abstractions` framework-free vs `Glimpse.Avalonia.Abstractions` (`Control`) — Tasks 1, 2. Test projects honor the split: `Glimpse.Core.Tests` never references Avalonia. ✅
- **Type consistency:** `ISnapshotRenderer.RenderSceneAsync` (Tasks 4, 9, 12), `SnapshotWriter.SavePng/ReadExisting/WriteManifest` (Tasks 11, 12), `ManifestMerge.Merge` signature (Tasks 11, 12), exit codes 0/1/2 (Task 12) — consistent across tasks. ✅
