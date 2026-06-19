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
