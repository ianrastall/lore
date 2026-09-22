using Lore.Models;

namespace Lore.Services;

// Traditional chart scoring, following the Lilly/Dorothean rubric summarised in
// "Astrological Chart Evaluation Research". Scores the seven classical planets on:
//
//   Essential (by sign/degree):  Domicile +5, Exaltation +4, Triplicity +3 (Dorothean
//                                triumvirate: day, night, and continuous participating
//                                ruler), Term (Egyptian bounds) +2, Face (Chaldean
//                                decan) +1, Detriment -5, Fall -4, Peregrine -5 (a
//                                planet holding none of the five dignities above).
//   Accidental (by placement):   angular/succedent/cadent house per Lilly's table,
//                                motion (direct +4 / retrograde -5, non-luminaries),
//                                solar phase (cazimi +5 / combust -5 / under beams -4).
//
// Deferred to a later pass (would add little for the effort): swift/slow motion and
// partile benefic/malefic conjunctions.
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

    // Element → (day, night, participating) rulers of the triplicity — the full Dorothean
    // triumvirate. The day/night ruler leads by sect; the participating ruler contributes
    // continuously regardless of sect (all three grant the same +3, per the research doc).
    private static readonly Dictionary<Element, (Planet day, Planet night, Planet part)> Triplicity = new()
    {
        [Element.Fire]  = (Planet.Sun, Planet.Jupiter, Planet.Saturn),
        [Element.Earth] = (Planet.Venus, Planet.Moon, Planet.Mars),
        [Element.Air]   = (Planet.Saturn, Planet.Mercury, Planet.Jupiter),
        [Element.Water] = (Planet.Venus, Planet.Mars, Planet.Moon),
    };

    // Egyptian terms (bounds): each sign is split into five unequal segments, each ruled
    // by a planet. Stored as (ruler, upper-degree boundary); a planet in a segment it
    // rules gains +2. Indexed by sign (Aries=0 … Pisces=11).
    private static readonly (Planet ruler, double upper)[][] Terms =
    [
        [(Planet.Jupiter, 6), (Planet.Venus, 12), (Planet.Mercury, 20), (Planet.Mars, 25), (Planet.Saturn, 30)],   // Aries
        [(Planet.Venus, 8), (Planet.Mercury, 14), (Planet.Jupiter, 22), (Planet.Saturn, 27), (Planet.Mars, 30)],   // Taurus
        [(Planet.Mercury, 6), (Planet.Jupiter, 12), (Planet.Venus, 17), (Planet.Mars, 24), (Planet.Saturn, 30)],   // Gemini
        [(Planet.Mars, 7), (Planet.Venus, 13), (Planet.Mercury, 19), (Planet.Jupiter, 26), (Planet.Saturn, 30)],   // Cancer
        [(Planet.Jupiter, 6), (Planet.Venus, 11), (Planet.Saturn, 18), (Planet.Mercury, 24), (Planet.Mars, 30)],   // Leo
        [(Planet.Mercury, 7), (Planet.Venus, 17), (Planet.Jupiter, 21), (Planet.Mars, 28), (Planet.Saturn, 30)],   // Virgo
        [(Planet.Saturn, 6), (Planet.Mercury, 14), (Planet.Jupiter, 21), (Planet.Venus, 28), (Planet.Mars, 30)],   // Libra
        [(Planet.Mars, 7), (Planet.Venus, 11), (Planet.Mercury, 19), (Planet.Jupiter, 24), (Planet.Saturn, 30)],   // Scorpio
        [(Planet.Jupiter, 12), (Planet.Venus, 17), (Planet.Mercury, 21), (Planet.Saturn, 26), (Planet.Mars, 30)],  // Sagittarius
        [(Planet.Mercury, 7), (Planet.Jupiter, 14), (Planet.Venus, 22), (Planet.Saturn, 26), (Planet.Mars, 30)],   // Capricorn
        [(Planet.Mercury, 7), (Planet.Venus, 13), (Planet.Jupiter, 20), (Planet.Mars, 25), (Planet.Saturn, 30)],   // Aquarius
        [(Planet.Venus, 12), (Planet.Jupiter, 16), (Planet.Mercury, 19), (Planet.Mars, 28), (Planet.Saturn, 30)],  // Pisces
    ];

    // Faces (decans): each sign's three 10° thirds, ruled in the Chaldean planetary order
    // (Saturn, Jupiter, Mars, Sun, Venus, Mercury, Moon…) beginning with Mars at 0° Aries.
    // A planet in a face it rules gains +1. Indexed [sign][0..2].
    private static readonly Planet[][] Faces =
    [
        [Planet.Mars, Planet.Sun, Planet.Venus],        // Aries
        [Planet.Mercury, Planet.Moon, Planet.Saturn],   // Taurus
        [Planet.Jupiter, Planet.Mars, Planet.Sun],      // Gemini
        [Planet.Venus, Planet.Mercury, Planet.Moon],    // Cancer
        [Planet.Saturn, Planet.Jupiter, Planet.Mars],   // Leo
        [Planet.Sun, Planet.Venus, Planet.Mercury],     // Virgo
        [Planet.Moon, Planet.Saturn, Planet.Jupiter],   // Libra
        [Planet.Mars, Planet.Sun, Planet.Venus],        // Scorpio
        [Planet.Mercury, Planet.Moon, Planet.Saturn],   // Sagittarius
        [Planet.Jupiter, Planet.Mars, Planet.Sun],      // Capricorn
        [Planet.Venus, Planet.Mercury, Planet.Moon],    // Aquarius
        [Planet.Saturn, Planet.Jupiter, Planet.Mars],   // Pisces
    ];

    // House (1..12) → accidental score (Lilly). Index 0 unused.
    private static readonly int[] HouseScore =
        [0, +5, +3, 0, +4, +3, -2, +4, -2, +2, +5, +4, -5];

    private const double CazimiOrb  = 17.0 / 60.0; // 0°17'
    private const double CombustOrb = 8.5;          // 8°30'
    private const double BeamsOrb   = 17.0;

    // Verdict thresholds on the aggregate score — the single tuning point (the scoring
    // rubric itself is untouched; only where we draw the band lines). Recalibrated for the
    // full five-tier essential system (v1.1) against the timed reference corpus (150 timed
    // charts), whose totals run ≈ -24..+47 with a median near +14. The bands give roughly
    // Extraordinary 23% / Ordinary 61% / Alarming 16% on that corpus — a broad, believable
    // middle, a meaningful top quarter, and a real (not vanishing) afflicted tail, so
    // someone looking themselves up has a fair chance of a notable result. Lower
    // ExtraordinaryAt to widen the top; raise AlarmingAt to widen the bottom.
    private const int ExtraordinaryAt = 27;
    private const int AlarmingAt       = -1;

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
            int essential = Essential(planet, pos, isDay, notes);
            int accidental = Accidental(planet, pos, chart, sun, notes);

            var row = new PlanetDignity(planet, essential, accidental, string.Join(", ", notes));
            rows.Add(row);
            total += row.Total;
        }

        var verdict = total >= ExtraordinaryAt ? ChartVerdict.Extraordinary
                    : total <= AlarmingAt ? ChartVerdict.Alarming
                    : ChartVerdict.Ordinary;

        return new ChartScore(total, verdict, isDay, rows);
    }

    private static int Essential(Planet p, PlanetPosition pos, bool isDay, List<string> notes)
    {
        var sign = pos.Sign;
        double deg = pos.DegreeInSign;
        int score = 0;
        bool anyDignity = false;
        var opposite = (ZodiacSign)(((int)sign + 6) % 12);

        if (DomicileRuler[(int)sign] == p) { score += 5; anyDignity = true; notes.Add("domicile +5"); }
        if (ExaltationSign.TryGetValue(p, out var ex) && ex == sign) { score += 4; anyDignity = true; notes.Add("exaltation +4"); }

        var trip = Triplicity[sign.GetElement()];
        if ((isDay ? trip.day : trip.night) == p) { score += 3; anyDignity = true; notes.Add("triplicity +3"); }
        else if (trip.part == p) { score += 3; anyDignity = true; notes.Add("triplicity (participating) +3"); }

        if (TermRuler(sign, deg) == p) { score += 2; anyDignity = true; notes.Add("term +2"); }
        if (Faces[(int)sign][Math.Min(2, (int)(deg / 10))] == p) { score += 1; anyDignity = true; notes.Add("face +1"); }

        bool debilitated = false;
        if (DomicileRuler[(int)opposite] == p) { score -= 5; debilitated = true; notes.Add("detriment -5"); }
        if (ExaltationSign.TryGetValue(p, out var ex2) && ex2 == opposite) { score -= 4; debilitated = true; notes.Add("fall -4"); }

        // Peregrine: a planet "wandering" with no share in its sign — neither essential
        // dignity nor debility (detriment/fall) by sign. It has no resources of its own.
        // (Detriment/fall are their own, separate debilities and are not also peregrine.)
        if (!anyDignity && !debilitated) { score -= 5; notes.Add("peregrine -5"); }

        return score;
    }

    // The ruling planet of the Egyptian term (bound) that contains this degree of the sign.
    private static Planet TermRuler(ZodiacSign sign, double degInSign)
    {
        foreach (var (ruler, upper) in Terms[(int)sign])
            if (degInSign < upper) return ruler;
        return Terms[(int)sign][^1].ruler; // exactly 30° (shouldn't occur) → last term
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
