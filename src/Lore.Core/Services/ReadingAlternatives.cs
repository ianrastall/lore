using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lore.Models;

namespace Lore.Services;

// A reading receives its own immutable corpus snapshot. Shared interpreters never
// keep a mutable current seed, and concurrent readings cannot borrow one another's text.
internal sealed class ReadingAlternatives<T> where T : class
{
    private readonly string _baseline, _kind;
    private readonly JsonSerializerOptions _options;
    private readonly Dictionary<string, string[]> _entries = new(StringComparer.Ordinal);
    private static readonly Regex Placeholder = new(@"\{[A-Za-z][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant);

    private ReadingAlternatives(T baseline, string kind, Dictionary<string, string[]> patches, JsonSerializerOptions options)
    {
        _kind = kind; _options = options;
        var root = JsonSerializer.SerializeToNode(baseline, options)!.AsObject();
        _baseline = root.ToJsonString();
        foreach (var (path, alternatives) in patches)
        {
            var (parent, key) = Resolve(root, path);
            if (parent[key] is not JsonValue value || !value.TryGetValue<string>(out var original) || string.IsNullOrWhiteSpace(original))
                throw new InvalidDataException($"{kind}/{path} is not an existing prose entry.");
            if (alternatives is null || alternatives.Length == 0 || alternatives.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"{kind}/{path} has an empty alternative.");
            string[] choices = [original, .. alternatives];
            if (choices.Distinct(StringComparer.Ordinal).Count() != choices.Length)
                throw new InvalidDataException($"{kind}/{path} repeats an alternative.");
            var required = Placeholder.Matches(original).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            foreach (string line in alternatives)
                if (!required.SetEquals(Placeholder.Matches(line).Select(m => m.Value)))
                    throw new InvalidDataException($"{kind}/{path} changes the required person/value placeholders.");
            _entries.Add(path, choices);
        }
    }

    public static ReadingAlternatives<T>? Load(string corpusPath, T baseline, JsonSerializerOptions options)
    {
        string sidecar = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(corpusPath))!, ReadingAlternatives.FileName);
        // Older standalone fixtures and a missing original corpus retain their existing
        // behavior. InstalledFiles separately requires the sidecar in every release.
        if (!File.Exists(sidecar) || !File.Exists(corpusPath)) return null;
        var catalog = ReadingAlternatives.Read(sidecar);
        string kind = Path.GetFileNameWithoutExtension(corpusPath);
        return catalog.Readings.TryGetValue(kind, out var patches) && patches.Count > 0
            ? new ReadingAlternatives<T>(baseline, kind, patches, options) : null;
    }

    public T For(string seed)
    {
        var root = JsonNode.Parse(_baseline)!.AsObject();
        foreach (string path in _entries.Keys)
        {
            var (parent, key) = Resolve(root, path);
            parent[key] = Pick(path, seed);
        }
        return root.Deserialize<T>(_options) ?? throw new InvalidDataException("Could not compose a reading corpus.");
    }

    public string Pick(string path, string seed, HashSet<string>? used = null)
    {
        if (!_entries.TryGetValue(path, out var choices))
        {
            var (parent, key) = Resolve(JsonNode.Parse(_baseline)!.AsObject(), path);
            choices = [parent[key]!.GetValue<string>()];
        }
        string picked = AuthoredText.Pick(choices, seed + "|" + _kind + "|" + path);
        if (used is null || used.Add(picked)) return picked;
        int first = Array.IndexOf(choices, picked);
        for (int i = 1; i < choices.Length; i++)
            if (used.Add(choices[(first + i) % choices.Length])) return choices[(first + i) % choices.Length];
        return picked;
    }

    private static (JsonObject Parent, string Key) Resolve(JsonObject root, string path)
    {
        string[] parts = path.Split('/');
        if (parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Empty prose path.");
        JsonObject current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            string? key = current.Select(p => p.Key).FirstOrDefault(k => k.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (key is null) throw new InvalidDataException($"Unknown prose path: {path}");
            if (i == parts.Length - 1) return (current, key);
            current = current[key] as JsonObject ?? throw new InvalidDataException($"Not a prose collection: {path}");
        }
        throw new InvalidDataException($"Invalid prose path: {path}");
    }
}

public static class ReadingAlternatives
{
    public const string FileName = "reading-variants.json";
    public sealed class Catalog
    {
        public string Version { get; init; } = "";
        public Dictionary<string, Dictionary<string, string[]>> Readings { get; init; } = new();
    }
    public static Catalog Read(string path)
    {
        var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The reading alternatives are empty.");
        string[] allowed = ["interpretations", "daily", "synastry", "davison", "mirror"];
        if (string.IsNullOrWhiteSpace(catalog.Version) || catalog.Readings is null || catalog.Readings.Count == 0 ||
            catalog.Readings.Keys.Any(k => !allowed.Contains(k)) || catalog.Readings.Values.Any(v => v is null))
            throw new InvalidDataException("The reading alternatives have an unknown version or reader.");
        return catalog;
    }
    public static string Seed(NatalChart chart) => chart.CalculatedForUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
        chart.Ascendant.ToString("R", CultureInfo.InvariantCulture);
    public static string GroupSeed(params NatalChart[] charts) => string.Join("|", charts.Select(Seed).Order(StringComparer.Ordinal));
}
