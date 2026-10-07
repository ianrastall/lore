using Lore.Models;

namespace Lore.Services;

// Answers "what if the birth time is a little off?". The chart is recalculated at every
// minute from `minutes` before the recorded time to `minutes` after, and each thing a
// reading leans on — the Rising sign, the Midheaven's sign, day or night, every body's
// sign and house — is followed across the window. Checking only the two ends would miss
// something that changes and changes back in between.
public static class TimeSensitivityService
{
    // Wide enough for "some time that afternoon"; a chart with no time at all is a
    // different question (the whole day), handled separately.
    public const int MaxMinutes = 180;

    // The check that suits a chart: a margin either side of its birth time, or the whole
    // day if it has none. Made with the settings the chart itself was cast with.
    public static TimeSensitivity? For(ChartService charts, NatalChart chart, int minutes,
        CancellationToken cancel = default)
    {
        charts = charts.With(chart.Settings);
        return chart.Timed
            ? Analyse(charts, chart.Celebrity, minutes, cancel)
            : AnalyseDay(charts, chart.Celebrity, cancel);
    }

    // Null when there is nothing to test: no birth time, or no margin given.
    public static TimeSensitivity? Analyse(ChartService charts, Celebrity person, int minutes,
        CancellationToken cancel = default)
    {
        if (!person.BirthTimeKnown || minutes <= 0) return null;
        minutes = Math.Min(minutes, MaxMinutes);

        var centre = BirthTimeResolver.ToUtc(person);
        var samples = new NatalChart[2 * minutes + 1];
        for (int i = 0; i < samples.Length; i++)
        {
            cancel.ThrowIfCancellationRequested();
            samples[i] = charts.CalculateAt(person, centre.AddMinutes(i - minutes));
        }

        // Clock time at the birthplace for sample i, as the birth time itself is given.
        var recorded = person.GetBirthTime();
        string Clock(int i) => recorded.AddMinutes(i - minutes).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var holds = new List<string>();
        var changes = new List<string>();
        NatalChart first = samples[0], last = samples[^1];

        // ── The angles ────────────────────────────────────────────────────────
        Follow("Rising sign", samples, c => ZodiacSignExtensions.FromLongitude(c.Ascendant).Name(), Clock,
            stable: sign => $"Rising sign: {sign} throughout (Ascendant {Position(first.Ascendant)} to {Position(last.Ascendant)})",
            holds, changes);
        Follow("Midheaven", samples, c => ZodiacSignExtensions.FromLongitude(c.Midheaven).Name(), Clock,
            stable: sign => $"Midheaven: {sign} throughout ({Position(first.Midheaven)} to {Position(last.Midheaven)})",
            holds, changes);
        Follow("Sect", samples, c => c.IsDayChart ? "a day chart" : "a night chart", Clock,
            stable: sect => $"Sect: {sect} throughout",
            holds, changes);

        // ── Signs ─────────────────────────────────────────────────────────────
        var bodies = first.Planets.Select(p => p.Planet).ToList();
        int signsHeld = 0;
        foreach (var body in bodies)
        {
            var before = changes.Count;
            Follow(body.Name(), samples, c => c.GetPlanet(body)?.Sign.Name() ?? "", Clock, stable: null, holds, changes);
            if (changes.Count == before) signsHeld++;
        }
        if (signsHeld == bodies.Count)
            holds.Add($"Signs: every body keeps its sign (the Moon moves from {Position(first.GetPlanet(Planet.Moon)?.Longitude)} to {Position(last.GetPlanet(Planet.Moon)?.Longitude)})");
        else if (signsHeld > 0)
            holds.Add($"Signs: the other {signsHeld} bodies keep their signs");

        // ── Houses ────────────────────────────────────────────────────────────
        int housesHeld = 0;
        foreach (var body in bodies)
        {
            var before = changes.Count;
            Follow(body.Name(), samples,
                c => c.GetPlanet(body) is { } p ? $"the {ChartInterpreter.Ordinal(c.GetHouseForLongitude(p.Longitude))} house" : "",
                Clock, stable: null, holds, changes);
            if (changes.Count == before) housesHeld++;
        }
        if (housesHeld == bodies.Count)
            holds.Add($"Houses: every body stays in its house ({first.HouseSystemLabel})");
        else if (housesHeld > 0)
            holds.Add($"Houses: the other {housesHeld} bodies stay in their houses ({first.HouseSystemLabel})");

        return new TimeSensitivity
        {
            Minutes = minutes,
            Window = $"{Clock(0)} to {Clock(samples.Length - 1)}, clock time at the birthplace",
            Summary = changes.Count == 0
                ? $"Nothing a reading relies on changes within {minutes} minutes either way."
                : $"{changes.Count} {(changes.Count == 1 ? "thing depends" : "things depend")} on the exact time.",
            Holds = holds,
            Changes = changes,
        };
    }

