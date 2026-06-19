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
