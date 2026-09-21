using Lore.Models;

namespace Lore.Services;

// Traditional chart scoring (v1), following the Lilly/Dorothean rubric summarised in
// "Astrological Chart Evaluation Research". Scores the seven classical planets on:
//
//   Essential (by sign/degree):  Domicile +5, Exaltation +4, Triplicity +3,
//                                Detriment -5, Fall -4.
//   Accidental (by placement):   angular/succedent/cadent house per Lilly's table,
//                                motion (direct +4 / retrograde -5, non-luminaries),
//                                solar phase (cazimi +5 / combust -5 / under beams -4).
//
// Deferred to a later pass (would need large lookup tables or add little): Terms and
// Faces, the Peregrine penalty (can't be judged accurately without Terms/Faces),
// swift/slow motion, and partile benefic/malefic conjunctions.
public static class DignityService
{
    private static readonly Planet[] Classical =
        [Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter, Planet.Saturn];

    // Sign → its classical ruling planet (domicile).
    private static readonly Planet[] DomicileRuler =
    [
        Planet.Mars,    // Aries
        Planet.Venus,   // Taurus
        Planet.Mercury, // Gemini
        Planet.Moon,    // Cancer
        Planet.Sun,     // Leo
        Planet.Mercury, // Virgo
        Planet.Venus,   // Libra
        Planet.Mars,    // Scorpio
        Planet.Jupiter, // Sagittarius
        Planet.Saturn,  // Capricorn
        Planet.Saturn,  // Aquarius
        Planet.Jupiter, // Pisces
    ];

    // Planet → sign of exaltation (classical planets only).
    private static readonly Dictionary<Planet, ZodiacSign> ExaltationSign = new()
    {
        [Planet.Sun] = ZodiacSign.Aries,
        [Planet.Moon] = ZodiacSign.Taurus,
        [Planet.Mercury] = ZodiacSign.Virgo,
        [Planet.Venus] = ZodiacSign.Pisces,
        [Planet.Mars] = ZodiacSign.Capricorn,
        [Planet.Jupiter] = ZodiacSign.Cancer,
        [Planet.Saturn] = ZodiacSign.Libra,
    };

    // Element → (day ruler, night ruler) of the triplicity (Dorothean, per the research doc).
    private static readonly Dictionary<Element, (Planet day, Planet night)> Triplicity = new()
    {
        [Element.Fire]  = (Planet.Sun, Planet.Jupiter),
        [Element.Earth] = (Planet.Venus, Planet.Moon),
        [Element.Air]   = (Planet.Saturn, Planet.Mercury),
        [Element.Water] = (Planet.Venus, Planet.Mars),
    };

    // House (1..12) → accidental score (Lilly). Index 0 unused.
    private static readonly int[] HouseScore =
        [0, +5, +3, 0, +4, +3, -2, +4, -2, +2, +5, +4, -5];

    private const double CazimiOrb  = 17.0 / 60.0; // 0°17'
    private const double CombustOrb = 8.5;          // 8°30'
    private const double BeamsOrb   = 17.0;

    // Verdict thresholds on the aggregate score — the single tuning point (the scoring
    // rubric itself is untouched; only where we draw the band lines). Calibrated against
    // the full-info reference corpus (74 timed charts), whose totals run ≈ -7..+52 with a
    // median near +22. The bands give roughly Extraordinary 24% / Ordinary 59% /
    // Alarming 16% on that corpus — a broad, believable middle, a meaningful top quarter,
    // and a real (not vanishing) afflicted tail, so someone looking themselves up has a
    // fair chance of a notable result. Lower ExtraordinaryAt to widen the top; raise
    // AlarmingAt to widen the bottom.
    private const int ExtraordinaryAt = 31;
    private const int AlarmingAt       = 13;

    public static ChartScore Compute(NatalChart chart)
    {
        var sun = chart.GetPlanet(Planet.Sun);
        bool isDay = sun is not null && chart.GetHouseForLongitude(sun.Longitude) is >= 7 and <= 12;

        var rows = new List<PlanetDignity>(Classical.Length);
        int total = 0;
        foreach (var planet in Classical)
        {
            var pos = chart.GetPlanet(planet);
            if (pos is null) continue;

            var notes = new List<string>();
            int essential = Essential(planet, pos.Sign, isDay, notes);
            int accidental = Accidental(planet, pos, chart, sun, notes);

            var row = new PlanetDignity(planet, essential, accidental,
                notes.Count == 0 ? "peregrine (no scored dignity)" : string.Join(", ", notes));
            rows.Add(row);
            total += row.Total;
        }

        var verdict = total >= ExtraordinaryAt ? ChartVerdict.Extraordinary
                    : total <= AlarmingAt ? ChartVerdict.Alarming
                    : ChartVerdict.Ordinary;

        return new ChartScore(total, verdict, isDay, rows);
    }

    private static int Essential(Planet p, ZodiacSign sign, bool isDay, List<string> notes)
    {
        int score = 0;
        var opposite = (ZodiacSign)(((int)sign + 6) % 12);

        if (DomicileRuler[(int)sign] == p) { score += 5; notes.Add("domicile +5"); }
        if (ExaltationSign.TryGetValue(p, out var ex) && ex == sign) { score += 4; notes.Add("exaltation +4"); }

        var trip = Triplicity[sign.GetElement()];
        if ((isDay ? trip.day : trip.night) == p) { score += 3; notes.Add("triplicity +3"); }

        if (DomicileRuler[(int)opposite] == p) { score -= 5; notes.Add("detriment -5"); }
        if (ExaltationSign.TryGetValue(p, out var ex2) && ex2 == opposite) { score -= 4; notes.Add("fall -4"); }

        return score;
    }

    private static int Accidental(Planet p, PlanetPosition pos, NatalChart chart, PlanetPosition? sun, List<string> notes)
    {
        int score = 0;

        int house = chart.GetHouseForLongitude(pos.Longitude);
        int hs = HouseScore[house];
        if (hs != 0) { score += hs; notes.Add($"{Ordinal(house)} house {hs:+#;-#;0}"); }

        // Motion applies to the five non-luminaries; Sun/Moon are never retrograde.
        if (p is not (Planet.Sun or Planet.Moon))
        {
            if (pos.IsRetrograde) { score -= 5; notes.Add("retrograde -5"); }
            else { score += 4; notes.Add("direct +4"); }
        }

        // Solar phase: every classical planet except the Sun itself.
        if (p != Planet.Sun && sun is not null)
        {
            double sep = Separation(pos.Longitude, sun.Longitude);
            if (sep <= CazimiOrb) { score += 5; notes.Add("cazimi +5"); }
            else if (sep <= CombustOrb) { score -= 5; notes.Add("combust -5"); }
            else if (sep <= BeamsOrb) { score -= 4; notes.Add("under the Sun's beams -4"); }
        }

        return score;
    }

    private static double Separation(double a, double b)
    {
        double diff = Math.Abs(a - b) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th"
    };
}
