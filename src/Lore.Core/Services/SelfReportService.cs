using Lore.Models;
using System.Globalization;

namespace Lore.Services;

// What a person has reported about themselves, gathered from the store and scored: the
// latest finished sitting of each questionnaire, and the one before it. It can be asked
// for anyone with answers, not only the person on screen, which is how the Synastry and
// Trine views read a second and third person's.
public sealed class SelfReportService
{
    // The character and life-area answers are kept in inventories.json with the
    // personality answers, each under a key of its own, so that they have the same lock,
    // backup and recovery and stay out of a My Charts export in the same way.
    public static string StoreKey(string chartId, InventoryPart part) => part switch
    {
        InventoryPart.Character => chartId + "#character",
        InventoryPart.LifeAreas => chartId + "#life",
        _ => chartId,
    };

    private readonly InventoryService? _inventory;
    private readonly CharacterService? _character;
    private readonly LifeAreaService? _life;
    private readonly InventoryStore? _store;

    public SelfReportService(InventoryService? inventory, CharacterService? character, LifeAreaService? life, InventoryStore? store)
    {
        _inventory = inventory;
        _character = character;
        _life = life;
        _store = store;
    }

    public InventoryService? Inventory => _inventory;
    public CharacterService? Character => _character;
    public LifeAreaService? LifeAreas => _life;

    public static DateOnly TakenOn(InventorySitting sitting) =>
        DateOnly.TryParseExact(sitting.Completed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day : DateOnly.FromDateTime(DateTime.Now);

    // Finished sittings of the right length, oldest first.
    private List<InventorySitting> Finished(string chartId, InventoryPart part, int count) =>
        _store is not { IsReadable: true } store ? [] :
        store.Get(StoreKey(chartId, part)).Where(s => s.IsComplete && s.Answers.Length == count).ToList();

    public SelfReport Read(Celebrity person)
    {
        InventoryProfile? personality = null, earlierPersonality = null;
        CharacterProfile? character = null, earlierCharacter = null;
        LifeAreasReport? life = null;

        if (_inventory is { IsAvailable: true } inventory)
        {
            var done = Finished(person.Id, InventoryPart.Personality, inventory.Count);
            if (done.Count > 0) personality = inventory.Score(done[^1].Answers, person.Name, TakenOn(done[^1]));
            if (done.Count > 1) earlierPersonality = inventory.Score(done[^2].Answers, person.Name, TakenOn(done[^2]));
        }
        if (_character is { IsAvailable: true } scales)
        {
            var done = Finished(person.Id, InventoryPart.Character, scales.Count);
            if (done.Count > 0) character = scales.Score(done[^1].Answers, person.Name, TakenOn(done[^1]));
            if (done.Count > 1) earlierCharacter = scales.Score(done[^2].Answers, person.Name, TakenOn(done[^2]));
        }
        if (_life is { IsAvailable: true } areas)
        {
            var done = Finished(person.Id, InventoryPart.LifeAreas, areas.Count);
            if (done.Count > 0) life = areas.Score(done[^1].Answers, TakenOn(done[^1]));
        }

        return new SelfReport
        {
            Personality = personality,
            EarlierPersonality = earlierPersonality,
            Character = character,
            EarlierCharacter = earlierCharacter,
            LifeAreas = life,
        };
    }
}
