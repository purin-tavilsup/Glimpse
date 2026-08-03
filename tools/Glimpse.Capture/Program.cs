using Glimpse.Capture;
using Glimpse.Core;

var options = CaptureOptions.Parse(args);

if (options.ListWindows)
{
    var finder = PlatformSupport.WindowFinder();
    if (finder is null)
    {
        Console.Error.WriteLine(PlatformSupport.UnsupportedMessage("--list-windows"));
        return 2;
    }

    foreach (var w in finder.ListOnScreen())
        Console.WriteLine($"[id {w.WindowId,-6}] layer {w.Layer,-3} {w.Width}x{w.Height}  {w.OwnerName} — {w.Title ?? "(untitled)"}");
    return 0;
}

if (options.CheckIcons)
{
    if (options.Source is null)
    {
        Console.Error.WriteLine("--check-icons requires a .d2 file path.");
        return 2;
    }
    if (!File.Exists(options.Source))
    {
        Console.Error.WriteLine($"not found: {options.Source}");
        return 2;
    }

    var urls = D2IconCheck.ExtractIconUrls(File.ReadAllText(options.Source));
    if (urls.Count == 0)
    {
        Console.WriteLine($"No icon: URLs in {options.Source} (nothing to check).");
        return 0;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var anyFailed = false;
    foreach (var url in urls)
    {
        int code;
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            code = (int)resp.StatusCode;
        }
        catch
        {
            code = 0;
        }

        if (code == 200)
        {
            Console.WriteLine($"ok   {code}  {url}");
        }
        else
        {
            Console.WriteLine($"BAD  {code}  {url}");
            anyFailed = true;
        }
    }

    if (anyFailed)
    {
        Console.Error.WriteLine("Broken icon URL(s) — fix them or those icons will silently vanish.");
        return 1;
    }
    Console.WriteLine("All icon URLs resolve.");
    return 0;
}

var outDir = options.OutDir ?? Path.Combine(OutputLocator.Resolve(), "glimpse");
var writer = new SnapshotWriter(outDir);
var outputPath = writer.PngPath(options.Name);

// Live-app screenshot when a window is named/identified, or the renderer is "app";
// otherwise render a source file. Both produce a RenderOutcome for the shared tail.
var wantApp = options.Renderer == "app" || options.Window is not null;
var extraWarnings = new List<string>();

RenderOutcome outcome;
try
{
    outcome = wantApp
        ? await CaptureAppAsync(options, outputPath, extraWarnings)
        : await RenderSourceAsync(options, outputPath);
}
catch (GlimpseRenderToolException ex)
{
    Console.Error.WriteLine(ex.Hint);
    return 2;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (extraWarnings.Count > 0)
    outcome = outcome with { Warnings = [.. extraWarnings, .. outcome.Warnings] };

if (!options.NoManifest)
{
    var entry = new SceneEntry(
        options.Name, options.Theme.ToString().ToLowerInvariant(), outputPath,
        outcome.Width, outcome.Height, 1.0, outcome.Status, DateTimeOffset.UtcNow, outcome.Warnings);

    var manifest = ManifestMerge.Merge(
        writer.ReadExisting(), [entry], [], Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
    writer.WriteManifest(manifest);
}

if (options.Prune)
    writer.Prune(new HashSet<string> { options.Name });

Console.WriteLine($"PNG:      {outcome.OutputPath}");
Console.WriteLine($"Status:   {outcome.Status} ({outcome.Width}x{outcome.Height})");
if (outcome.Warnings.Count > 0)
    Console.WriteLine($"Warnings: {string.Join(", ", outcome.Warnings)}");
if (!options.NoManifest)
    Console.WriteLine($"Manifest: {Path.Combine(outDir, "manifest.json")}");

return outcome.ExitCode;

async Task<RenderOutcome> RenderSourceAsync(CaptureOptions o, string outPath)
{
    var spec = RendererRegistry.Default().Resolve(o.Renderer, o.Source);
    if (o.Source is null)
        throw new ArgumentException("A source file is required for this renderer.");

    var req = new RenderRequest(o.Source, outPath, o.Width, o.Height, o.Theme, o.WindowId);
    return await new RenderEngine(new ProcessRunner()).RenderAsync(spec, req);
}

async Task<RenderOutcome> CaptureAppAsync(CaptureOptions o, string outPath, List<string> warnings)
{
    var capturer = PlatformSupport.AppCapturer(new ProcessRunner())
        ?? throw new ArgumentException(PlatformSupport.UnsupportedMessage("The 'app' renderer"));

    // A null window id means "whole screen" to every capturer, which is also the fallback
    // when --window matched nothing.
    var windowId = o.WindowId ?? ResolveWindowId(o, warnings);

    return await capturer.CaptureAsync(
        new RenderRequest("", outPath, o.Width, o.Height, o.Theme, windowId));
}

long? ResolveWindowId(CaptureOptions o, List<string> warnings)
{
    if (o.Window is null)
        return null;

    var finder = PlatformSupport.WindowFinder()
        ?? throw new ArgumentException(PlatformSupport.UnsupportedMessage("--window lookup"));

    var selected = WindowSelector.SelectFrontmost(finder.ListOnScreen(), o.Window, o.Title);
    if (selected is null)
    {
        warnings.Add($"fullscreen-fallback:no on-screen window matching '{o.Window}'");
        return null;
    }

    Console.WriteLine($"Window:   {selected.OwnerName} — {selected.Title ?? "(untitled)"} [id {selected.WindowId}]");
    return selected.WindowId;
}
