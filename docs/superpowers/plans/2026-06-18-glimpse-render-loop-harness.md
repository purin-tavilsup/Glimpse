# Glimpse Render-Loop Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Glimpse into a renderer-agnostic visual-feedback harness — any UI or diagram → PNG → the agent Reads it → critique → improve → repeat — driven by a generic "command → PNG" renderer contract.

**Architecture:** A thin pipeline (`resolve renderer → run its command → analyse the PNG → record + report`) in `Glimpse.Core`, plus a `Glimpse.Capture` CLI and a `glimpse` skill that packages the loop. Renderers are config (a command template + the tool they need), so adding one is data, not code. The existing Avalonia engine is left untouched.

**Tech Stack:** C# / .NET 10, xUnit, SkiaSharp 2.88.9 (PNG decode), external render tools (`mmdc`, `dot`, `d2`, Chrome, `screencapture`).

## Global Constraints

- Target framework: `net10.0` (set in `Directory.Build.props` — do not re-declare per project).
- `Nullable` enabled; `ImplicitUsings` enabled; `TreatWarningsAsErrors` true — code must be warning-clean.
- Central package management: versions live in `Directory.Packages.props`; project files use `<PackageReference Include="X" />` with no `Version=`.
- Tests: xUnit (`[Fact]`/`[Theory]` + `[InlineData]`), naming `Method_Condition_ShouldExpectedBehavior`, Arrange-Act-Assert separated by blank lines, no `#region`.
- Style: file-scoped namespaces; PascalCase public, camelCase locals/params; records for immutable data; primary constructors for simple DI.
- New framework-free code goes in `Glimpse.Core` (no Avalonia references). Do not modify `Glimpse.Avalonia*`.
- SkiaSharp pinned to `2.88.9` (matches Avalonia 11.3.17's transitive Skia — avoids a second native build).

---

## File Structure

**Created:**
- `src/Glimpse.Core/PngAnalysis.cs` — decode a PNG, report dimensions + blank/single-color warnings.
- `src/Glimpse.Core/RendererSpec.cs` — the renderer contract record + built-in defaults.
- `src/Glimpse.Core/RendererRegistry.cs` — resolve a spec by explicit name or source extension; optional JSON override.
- `src/Glimpse.Core/ToolLocator.cs` — find a tool on PATH (+ Chrome special-case), install hints.
- `src/Glimpse.Core/RenderCommandBuilder.cs` — pure placeholder substitution → executable + args.
- `src/Glimpse.Core/ProcessRunner.cs` — `IProcessRunner` + real implementation.
- `src/Glimpse.Core/RenderEngine.cs` — the pipeline: resolve tool → run → analyse → outcome.
- `tools/Glimpse.Capture/Glimpse.Capture.csproj`, `Program.cs`, `CaptureOptions.cs` — the CLI.
- `tests/Glimpse.Core.Tests/PngAnalysisTests.cs`, `RendererRegistryTests.cs`, `RenderCommandBuilderTests.cs`, `ToolLocatorTests.cs`, `RenderEngineTests.cs`.
- `tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj`, `CaptureOptionsTests.cs`, `EndToEndGateTests.cs`.
- `.claude/skills/glimpse/SKILL.md` — the loop skill.

**Modified:**
- `Directory.Packages.props` — add `SkiaSharp` 2.88.9.
- `src/Glimpse.Core/Glimpse.Core.csproj` — add SkiaSharp PackageReference.
- `src/Glimpse.Core/SnapshotWriter.cs` — add `PngPath(name)` + `Prune(...)` for stable-name harness output.
- `Glimpse.slnx` — register `tools/Glimpse.Capture` + its test project.

---

### Task 1: PNG analyser in `Glimpse.Core`

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Glimpse.Core/Glimpse.Core.csproj`
- Create: `src/Glimpse.Core/PngAnalysis.cs`
- Test: `tests/Glimpse.Core.Tests/PngAnalysisTests.cs`

**Interfaces:**
- Consumes: nothing (leaf).
- Produces:
  - `record PngInspection(int Width, int Height, IReadOnlyList<string> Warnings)`
  - `static class PngAnalysis { static PngInspection Inspect(string pngPath); static PngInspection Inspect(byte[] png); }`
  - Warning vocabulary: `"single-color-frame:RRGGBBAA"` (8 hex digits, uppercase) when the sampled grid is one color; empty list otherwise.

- [ ] **Step 1: Add the SkiaSharp package version**

In `Directory.Packages.props`, add inside the existing `<ItemGroup>`:

```xml
<PackageVersion Include="SkiaSharp" Version="2.88.9" />
```

- [ ] **Step 2: Reference SkiaSharp from Glimpse.Core**

In `src/Glimpse.Core/Glimpse.Core.csproj`, add a new `<ItemGroup>`:

```xml
<ItemGroup>
  <PackageReference Include="SkiaSharp" />
</ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Glimpse.Core.Tests/PngAnalysisTests.cs`:

```csharp
using Glimpse.Core;
using SkiaSharp;
using Xunit;

namespace Glimpse.Core.Tests;

public class PngAnalysisTests
{
    private static byte[] SolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] TwoColorPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, width / 2f, height, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Inspect_WithSolidColor_ShouldReturnSingleColorWarning()
    {
        var png = SolidPng(64, 48, new SKColor(0x11, 0x22, 0x33, 0xFF));

        var result = PngAnalysis.Inspect(png);

        Assert.Equal(64, result.Width);
        Assert.Equal(48, result.Height);
        Assert.Contains("single-color-frame:112233FF", result.Warnings);
    }

    [Fact]
    public void Inspect_WithMultipleColors_ShouldReturnNoWarnings()
    {
        var png = TwoColorPng(64, 48);

        var result = PngAnalysis.Inspect(png);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Inspect_WithMissingFile_ShouldThrowFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => PngAnalysis.Inspect("/no/such/file.png"));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter PngAnalysisTests`
Expected: FAIL — `PngAnalysis` / `PngInspection` do not exist.

- [ ] **Step 5: Implement the analyser**

Create `src/Glimpse.Core/PngAnalysis.cs`:

```csharp
using SkiaSharp;

namespace Glimpse.Core;

public sealed record PngInspection(int Width, int Height, IReadOnlyList<string> Warnings);

/// <summary>Decodes a PNG and flags misleading frames an agent might trust (blank / single-color).</summary>
public static class PngAnalysis
{
    private const int SampleStride = 8; // sample a grid, not every pixel

    public static PngInspection Inspect(string pngPath)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException("PNG not found.", pngPath);

        return Inspect(File.ReadAllBytes(pngPath));
    }

    public static PngInspection Inspect(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png)
            ?? throw new ArgumentException("Bytes are not a decodable image.", nameof(png));

        var first = bitmap.GetPixel(0, 0);
        var uniform = true;

        for (var y = 0; y < bitmap.Height && uniform; y += SampleStride)
            for (var x = 0; x < bitmap.Width; x += SampleStride)
                if (bitmap.GetPixel(x, y) != first)
                {
                    uniform = false;
                    break;
                }

        var warnings = uniform
            ? new[] { $"single-color-frame:{first.Red:X2}{first.Green:X2}{first.Blue:X2}{first.Alpha:X2}" }
            : Array.Empty<string>();

        return new PngInspection(bitmap.Width, bitmap.Height, warnings);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter PngAnalysisTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Glimpse.Core/Glimpse.Core.csproj src/Glimpse.Core/PngAnalysis.cs tests/Glimpse.Core.Tests/PngAnalysisTests.cs
git commit -m "feat(core): PNG analyser (dimensions + single-color warning)"
```

---

### Task 2: Renderer spec + built-in defaults + registry

**Files:**
- Create: `src/Glimpse.Core/RendererSpec.cs`
- Create: `src/Glimpse.Core/RendererRegistry.cs`
- Test: `tests/Glimpse.Core.Tests/RendererRegistryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record RendererSpec(string Name, string Tool, IReadOnlyList<string> Args, IReadOnlyList<string> SourceExtensions)`
  - `static class BuiltInRenderers { static IReadOnlyList<RendererSpec> All { get; } }` — names: `mermaid`, `graphviz`, `d2`, `web`, `app`.
  - `class RendererRegistry` with `static RendererRegistry Default()`, `static RendererRegistry LoadOrDefault(string? configPath)`, and `RendererSpec Resolve(string? name, string? sourcePath)` (explicit name wins; else infer by lowercased extension; throws `ArgumentException` if neither resolves).

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/RendererRegistryTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class RendererRegistryTests
{
    private readonly RendererRegistry registry = RendererRegistry.Default();

    [Theory]
    [InlineData("diagram.mmd", "mermaid")]
    [InlineData("graph.dot", "graphviz")]
    [InlineData("graph.gv", "graphviz")]
    [InlineData("layout.d2", "d2")]
    [InlineData("page.html", "web")]
    [InlineData("page.HTM", "web")]
    public void Resolve_ByExtension_ShouldPickRenderer(string sourcePath, string expectedName)
    {
        var spec = registry.Resolve(null, sourcePath);

        Assert.Equal(expectedName, spec.Name);
    }

    [Fact]
    public void Resolve_WithExplicitName_ShouldOverrideExtension()
    {
        var spec = registry.Resolve("graphviz", "diagram.mmd");

        Assert.Equal("graphviz", spec.Name);
    }

    [Fact]
    public void Resolve_WithUnknownExtensionAndNoName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => registry.Resolve(null, "notes.txt"));
    }

    [Fact]
    public void Resolve_WithUnknownName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => registry.Resolve("plantuml", "x.puml"));
    }

    [Fact]
    public void All_ShouldContainTheFiveBuiltIns()
    {
        var names = BuiltInRenderers.All.Select(r => r.Name).ToList();

        Assert.Equal(new[] { "mermaid", "graphviz", "d2", "web", "app" }, names);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RendererRegistryTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement the spec + built-ins**

Create `src/Glimpse.Core/RendererSpec.cs`:

```csharp
namespace Glimpse.Core;

/// <summary>
/// A renderer is a command that writes a PNG to a path. <see cref="Args"/> are passed to
/// <see cref="Tool"/> with placeholders substituted: {source} {out} {width} {height} {theme} {windowid}.
/// </summary>
public sealed record RendererSpec(
    string Name,
    string Tool,
    IReadOnlyList<string> Args,
    IReadOnlyList<string> SourceExtensions);

public static class BuiltInRenderers
{
    public static IReadOnlyList<RendererSpec> All { get; } =
    [
        new("mermaid", "mmdc",
            ["-i", "{source}", "-o", "{out}", "-t", "{theme}", "-w", "{width}"],
            [".mmd", ".mermaid"]),

        new("graphviz", "dot",
            ["-Tpng", "{source}", "-o", "{out}"],
            [".dot", ".gv"]),

        new("d2", "d2",
            ["{source}", "{out}"],
            [".d2"]),

        new("web", "chrome",
            ["--headless", "--disable-gpu", "--screenshot={out}", "--window-size={width},{height}", "{source}"],
            [".html", ".htm"]),

        new("app", "screencapture",
            ["-x", "-o", "-l{windowid}", "{out}"],
            []),
    ];
}
```

- [ ] **Step 4: Implement the registry**

Create `src/Glimpse.Core/RendererRegistry.cs`:

```csharp
using System.Text.Json;

namespace Glimpse.Core;

/// <summary>Resolves a <see cref="RendererSpec"/> by explicit name or source extension.</summary>
public sealed class RendererRegistry
{
    private readonly IReadOnlyList<RendererSpec> specs;

    public RendererRegistry(IReadOnlyList<RendererSpec> specs) => this.specs = specs;

    public static RendererRegistry Default() => new(BuiltInRenderers.All);

    /// <summary>Built-ins overlaid with any same-named specs from a JSON array file (if present).</summary>
    public static RendererRegistry LoadOrDefault(string? configPath)
    {
        if (configPath is null || !File.Exists(configPath))
            return Default();

        var overrides = JsonSerializer.Deserialize<List<RendererSpec>>(
            File.ReadAllText(configPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var byName = BuiltInRenderers.All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var spec in overrides)
            byName[spec.Name] = spec;

        return new RendererRegistry(byName.Values.ToList());
    }

    public RendererSpec Resolve(string? name, string? sourcePath)
    {
        if (name is not null)
            return specs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown renderer '{name}'. Known: {string.Join(", ", specs.Select(s => s.Name))}.");

        var ext = Path.GetExtension(sourcePath ?? "").ToLowerInvariant();
        return specs.FirstOrDefault(s => s.SourceExtensions.Contains(ext))
            ?? throw new ArgumentException($"No renderer for extension '{ext}'. Pass --renderer explicitly.");
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RendererRegistryTests`
Expected: PASS (9 tests including the Theory rows).

- [ ] **Step 6: Commit**

```bash
git add src/Glimpse.Core/RendererSpec.cs src/Glimpse.Core/RendererRegistry.cs tests/Glimpse.Core.Tests/RendererRegistryTests.cs
git commit -m "feat(core): renderer spec, built-in defaults, registry"
```

---

### Task 3: Tool locator (PATH lookup + Chrome + install hints)

**Files:**
- Create: `src/Glimpse.Core/ToolLocator.cs`
- Test: `tests/Glimpse.Core.Tests/ToolLocatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static class ToolLocator { static string? Resolve(string tool); static string InstallHint(string tool); }`
  - `Resolve` returns an absolute executable path or null. `"chrome"` resolves to the macOS Chrome binary if PATH has nothing.
  - `InstallHint` returns an actionable one-liner for known tools, or a generic message.

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/ToolLocatorTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void Resolve_WithRealShellTool_ShouldReturnAbsolutePath()
    {
        // 'ls' exists on every macOS/Linux dev box and is on PATH.
        var path = ToolLocator.Resolve("ls");

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Resolve_WithNonexistentTool_ShouldReturnNull()
    {
        Assert.Null(ToolLocator.Resolve("definitely-not-a-real-tool-xyz"));
    }

    [Theory]
    [InlineData("mmdc", "mermaid-cli")]
    [InlineData("dot", "graphviz")]
    [InlineData("d2", "d2")]
    public void InstallHint_ForKnownTool_ShouldMentionThePackage(string tool, string fragment)
    {
        var hint = ToolLocator.InstallHint(tool);

        Assert.Contains(fragment, hint);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter ToolLocatorTests`
Expected: FAIL — `ToolLocator` does not exist.

- [ ] **Step 3: Implement the locator**

Create `src/Glimpse.Core/ToolLocator.cs`:

```csharp
using System.Diagnostics;

namespace Glimpse.Core;

/// <summary>Finds render tools on PATH (with a Chrome special-case) and gives install hints.</summary>
public static class ToolLocator
{
    private const string MacChrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

    private static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>
    {
        ["mmdc"] = "mermaid-cli not found — install: npm i -g @mermaid-js/mermaid-cli",
        ["dot"] = "graphviz not found — install: brew install graphviz",
        ["d2"] = "d2 not found — install: brew install d2",
        ["chrome"] = "Chrome not found — install Google Chrome, or set a {chrome} override.",
        ["screencapture"] = "screencapture is macOS-only and ships with the OS.",
    };

    public static string? Resolve(string tool)
    {
        if (tool == "chrome")
            return WhichOrNull("google-chrome") ?? WhichOrNull("chromium") ?? (File.Exists(MacChrome) ? MacChrome : null);

        return WhichOrNull(tool);
    }

    public static string InstallHint(string tool)
        => Hints.TryGetValue(tool, out var hint) ? hint : $"'{tool}' not found on PATH.";

    private static string? WhichOrNull(string tool)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/which", tool) { RedirectStandardOutput = true };
        using var process = Process.Start(startInfo);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Length > 0 ? output : null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter ToolLocatorTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Glimpse.Core/ToolLocator.cs tests/Glimpse.Core.Tests/ToolLocatorTests.cs
git commit -m "feat(core): tool locator with PATH lookup, Chrome path, install hints"
```

---

### Task 4: Render command builder (pure substitution)

**Files:**
- Create: `src/Glimpse.Core/RenderCommandBuilder.cs`
- Test: `tests/Glimpse.Core.Tests/RenderCommandBuilderTests.cs`

**Interfaces:**
- Consumes: `RendererSpec` (Task 2), `SnapshotTheme` (from `Glimpse.Abstractions`).
- Produces:
  - `record RenderRequest(string Source, string OutputPath, int Width, int Height, SnapshotTheme Theme, int? WindowId = null)`
  - `record RenderCommand(string Executable, IReadOnlyList<string> Args)`
  - `static class RenderCommandBuilder { static RenderCommand Build(RendererSpec spec, RenderRequest request, string executable); }`
  - Substitutes `{source} {out} {width} {height} {theme} {windowid}` inside each arg. `{theme}` → `"dark"` for Dark, `"default"` for Light (mermaid vocabulary). Throws `ArgumentException` if an arg references `{windowid}` but `WindowId` is null.

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/RenderCommandBuilderTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class RenderCommandBuilderTests
{
    private static RenderRequest Request(SnapshotTheme theme = SnapshotTheme.Light, int? windowId = null) =>
        new("in.mmd", "/out/diagram.png", 1280, 800, theme, windowId);

    [Fact]
    public void Build_ForMermaid_ShouldSubstituteSourceOutWidthAndTheme()
    {
        var spec = new RendererSpec("mermaid", "mmdc",
            ["-i", "{source}", "-o", "{out}", "-t", "{theme}", "-w", "{width}"], [".mmd"]);

        var command = RenderCommandBuilder.Build(spec, Request(SnapshotTheme.Dark), "/usr/bin/mmdc");

        Assert.Equal("/usr/bin/mmdc", command.Executable);
        Assert.Equal(new[] { "-i", "in.mmd", "-o", "/out/diagram.png", "-t", "dark", "-w", "1280" }, command.Args);
    }

    [Fact]
    public void Build_ForLightTheme_ShouldMapToDefault()
    {
        var spec = new RendererSpec("mermaid", "mmdc", ["-t", "{theme}"], [".mmd"]);

        var command = RenderCommandBuilder.Build(spec, Request(SnapshotTheme.Light), "mmdc");

        Assert.Equal(new[] { "-t", "default" }, command.Args);
    }

    [Fact]
    public void Build_ForWindowIdArg_WithWindowId_ShouldSubstitute()
    {
        var spec = new RendererSpec("app", "screencapture", ["-l{windowid}", "{out}"], []);

        var command = RenderCommandBuilder.Build(spec, Request(windowId: 42), "screencapture");

        Assert.Equal(new[] { "-l42", "/out/diagram.png" }, command.Args);
    }

    [Fact]
    public void Build_ForWindowIdArg_WithoutWindowId_ShouldThrow()
    {
        var spec = new RendererSpec("app", "screencapture", ["-l{windowid}", "{out}"], []);

        Assert.Throws<ArgumentException>(() => RenderCommandBuilder.Build(spec, Request(), "screencapture"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RenderCommandBuilderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement the builder**

Create `src/Glimpse.Core/RenderCommandBuilder.cs`:

```csharp
using Glimpse.Abstractions;

namespace Glimpse.Core;

public sealed record RenderRequest(
    string Source,
    string OutputPath,
    int Width,
    int Height,
    SnapshotTheme Theme,
    int? WindowId = null);

public sealed record RenderCommand(string Executable, IReadOnlyList<string> Args);

/// <summary>Pure: fills a spec's arg placeholders from a request. No I/O.</summary>
public static class RenderCommandBuilder
{
    public static RenderCommand Build(RendererSpec spec, RenderRequest request, string executable)
    {
        var args = spec.Args.Select(arg => Substitute(arg, request)).ToList();
        return new RenderCommand(executable, args);
    }

    private static string Substitute(string arg, RenderRequest request)
    {
        if (arg.Contains("{windowid}") && request.WindowId is null)
            throw new ArgumentException("This renderer needs a window id, but none was supplied.");

        return arg
            .Replace("{source}", request.Source)
            .Replace("{out}", request.OutputPath)
            .Replace("{width}", request.Width.ToString())
            .Replace("{height}", request.Height.ToString())
            .Replace("{theme}", request.Theme == SnapshotTheme.Dark ? "dark" : "default")
            .Replace("{windowid}", request.WindowId?.ToString() ?? "");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RenderCommandBuilderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Glimpse.Core/RenderCommandBuilder.cs tests/Glimpse.Core.Tests/RenderCommandBuilderTests.cs
git commit -m "feat(core): render command builder (placeholder substitution)"
```

---

### Task 5: Process runner + render engine (the pipeline)

**Files:**
- Create: `src/Glimpse.Core/ProcessRunner.cs`
- Create: `src/Glimpse.Core/RenderEngine.cs`
- Test: `tests/Glimpse.Core.Tests/RenderEngineTests.cs`

**Interfaces:**
- Consumes: `RendererSpec`, `RendererRegistry`, `ToolLocator`, `RenderCommandBuilder`, `RenderRequest`, `RenderCommand`, `PngAnalysis`.
- Produces:
  - `record ProcessResult(int ExitCode, string StdErr)`
  - `interface IProcessRunner { Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args); }`
  - `class ProcessRunner : IProcessRunner` (real `Process.Start`).
  - `record RenderOutcome(string Status, int ExitCode, string OutputPath, int Width, int Height, IReadOnlyList<string> Warnings)` — `Status` is `"ok"` or `"failed"`.
  - `class RenderEngine(IProcessRunner runner)` with `Task<RenderOutcome> RenderAsync(RendererSpec spec, RenderRequest request)`. Resolves the tool (throws `GlimpseRenderToolException` with an install hint if missing), runs the command, then: non-zero exit or missing output → `("failed", 2, …)` with a `render-failed:` warning; otherwise analyses the PNG → exit `1` if warnings else `0`, status `"ok"`.
  - `class GlimpseRenderToolException(string tool, string hint) : Exception` exposing `Tool` and `Hint`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/RenderEngineTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.Core;
using SkiaSharp;
using Xunit;

namespace Glimpse.Core.Tests;

public class RenderEngineTests
{
    // Uses 'ls' as a real, always-present tool so ToolLocator resolves; the fake runner
    // simulates the tool's effect (writing a PNG or not) without actually invoking it.
    private static RendererSpec LsSpec() => new("fake", "ls", ["{out}"], [".x"]);

    private sealed class FakeRunner(int exitCode, Action onRun) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
        {
            onRun();
            return Task.FromResult(new ProcessResult(exitCode, exitCode == 0 ? "" : "boom"));
        }
    }

    private static void WriteSolidPng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void WriteTwoColorPng(string path)
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 16, 32, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static RenderRequest RequestTo(string outPath) =>
        new("in.x", outPath, 100, 100, SnapshotTheme.Light);

    [Fact]
    public async Task RenderAsync_WhenCommandSucceedsWithGoodPng_ShouldReturnOkExitZero()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(0, () => WriteTwoColorPng(outPath)));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("ok", outcome.Status);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Empty(outcome.Warnings);
        File.Delete(outPath);
    }

    [Fact]
    public async Task RenderAsync_WhenPngIsSingleColor_ShouldReturnExitOneWithWarning()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(0, () => WriteSolidPng(outPath, SKColors.White)));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("ok", outcome.Status);
        Assert.Equal(1, outcome.ExitCode);
        Assert.NotEmpty(outcome.Warnings);
        File.Delete(outPath);
    }

    [Fact]
    public async Task RenderAsync_WhenCommandFails_ShouldReturnFailedExitTwo()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var engine = new RenderEngine(new FakeRunner(1, () => { /* writes nothing */ }));

        var outcome = await engine.RenderAsync(LsSpec(), RequestTo(outPath));

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }

    [Fact]
    public async Task RenderAsync_WhenToolMissing_ShouldThrowWithHint()
    {
        var spec = new RendererSpec("mermaid", "definitely-not-a-real-tool-xyz", ["{out}"], [".x"]);
        var engine = new RenderEngine(new FakeRunner(0, () => { }));

        var ex = await Assert.ThrowsAsync<GlimpseRenderToolException>(
            () => engine.RenderAsync(spec, RequestTo("/tmp/x.png")));
        Assert.Equal("definitely-not-a-real-tool-xyz", ex.Tool);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RenderEngineTests`
Expected: FAIL — `IProcessRunner` / `RenderEngine` do not exist.

- [ ] **Step 3: Implement the process runner**

Create `src/Glimpse.Core/ProcessRunner.cs`:

```csharp
using System.Diagnostics;

namespace Glimpse.Core;

public sealed record ProcessResult(int ExitCode, string StdErr);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args);
}

/// <summary>Runs a render tool as a child process, capturing stderr.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new GlimpseRenderToolException(executable, $"Failed to start '{executable}'.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, stderr);
    }
}
```

- [ ] **Step 4: Implement the engine**

Create `src/Glimpse.Core/RenderEngine.cs`:

```csharp
namespace Glimpse.Core;

