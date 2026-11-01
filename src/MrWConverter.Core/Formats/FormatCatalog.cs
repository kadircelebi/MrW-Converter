using System.Text.Json;

namespace MrWConverter.Core.Formats;

/// <summary>
/// Knows which formats exist and what each one can become. The UI builds its
/// choices from this data, so adding a format starts in catalog.json.
/// </summary>
public sealed class FormatCatalog
{
    private const string ResourceName = "MrWConverter.Core.Formats.catalog.json";

    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _targetsByInput;
    private readonly IReadOnlyDictionary<string, string> _groupByInput;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _toolsByInput;

    private FormatCatalog(CatalogData data)
    {
        _aliases = data.Aliases;

        var targets = new Dictionary<string, IReadOnlyList<string>>();
        var groups = new Dictionary<string, string>();
        var tools = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var group in data.Groups)
        {
            foreach (var input in group.Inputs)
            {
                targets[input] = group.Outputs.Where(output => output != input).ToArray();
                groups[input] = group.Name;
                tools[input] = group.Tools ?? [];
            }
        }

        _targetsByInput = targets;
        _groupByInput = groups;
        _toolsByInput = tools;
    }

    public static FormatCatalog Default { get; } = LoadDefault();

    public IEnumerable<string> InputFormats => _targetsByInput.Keys;

    public IEnumerable<string> InputExtensions =>
        InputFormats.Concat(_aliases.Where(alias => _targetsByInput.ContainsKey(alias.Value)).Select(alias => alias.Key));

    /// <summary>Returns the canonical format of a path, for example "jpg" for "PHOTO.JPEG".</summary>
    public string FormatOf(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return _aliases.GetValueOrDefault(extension, extension);
    }

    public bool IsSupported(string path) => _targetsByInput.ContainsKey(FormatOf(path));

    /// <summary>Returns the catalog group of a supported file: "image", "audio", "video" or "animation".</summary>
    public string GroupOf(string path) => _groupByInput[FormatOf(path)];

    /// <summary>
    /// Returns the targets every given file can become, keeping catalog order.
    /// A file's own format is not a target, so a mixed selection offers fewer choices.
    /// </summary>
    public IReadOnlyList<string> CommonTargets(IReadOnlyCollection<string> paths) => Common(paths, _targetsByInput);

    /// <summary>Returns the tools that work on every given file.</summary>
    public IReadOnlyList<string> CommonTools(IReadOnlyCollection<string> paths) => Common(paths, _toolsByInput);

    private IReadOnlyList<string> Common(IReadOnlyCollection<string> paths, IReadOnlyDictionary<string, IReadOnlyList<string>> table)
    {
        if (paths.Count == 0 || !paths.All(IsSupported))
        {
            return [];
        }

        return paths
            .Select(path => table[FormatOf(path)])
            .Aggregate((common, items) => common.Intersect(items).ToArray());
    }

    private static FormatCatalog LoadDefault()
    {
        using var stream = typeof(FormatCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<CatalogData>(stream, options)
            ?? throw new InvalidOperationException("The format catalog is empty.");

        return new FormatCatalog(data);
    }

    private sealed record CatalogData(Dictionary<string, string> Aliases, List<FormatGroup> Groups);

    private sealed record FormatGroup(string Name, List<string> Inputs, List<string> Outputs, List<string>? Tools);
}
