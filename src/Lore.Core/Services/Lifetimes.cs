using Lore.Models;

namespace Lore.Services;

// Whether two people could conceivably have known each other: whether they were alive at
// the same time. Any overlap counts, however brief (a newborn and someone of ninety), as
// does a shared day. Three lifetimes that overlap in pairs always share a moment, so a
// trio needs no test of its own.
public static class Lifetimes
{
    public static bool Overlap(Celebrity first, Celebrity second)
    {
        if (Born(first) is not { } a || Born(second) is not { } b) return false;
        return a <= Died(second) && b <= Died(first);
    }

    private static DateOnly? Born(Celebrity person) =>
        Celebrity.TryParseDate(person.BirthDate, person.JulianCalendar, out int year, out int month, out int day)
            ? BirthTimeResolver.GregorianDate(year, month, day, person.JulianCalendar)
            : null;

    // The last day of a lifetime. My Charts people count as living, whatever a file brought
    // in from elsewhere may say.
    private static DateOnly Died(Celebrity person) =>
        person.Category != UserChartService.MyChartsCategory &&
        Celebrity.TryParseDate(person.DeathDate, julian: false, out int year, out int month, out int day)
            ? new DateOnly(year, month, day)
            : DateOnly.MaxValue;
}