    // The same question for a chart with no birth time at all: the whole calendar day at
    // the birthplace. There are no angles or houses to follow, only signs — and it is the
    // Moon, covering some 13° in a day, that most often changes. Null for a chart that
    // has a birth time.
    //
    // The sky is sampled every ten minutes from local midnight to the next local midnight
    // (23 or 25 hours on a day the clocks changed). No body can enter a sign and leave it
    // again in ten minutes, so comparing neighbouring samples finds every change, and
    // each one is then narrowed to the minute.
    public static TimeSensitivity? AnalyseDay(ChartService charts, Celebrity person, CancellationToken cancel = default)
    {
        if (person.BirthTimeKnown) return null;

        var (startUtc, endUtc, clock) = BirthTimeResolver.LocalDay(person);
        const int StepMinutes = 10;
        int steps = (int)Math.Ceiling((endUtc - startUtc).TotalMinutes / StepMinutes);
        // The last sample is one minute short of the next midnight: still this day.
        DateTime At(int i) => i >= steps ? endUtc.AddMinutes(-1) : startUtc.AddMinutes(i * StepMinutes);
        double Jd(DateTime utc) => SwissEphemeris.DateTimeToJulianDay(utc);

        var samples = new IReadOnlyList<PlanetPosition>[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            cancel.ThrowIfCancellationRequested();
            samples[i] = charts.CalculateSky(Jd(At(i)));
        }

        PlanetPosition? Find(IReadOnlyList<PlanetPosition> sky, Planet body) => sky.FirstOrDefault(p => p.Planet == body);

        // The first minute at which `body` is no longer in `from`, between two samples.
        DateTime ChangeTime(Planet body, ZodiacSign from, DateTime lo, DateTime hi)
        {
            while ((hi - lo).TotalMinutes > 1)
            {
                var mid = lo.AddMinutes(Math.Floor((hi - lo).TotalMinutes / 2));
                if (charts.CalculateBody(Jd(mid), body)?.Sign == from) lo = mid; else hi = mid;
            }
            return hi;
        }

        var holds = new List<string>();
        var changes = new List<string>();
        var moonSigns = new List<ZodiacSign>();
        var bodies = samples[0].Select(p => p.Planet).ToList();
        int held = 0;
        foreach (var body in bodies)
        {
            if (Find(samples[0], body) is not { } first) continue;
            var current = first.Sign;
            var parts = new List<string> { current.Name() };
            if (body == Planet.Moon) moonSigns.Add(current);

            for (int i = 1; i <= steps; i++)
            {
                if (Find(samples[i], body)?.Sign is not { } next || next == current) continue;
                parts[^1] += $" until {clock(ChangeTime(body, current, At(i - 1), At(i)))}";
                parts.Add(next.Name());
                current = next;
                if (body == Planet.Moon) moonSigns.Add(next);
            }

            if (parts.Count > 1)
                changes.Add($"{body.Name()}: {string.Join(", then ", parts)}");
            else
            {
                held++;
                if (body == Planet.Moon)
                    holds.Add($"Moon: {current.Name()} all day, somewhere from {Position(first.Longitude)} to {Position(Find(samples[^1], body)?.Longitude)}");
            }
        }
        if (held == bodies.Count)
            holds.Add("Signs: every body keeps its sign all day");
        else if (held > 0)
            holds.Add($"Signs: the other {held} bodies keep their signs all day");
        holds.Add("The Rising sign, Midheaven and houses can't be known without a birth time");

        return new TimeSensitivity
        {
            Minutes = (int)((endUtc - startUtc).TotalMinutes / 2),
            WholeDay = true,
            Window = "The whole day, midnight to midnight at the birthplace",
            Summary = changes.Count == 0
                ? "Every sign is the same whatever time of day the birth was."
                : $"{changes.Count} {(changes.Count == 1 ? "sign depends" : "signs depend")} on the time of day.",
            Holds = holds,
            Changes = changes,
            MoonSigns = moonSigns.Distinct().ToList(),
        };
    }

    // Follows one quality across the window. If it never changes, `stable` (when given)
    // writes the line for Holds; if it does, the sequence goes to Changes as
    // "Rising sign: Cancer until 11:38, then Leo".
    private static void Follow<T>(
        string subject, T[] samples, Func<T, string> state, Func<int, string> clock,
        Func<string, string>? stable, List<string> holds, List<string> changes)
    {
        string current = state(samples[0]);
        var parts = new List<string> { current };
        for (int i = 1; i < samples.Length; i++)
        {
            string next = state(samples[i]);
            if (next == current) continue;
            parts[^1] += $" until {clock(i)}";
            parts.Add(next);
            current = next;
        }

        if (parts.Count == 1)
        {
            if (stable is not null) holds.Add(stable(current));
        }
        else
        {
            changes.Add($"{subject}: {string.Join(", then ", parts)}");
        }
    }

    private static string Position(double? longitude) => longitude is not { } lon ? "" :
        $"{ZodiacSignExtensions.FormatDegreeInSign(lon)} {ZodiacSignExtensions.FromLongitude(lon).Name()}";
}
