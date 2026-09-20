using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

public sealed class CelebrityService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private List<Celebrity>? _celebrities;

    public IReadOnlyList<Celebrity> All => _celebrities ?? [];

    public async Task<IReadOnlyList<Celebrity>> LoadAsync(string jsonPath)
    {
        if (_celebrities is not null) return _celebrities;

        await using var stream = File.OpenRead(jsonPath);
        _celebrities = await JsonSerializer.DeserializeAsync<List<Celebrity>>(stream, JsonOpts)
            ?? throw new InvalidOperationException("Failed to load celebrity data.");
        return _celebrities;
    }

    public IReadOnlyList<string> GetCategories() =>
        _celebrities?.Select(c => c.Category).Distinct().Order().ToList() ?? [];

    public IReadOnlyList<Celebrity> Filter(string? category, string? searchText)
    {
        if (_celebrities is null) return [];

        IEnumerable<Celebrity> q = _celebrities;

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(c => c.Category == category);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string lower = searchText.ToLowerInvariant();
            q = q.Where(c =>
                c.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                c.BirthPlace.Contains(lower, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(c => c.Name).ToList();
    }
}
