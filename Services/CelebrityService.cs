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

    // Category listing and filtering live in MainViewModel, which also folds in the
    // user's custom charts; this service just loads and exposes the bundled corpus.
}
