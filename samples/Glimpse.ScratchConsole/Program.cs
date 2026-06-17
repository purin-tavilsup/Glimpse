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