public sealed class GlimpseRenderToolException(string tool, string hint)
    : Exception($"{tool}: {hint}")
{
    public string Tool { get; } = tool;
    public string Hint { get; } = hint;
}

public sealed record RenderOutcome(
    string Status,
    int ExitCode,
    string OutputPath,
    int Width,
    int Height,
    IReadOnlyList<string> Warnings);

/// <summary>Resolve tool -> run command -> analyse PNG -> outcome. The whole pipeline, minus persistence.</summary>
public sealed class RenderEngine(IProcessRunner runner)
{
    public async Task<RenderOutcome> RenderAsync(RendererSpec spec, RenderRequest request)
    {
        var executable = ToolLocator.Resolve(spec.Tool)
            ?? throw new GlimpseRenderToolException(spec.Tool, ToolLocator.InstallHint(spec.Tool));

        var command = RenderCommandBuilder.Build(spec, request, executable);
        var result = await runner.RunAsync(command.Executable, command.Args);

        if (result.ExitCode != 0 || !File.Exists(request.OutputPath))
            return new RenderOutcome("failed", 2, request.OutputPath, 0, 0,
                [$"render-failed:{result.StdErr.Trim()}"]);

        var inspection = PngAnalysis.Inspect(request.OutputPath);
        var exitCode = inspection.Warnings.Count > 0 ? 1 : 0;
        return new RenderOutcome("ok", exitCode, request.OutputPath,
            inspection.Width, inspection.Height, inspection.Warnings);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter RenderEngineTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Glimpse.Core/ProcessRunner.cs src/Glimpse.Core/RenderEngine.cs tests/Glimpse.Core.Tests/RenderEngineTests.cs
git commit -m "feat(core): process runner + render engine pipeline"
```

---

### Task 6: Stable-name output + manifest recording on `SnapshotWriter`

**Files:**
- Modify: `src/Glimpse.Core/SnapshotWriter.cs`
- Test: `tests/Glimpse.Core.Tests/SnapshotWriterTests.cs` (add cases)

**Interfaces:**
- Consumes: existing `SnapshotWriter(string outputDir)`, `Manifest`, `SceneEntry`, `ManifestMerge`.
- Produces (added to `SnapshotWriter`):
  - `string PngPath(string name)` → `Path.Combine(outputDir, $"{name}.png")`, creating `outputDir`. The harness passes this as `{out}` so the render tool writes the stable file directly (overwrite on re-run).
  - `void Prune(ISet<string> keepNames)` → deletes `*.png` files in `outputDir` whose base name is not in `keepNames`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Glimpse.Core.Tests/SnapshotWriterTests.cs` (new methods in the existing class):

```csharp
[Fact]
public void PngPath_ShouldReturnStableNameInsideOutputDir()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var writer = new SnapshotWriter(dir);

    var path = writer.PngPath("architecture");

    Assert.Equal(Path.Combine(dir, "architecture.png"), path);
    Assert.True(Directory.Exists(dir));
    Directory.Delete(dir, recursive: true);
}

[Fact]
public void Prune_ShouldDeletePngsNotInKeepSet()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    File.WriteAllBytes(Path.Combine(dir, "keep.png"), [1]);
    File.WriteAllBytes(Path.Combine(dir, "stale.png"), [1]);
    var writer = new SnapshotWriter(dir);

    writer.Prune(new HashSet<string> { "keep" });

    Assert.True(File.Exists(Path.Combine(dir, "keep.png")));
    Assert.False(File.Exists(Path.Combine(dir, "stale.png")));
    Directory.Delete(dir, recursive: true);
}
```

(If `SnapshotWriterTests` lacks a `using System.Collections.Generic;`/`System.IO;`, add them — they are usually implicit via `ImplicitUsings`.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter SnapshotWriterTests`
Expected: FAIL — `PngPath` / `Prune` do not exist.

- [ ] **Step 3: Add the methods**

In `src/Glimpse.Core/SnapshotWriter.cs`, add inside the class (after `SavePng`):

```csharp
/// <summary>Stable path for a named target — re-renders overwrite it (no accumulation).</summary>
public string PngPath(string name)
{
    Directory.CreateDirectory(outputDir);
    return Path.Combine(outputDir, $"{name}.png");
}

/// <summary>Deletes PNGs in the output dir whose base name is not in <paramref name="keepNames"/>.</summary>
public void Prune(ISet<string> keepNames)
{
    if (!Directory.Exists(outputDir))
        return;

    foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
        if (!keepNames.Contains(Path.GetFileNameWithoutExtension(file)))
            File.Delete(file);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter SnapshotWriterTests`
Expected: PASS (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add src/Glimpse.Core/SnapshotWriter.cs tests/Glimpse.Core.Tests/SnapshotWriterTests.cs
git commit -m "feat(core): stable-name PngPath + Prune on SnapshotWriter"
```

---

### Task 7: `Glimpse.Capture` CLI

**Files:**
- Create: `tools/Glimpse.Capture/Glimpse.Capture.csproj`
- Create: `tools/Glimpse.Capture/CaptureOptions.cs`
- Create: `tools/Glimpse.Capture/Program.cs`
- Create: `tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj`
- Create: `tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs`
- Modify: `Glimpse.slnx`

**Interfaces:**
- Consumes: `RendererRegistry`, `RenderEngine`, `ProcessRunner`, `RenderRequest`, `RenderOutcome`, `SnapshotWriter`, `ManifestMerge`, `SceneEntry`, `OutputLocator`, `SnapshotTheme`.
- Produces:
  - `record CaptureOptions(string? Source, string? Renderer, string Name, string? OutDir, int Width, int Height, SnapshotTheme Theme, int? WindowId, bool Prune)` with `static CaptureOptions Parse(string[] args)`.
  - Defaults: `Width=1280`, `Height=800`, `Theme=Light`, `Prune=false`. `Name` defaults to the source filename without extension; for `--renderer app` with no source, `Name` defaults to `"app"`.
  - Flags: `--renderer`, `--name`, `--out`, `--size WxH`, `--theme light|dark`, `--window-id`, `--prune`. First positional arg (not starting with `-`) is the source.

- [ ] **Step 1: Write the failing CaptureOptions tests**

Create `tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.Capture;
using Xunit;

namespace Glimpse.Capture.Tests;

public class CaptureOptionsTests
{
    [Fact]
    public void Parse_WithSourceOnly_ShouldDefaultNameFromFileName()
    {
        var options = CaptureOptions.Parse(["diagrams/architecture.mmd"]);

        Assert.Equal("diagrams/architecture.mmd", options.Source);
        Assert.Equal("architecture", options.Name);
        Assert.Equal(1280, options.Width);
        Assert.Equal(800, options.Height);
        Assert.Equal(SnapshotTheme.Light, options.Theme);
    }

    [Fact]
    public void Parse_WithSizeFlag_ShouldSplitWidthAndHeight()
    {
        var options = CaptureOptions.Parse(["x.mmd", "--size", "640x480"]);

        Assert.Equal(640, options.Width);
        Assert.Equal(480, options.Height);
    }

    [Fact]
    public void Parse_WithThemeAndRendererAndName_ShouldBind()
    {
        var options = CaptureOptions.Parse(
            ["x.mmd", "--renderer", "graphviz", "--name", "flow", "--theme", "dark"]);

        Assert.Equal("graphviz", options.Renderer);
        Assert.Equal("flow", options.Name);
        Assert.Equal(SnapshotTheme.Dark, options.Theme);
    }

    [Fact]
    public void Parse_WithAppRendererAndWindowId_ShouldDefaultNameToApp()
    {
        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "42"]);

        Assert.Null(options.Source);
        Assert.Equal("app", options.Name);
        Assert.Equal(42, options.WindowId);
    }

    [Fact]
    public void Parse_WithUnknownFlag_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CaptureOptions.Parse(["x.mmd", "--bogus"]));
    }
}
```

- [ ] **Step 2: Create the test project + register projects in the solution**

Create `tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\Glimpse.Capture\Glimpse.Capture.csproj" />
  </ItemGroup>

</Project>
```

Create `tools/Glimpse.Capture/Glimpse.Capture.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Glimpse.Abstractions\Glimpse.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Glimpse.Core\Glimpse.Core.csproj" />
  </ItemGroup>

</Project>
```

In `Glimpse.slnx`, add a `tools` folder block and the test project line:

```xml
  <Folder Name="/tools/">
    <Project Path="tools/Glimpse.Capture/Glimpse.Capture.csproj" />
  </Folder>
```

and inside the existing `/tests/` folder:

```xml
    <Project Path="tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj" />
```

- [ ] **Step 3: Implement `CaptureOptions`**

Create `tools/Glimpse.Capture/CaptureOptions.cs`:

```csharp
using Glimpse.Abstractions;

namespace Glimpse.Capture;

public sealed record CaptureOptions(
    string? Source,
    string? Renderer,
    string Name,
    string? OutDir,
    int Width,
    int Height,
    SnapshotTheme Theme,
    int? WindowId,
    bool Prune)
{
    public static CaptureOptions Parse(string[] args)
    {
        string? source = null, renderer = null, name = null, outDir = null;
        int width = 1280, height = 800, windowId = 0;
        var theme = SnapshotTheme.Light;
        var prune = false;
        var hasWindowId = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--renderer": renderer = Next(args, ref i); break;
                case "--name": name = Next(args, ref i); break;
                case "--out": outDir = Next(args, ref i); break;
                case "--theme": theme = ThemeFor(Next(args, ref i)); break;
                case "--window-id": windowId = int.Parse(Next(args, ref i)); hasWindowId = true; break;
                case "--prune": prune = true; break;
                case "--size": (width, height) = SizeFor(Next(args, ref i)); break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unexpected argument '{arg}'.");
                    source = arg;
                    break;
            }
        }

        var resolvedName = name
            ?? (source is not null ? Path.GetFileNameWithoutExtension(source) : renderer ?? "snapshot");

        return new CaptureOptions(source, renderer, resolvedName, outDir, width, height, theme,
            hasWindowId ? windowId : null, prune);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for '{args[i]}'.");
        return args[++i];
    }

    private static SnapshotTheme ThemeFor(string theme) => theme switch
    {
        "light" => SnapshotTheme.Light,
        "dark" => SnapshotTheme.Dark,
        _ => throw new ArgumentException($"Unknown --theme '{theme}' (expected light|dark)."),
    };

    private static (int Width, int Height) SizeFor(string size)
    {
        var parts = size.Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
            throw new ArgumentException($"Invalid --size '{size}' (expected WIDTHxHEIGHT, e.g. 1280x800).");
        return (w, h);
    }
}
```

- [ ] **Step 4: Run the CaptureOptions tests to verify they pass**

Run: `dotnet test tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj --filter CaptureOptionsTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Implement `Program.cs`**

Create `tools/Glimpse.Capture/Program.cs`:

```csharp
using Glimpse.Capture;
using Glimpse.Core;

var options = CaptureOptions.Parse(args);

var registry = RendererRegistry.Default();
var outDir = options.OutDir ?? Path.Combine(OutputLocator.Resolve(), "glimpse");
var writer = new SnapshotWriter(outDir);
var outputPath = writer.PngPath(options.Name);

RendererSpec spec;
try
{
    spec = registry.Resolve(options.Renderer, options.Source);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (options.Source is null && spec.Name != "app")
{
    Console.Error.WriteLine("A source file is required for this renderer.");
    return 2;
}

var request = new RenderRequest(
    options.Source ?? "", outputPath, options.Width, options.Height, options.Theme, options.WindowId);

var engine = new RenderEngine(new ProcessRunner());

RenderOutcome outcome;
try
{
    outcome = await engine.RenderAsync(spec, request);
}
catch (GlimpseRenderToolException ex)
{
    Console.Error.WriteLine(ex.Hint);
    return 2;
}

var entry = new SceneEntry(
    options.Name, options.Theme.ToString().ToLowerInvariant(), outputPath,
    outcome.Width, outcome.Height, 1.0, outcome.Status, DateTimeOffset.UtcNow, outcome.Warnings);

var manifest = ManifestMerge.Merge(
    writer.ReadExisting(), [entry], [], Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
writer.WriteManifest(manifest);

if (options.Prune)
    writer.Prune(new HashSet<string> { options.Name });

Console.WriteLine($"PNG:      {outcome.OutputPath}");
Console.WriteLine($"Status:   {outcome.Status} ({outcome.Width}x{outcome.Height})");
if (outcome.Warnings.Count > 0)
    Console.WriteLine($"Warnings: {string.Join(", ", outcome.Warnings)}");
Console.WriteLine($"Manifest: {Path.Combine(outDir, "manifest.json")}");

return outcome.ExitCode;
```

- [ ] **Step 6: Build the CLI and verify it runs**

Run: `dotnet build tools/Glimpse.Capture/Glimpse.Capture.csproj`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors).

- [ ] **Step 7: Commit**

```bash
git add tools/Glimpse.Capture tests/Glimpse.Capture.Tests Glimpse.slnx
git commit -m "feat(cli): Glimpse.Capture harness CLI (render -> analyse -> manifest)"
```

---

### Task 8: `glimpse` skill + real end-to-end mermaid gate

**Files:**
- Create: `.claude/skills/glimpse/SKILL.md`
- Create: `tests/Glimpse.Capture.Tests/EndToEndGateTests.cs`

**Interfaces:**
- Consumes: the built `Glimpse.Capture` CLI; `mmdc` on PATH (skip if absent); `PngAnalysis`, `SnapshotWriter`/`Manifest` for assertions.
- Produces: a CI-style gate proving a real mermaid source renders to a non-blank PNG and a manifest entry.

- [ ] **Step 1: Write the end-to-end gate test (skips if mmdc absent)**

Create `tests/Glimpse.Capture.Tests/EndToEndGateTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Capture.Tests;

public class EndToEndGateTests
{
    [SkippableFact]
    public async Task Mermaid_RealRender_ShouldProduceNonBlankPngAndManifestEntry()
    {
        Skip.If(ToolLocator.Resolve("mmdc") is null, "mermaid-cli (mmdc) not installed.");

        var dir = Path.Combine(Path.GetTempPath(), $"glimpse-e2e-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "flow.mmd");
        await File.WriteAllTextAsync(source, "flowchart TD\n  A[Start] --> B[Render]\n  B --> C[Read]\n");
        var writer = new SnapshotWriter(dir);

        var spec = RendererRegistry.Default().Resolve("mermaid", source);
        var request = new RenderRequest(source, writer.PngPath("flow"), 800, 600, Abstractions.SnapshotTheme.Light);
        var outcome = await new RenderEngine(new ProcessRunner()).RenderAsync(spec, request);

        Assert.Equal("ok", outcome.Status);
        Assert.Empty(outcome.Warnings);            // a real flowchart is never single-color
        Assert.True(File.Exists(outcome.OutputPath));

        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Add the Xunit.SkippableFact package**

In `Directory.Packages.props` `<ItemGroup>`:

```xml
<PackageVersion Include="Xunit.SkippableFact" Version="1.5.23" />
```

In `tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj`, add to the package `<ItemGroup>`:

```xml
<PackageReference Include="Xunit.SkippableFact" />
```

- [ ] **Step 3: Run the gate (passes locally where mmdc is installed)**

Run: `dotnet test tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj --filter EndToEndGateTests`
Expected: PASS (renders a real PNG) — or SKIPPED on machines without `mmdc`.

- [ ] **Step 4: Write the skill**

Create `.claude/skills/glimpse/SKILL.md`:

```markdown
---
name: glimpse
description: Use when you need to SEE a UI or diagram you are creating or changing — render any artifact (mermaid/graphviz/d2 diagram, HTML page, or live macOS app window) to a PNG, Read it, critique, improve, repeat. Triggers on "render this diagram", "show me how this looks", "iterate on this diagram/UI until it's right", "screenshot the app".
---

# Glimpse — Visual Feedback Loop

Render an artifact to a PNG, Read the PNG, judge it, improve the source, repeat.

## The loop

1. **Render:** `dotnet run --project tools/Glimpse.Capture -- <source> [--renderer NAME] [--name NAME] [--theme dark] [--size WxH]`
   - Renderer is inferred from extension (`.mmd`→mermaid, `.dot`/`.gv`→graphviz, `.d2`→d2, `.html`→web). Use `--renderer app --window-id N` for a live macOS window.
2. **Read the printed `PNG:` path** with the Read tool — actually look at it.
3. **Check the printed warnings.** `single-color-frame:*` or a non-zero exit means the render broke (missing font, bad source, blank output) — fix the pipeline before judging design.
4. **Judge against intent:** layout, overlap, clipped/tofu text, legibility, does it communicate the thing.
5. **If not right:** edit the source, re-run (same `--name` overwrites — no accumulation), go to step 2.
6. **Stop** when it meets intent and warnings are clean. Cap at ~5–6 iterations; if not converging, stop and report what's stuck.

## Requirements

The renderer's tool must be installed; the CLI fails fast with an install hint:
- mermaid → `npm i -g @mermaid-js/mermaid-cli`
- graphviz → `brew install graphviz`
- d2 → `brew install d2`
- web → Google Chrome
- app → macOS `screencapture` (+ Screen Recording permission)

## Output

PNGs + `manifest.json` go under `.claude/tmp/ui-snapshots/glimpse/` (per-repo) or `~/.claude/ui-snapshots/glimpse/` (outside a repo). Stable name per `--name`, so re-renders overwrite.
```

- [ ] **Step 5: Run the full suite (regression check)**

Run: `dotnet test`
Expected: all prior tests + the new ones PASS (the e2e gate passes where `mmdc` exists), format clean.

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/glimpse/SKILL.md tests/Glimpse.Capture.Tests/EndToEndGateTests.cs Directory.Packages.props tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj
git commit -m "feat: glimpse skill + real mermaid end-to-end gate"
```

---

## Self-Review

**Spec coverage:**
- §3 pipeline (resolve → render → analyse → record + report) → Tasks 5 (engine) + 7 (CLI records/reports). ✓
- §4 generic command→PNG contract, built-ins, extension inference, JSON override, fail-fast → Tasks 2 + 3 + 4. ✓
- §5 mechanical checks, exit codes 0/1/2, stop rules → Tasks 1 + 5 (codes) + 8 (stop rules in skill). ✓
- §6 stable-name overwrite, prune, manifest reuse, `(Name,Theme)` key → Task 6 + Task 7 (uses `Scene` field for name). ✓
- §7 Avalonia parked (untouched — no task modifies it ✓); SkiaSharp PNG analyser → Task 1. ✓
- §8 CLI `tools/Glimpse.Capture` + flags + absolute-path report; `glimpse` skill → Tasks 7 + 8. ✓
- §9 build order → Tasks 1→8 follow it. ✓
- §10 tool deps + fail-fast + SkiaSharp dep → Tasks 1, 3. ✓

**Placeholder scan:** none — every code/step is concrete. ✓

**Type consistency:** `RenderRequest`, `RenderCommand`, `RenderOutcome`, `RendererSpec`, `PngInspection`, `ProcessResult`, `GlimpseRenderToolException`, `CaptureOptions` are defined once and consumed with matching signatures across Tasks 1–8. `SceneEntry` reused with `Scene`=name. ✓

**Known simplifications (intentional, per spec Non-Goals):** `{theme}` maps Light→`default`/Dark→`dark` (mermaid vocabulary; other renderers don't use `{theme}` in their default args). `app` window-name→id lookup stays deferred to the live-screenshot plan; `--window-id` is the input here.
