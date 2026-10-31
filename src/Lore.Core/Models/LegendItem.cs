using System.Text.Json.Serialization;

namespace Lore.Models;

// One entry in the Legend, as it is kept in Data\legend.json: a glyph, colour or term
// Lore shows, with a line for the list and a fuller explanation. ColorHex is set only
// for an entry with a line colour of its own (the aspects).
public sealed record LegendItem(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group")] string Group,
    [property: JsonPropertyName("short")] string Short,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("color")] string? ColorHex = null);
