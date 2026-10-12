using Lore.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lore.Services;

// The Legend's entries, read from Data\legend.json and checked whole before any is
// shown. A file that is missing or damaged leaves one entry that says so, in place of
// the Legend: nothing else in Lore depends on it.
public sealed partial class LegendService
{
    private sealed class Corpus
    {
        public List<LegendItem?>? Entries { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<LegendItem> Items { get; }

    // True when the file was read and every entry in it is whole.
    public bool IsAvailable { get; }

    public LegendService(string legendJsonPath)
    {
        List<LegendItem?>? read = null;
        try
        {
            if (File.Exists(legendJsonPath))
            {
                using var stream = File.OpenRead(legendJsonPath);
                read = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts)?.Entries;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The Legend could not be read from {legendJsonPath}: {ex.Message}");
        }

        string? fault = read is null ? "there is nothing in it" : Fault(read);
        if (read is not null && fault is not null)
            Diagnostics.Log($"The Legend in {legendJsonPath} is not usable: {fault}.");
        IsAvailable = fault is null;
        Items = IsAvailable ? read!.Select(e => e!).ToList() : [Unavailable];
    }

    private static readonly LegendItem Unavailable = new("?", "The Legend is not available", "Legend", "a file is missing",
        "The text of the Legend (Data\\legend.json) is missing or damaged, so it cannot be shown. Nothing else in Lore is " +
        "affected.\n\nInstalling Lore again, or unzipping the portable copy afresh, puts the file back.");

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    // What is wrong with the entries as read, or null if they are all whole: each has
    // its five texts, no two share a name within a group, a colour is #RRGGBB, and the
    // bold marks in a detail are in pairs within each block (an odd one would embolden
    // the rest of the paragraph).
    internal static string? Fault(IReadOnlyList<LegendItem?> entries)
    {
        if (entries.Count == 0) return "there are no entries";
        var seen = new HashSet<(string, string)>();
        foreach (var e in entries)
        {
            if (e is null) return "an entry is empty";
            string name = string.IsNullOrWhiteSpace(e.Name) ? "(unnamed)" : e.Name;
            if (string.IsNullOrWhiteSpace(e.Symbol) || string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.Group) ||
                string.IsNullOrWhiteSpace(e.Short) || string.IsNullOrWhiteSpace(e.Detail))
                return $"the entry {name} lacks its symbol, name, group, short line or detail";
            if (!seen.Add((e.Group, e.Name))) return $"{e.Name} is given twice under {e.Group}";
            if (e.ColorHex is not null && !Hex().IsMatch(e.ColorHex)) return $"the colour of {e.Name} is not #RRGGBB";
            foreach (string block in e.Detail.Split("\n\n"))
                if (Regex.Matches(block, Regex.Escape("**")).Count % 2 != 0)
                    return $"a bold mark in {e.Name} has no partner";
        }
        return null;
    }
}
