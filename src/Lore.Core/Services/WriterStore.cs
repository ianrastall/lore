using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Models;

namespace Lore.Services;

public sealed class WriterStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    public WriterStore() : this(AppFolder.Path) { }
    public WriterStore(string directory) => _path = Path.Combine(directory, "writer-briefs.json");

    public async Task<IReadOnlyList<WriterBrief>> LoadAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var held = await StoreFile.TryLockAsync(_path + ".lock", TimeSpan.FromSeconds(3))
            ?? throw new IOException("Saved Writers work is busy. Try again shortly.");
        return await ReadAsync();
    }

    public async Task<IReadOnlyList<WriterBrief>> SaveAsync(WriterBrief brief)
    {
        Validate(brief);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var held = await StoreFile.TryLockAsync(_path + ".lock", TimeSpan.FromSeconds(3))
            ?? throw new IOException("Saved Writers work is busy. The brief has not been saved; try again shortly.");
        // Read under the cross-process lock. A damaged file throws and is never replaced.
        var current = (await ReadAsync()).Where(b => b.Id != brief.Id).Append(brief).ToArray();
        string temp = StoreFile.TempPath(_path);
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(current, Options));
            StoreFile.Replace(temp, _path, _path + ".bak");
        }
        finally { StoreFile.DeleteTemp(temp); }
        return current;
    }

    private async Task<IReadOnlyList<WriterBrief>> ReadAsync()
    {
        if (!File.Exists(_path)) return [];
        var briefs = JsonSerializer.Deserialize<WriterBrief[]>(await File.ReadAllTextAsync(_path), Options)
            ?? throw new InvalidDataException("Saved Writers work is empty or damaged. The file has been left untouched.");
        foreach (var brief in briefs) Validate(brief);
        if (briefs.Select(b => b.Id).Distinct().Count() != briefs.Length) throw new InvalidDataException("Saved Writers work contains duplicate identities.");
        return briefs;
    }

    private static void Validate(WriterBrief brief)
    {
        if (brief is null || string.IsNullOrWhiteSpace(brief.Id) || string.IsNullOrWhiteSpace(brief.Title) || string.IsNullOrWhiteSpace(brief.CorpusVersion) ||
            brief.Births is null || brief.Births.Count is < 1 or > 3 || brief.Sections is null || brief.Sections.Count == 0 ||
            brief.Settings is null || brief.Settings.Orbs is null || !Enum.IsDefined(brief.Settings.Houses) || !Enum.IsDefined(brief.Settings.Node) || !Enum.IsDefined(brief.Settings.Lilith) ||
            brief.Births.Any(b => b is null || b.Person is null || !b.Person.HasBirthCoordinates || b.Utc.Year is < 1200 or > 2400) ||
            brief.Sections.Any(s => s is null || string.IsNullOrWhiteSpace(s.Heading) || s.Items is null || s.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Text))))
            throw new InvalidDataException("This Writers brief is incomplete or damaged. Existing saved work has been left untouched.");
    }
}
