using System.Globalization;
using System.Text.Json.Serialization;

namespace Lore.Models;

// The personality inventory as it is kept in Data\inventory.json: the IPIP-NEO-120, its
// 120 statements with their scoring keys, the 30 facets and five traits they measure,
// the reference figures each is set against, and Lore's own description of a low, a
// typical and a high score on each. It has nothing to do with any chart.
public sealed class InventoryInstrument
{
    public InventoryInfo Instrument { get; init; } = new();
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<InventoryDomain> Domains { get; init; } = [];
    public IReadOnlyList<InventoryFacet> Facets { get; init; } = [];
    public IReadOnlyList<InventoryItem> Items { get; init; } = [];
}

public sealed class InventoryInfo
{
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Source { get; init; } = "";
    public string Licence { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string Norms { get; init; } = "";
}

// One of the five broad traits. Mean and Sd are the reference group's, on the scale a
// trait is scored on: the average of its six facets, 4 to 20.
public sealed class InventoryDomain
{
    public string Key { get; init; } = "";       // N, E, O, A, C
    public string Name { get; init; } = "";
    public double Mean { get; init; }
    public double Sd { get; init; }
    public string About { get; init; } = "";
    public string Low { get; init; } = "";
    public string Typical { get; init; } = "";
    public string High { get; init; } = "";
}

// One of the 30 facets, six to a trait. Mean and Sd are on the facet's own scale: the
// sum of its four statements, 4 to 20.
public sealed class InventoryFacet
{
    public string Key { get; init; } = "";       // N1 … C6
    public string Domain { get; init; } = "";
    public string Name { get; init; } = "";
    public double Mean { get; init; }
    public double Sd { get; init; }
    public string Measures { get; init; } = "";
    public string Low { get; init; } = "";
    public string Typical { get; init; } = "";
    public string High { get; init; } = "";
}

// One statement. Reversed: agreeing with it counts towards a low score on its facet.
public sealed class InventoryItem
{
    public int N { get; init; }                  // 1 to 120, the order they are asked in
    public string Text { get; init; } = "";
    public string Facet { get; init; } = "";
    public bool Reversed { get; init; }
}

// Where a score stands against the reference group: within one standard deviation of
// its average (Typical: about two people in three), beyond that (Low, High: about one
// in six each way), or beyond two (VeryLow, VeryHigh: about one in forty).
public enum ScoreBand { VeryLow, Low, Typical, High, VeryHigh }

public static class ScoreBandExtensions
{
    public static string Label(this ScoreBand band) => band switch
    {
        ScoreBand.VeryLow => "Very low",
        ScoreBand.Low => "Low",
        ScoreBand.High => "High",
        ScoreBand.VeryHigh => "Very high",
        _ => "Typical",
    };

    // The band a score falls in, from how many standard deviations it is from the average.
    public static ScoreBand Of(double z) =>
        z <= -2 ? ScoreBand.VeryLow : z < -1 ? ScoreBand.Low : z >= 2 ? ScoreBand.VeryHigh : z > 1 ? ScoreBand.High : ScoreBand.Typical;
}

// One facet as scored. Raw is the sum of its four answers (4 to 20); Z how many standard
// deviations that is from the reference group's average.
public sealed record FacetScore(InventoryFacet Facet, int Raw, double Z)
{
    public ScoreBand Band => ScoreBandExtensions.Of(Z);
    public double Percentile => InventoryProfile.PercentileOf(Z);

    // For the profile's list (x:Bind cannot call extension methods).
    public string Name => Facet.Name;
    public string Measures => Facet.Measures;
    public string BandLabel => Band.Label();
    public string ScoreText => $"{Raw} of 20";
    public string Text => Band switch
    {
        ScoreBand.VeryLow or ScoreBand.Low => Facet.Low,
        ScoreBand.VeryHigh or ScoreBand.High => Facet.High,
        _ => Facet.Typical,
    };
}

// One trait as scored: the average of its six facets, and those facets.
public sealed record DomainScore(InventoryDomain Domain, double Score, double Z, IReadOnlyList<FacetScore> Facets)
{
    public ScoreBand Band => ScoreBandExtensions.Of(Z);
    public double Percentile => InventoryProfile.PercentileOf(Z);

    public string Name => Domain.Name;
    public string About => Domain.About;
    public string BandLabel => Band.Label();
    public string ScoreText => $"{Score.ToString("0.0", CultureInfo.InvariantCulture)} of 20";
    public string PercentileText => InventoryProfile.PercentileText(Percentile);
    public string Text => Band switch
    {
        ScoreBand.VeryLow or ScoreBand.Low => Domain.Low,
        ScoreBand.VeryHigh or ScoreBand.High => Domain.High,
        _ => Domain.Typical,
    };
}

// A completed sitting, scored: the five traits, each with its six facets.
public sealed class InventoryProfile
{
    public required string Name { get; init; }
    public required DateOnly TakenOn { get; init; }
    public required IReadOnlyList<DomainScore> Domains { get; init; }

    // The share of the reference group scoring lower, 0 to 100, taking scores to be
    // spread in the usual bell shape. A guide, not a measurement: four statements do
    // not place anyone to the nearest point.
    public static double PercentileOf(double z)
    {
        // Abramowitz and Stegun 26.2.17, good to seven places.
        double t = 1 / (1 + 0.2316419 * Math.Abs(z));
        double tail = Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI) *
                      t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return 100 * (z >= 0 ? 1 - tail : tail);
    }

    // "higher than about 85 in 100", kept away from the two ends, which no short
    // questionnaire can tell apart.
    public static string PercentileText(double percentile) =>
        $"higher than about {Math.Clamp((int)Math.Round(percentile / 5) * 5, 5, 95)} in 100";
}

// One sitting of the questionnaire as it is saved: the answers, 1 to 5 in the order of
// the instrument's choices and 0 for a statement not yet answered, and the day it was
// finished (null while it is under way).
public sealed class InventorySitting
{
    [JsonPropertyName("started")]
    public string Started { get; set; } = "";       // yyyy-MM-dd

    [JsonPropertyName("completed")]
    public string? Completed { get; set; }          // yyyy-MM-dd

    [JsonPropertyName("answers")]
    public int[] Answers { get; set; } = [];

    [JsonIgnore]
    public bool IsComplete => Completed is not null;
}

// How the answers describe a planet's part of life: lived well, lived with strain, or
// neither in particular.
public enum MirrorLived { Plain, Well, Strain }

// What the chart leads one to expect of a planet, from its dignity: Unknown without a
// birth time, when Lore does not score it.
public enum MirrorExpectation { Unknown, Easy, Middling, Strained }

// The two set against each other. Unlived: an easy placement lived with strain.
// HardWon: a hard placement lived well.
public enum MirrorVerdict { NoContrast, AsWritten, AsWrittenHard, Unlived, HardWon }

// One dial of a planet set against what its sign leads one to expect. Gap is how far the
// answers are above (+) or below (−) that, in steps of low, mid, high; nought is a match
// at high or at low (a match at mid is not recorded: there is nothing to say of it).
// Clause is the comparison in words, to be set into a sentence.
public sealed record MirrorComparison(string Dial, string Expected, string Actual, int Gap, string Clause);

// One planet as the chart-and-answers reading has it.
public sealed record MirrorPlanet(
    Planet Planet, PlanetPosition Position, string Expression, MirrorLived Lived, int? Dignity, MirrorExpectation Expectation)
{
    public string Text { get; init; } = "";
    public string Stands { get; init; } = "";
    public string DignityNotes { get; init; } = "";
    public IReadOnlyList<FacetScore> Facets { get; init; } = [];

    // True when every one of its dials is middling, so that it has only the catch-all
    // expression: the answers say nothing in particular of this planet.
    public bool IsOrdinary { get; init; }

    // Each of its dials against its sign, where there is anything to say; and that put
    // into a line for the Chart and answers reading. Empty for Uranus and Neptune, whose
    // sign belongs to a generation and not to a person.
    public IReadOnlyList<MirrorComparison> Comparisons { get; init; } = [];
    public string AgainstSign { get; init; } = "";

    // What this adds to the planet's paragraph in the Report; empty for none.
    public string ReportLine { get; init; } = "";

    // What is added to a transit to this planet in the Daily and Forecast readings: one
    // line for a square or opposition, one for a sextile or trine. Empty for none.
    public string DailyHard { get; init; } = "";
    public string DailyEasy { get; init; } = "";

    // The same for a pass in the Forecast, which lasts days or weeks and is not "today".
    public string ForecastHard { get; init; } = "";
    public string ForecastEasy { get; init; } = "";

    // The line for a transit of a given tone. A conjunction takes whichever line goes
    // with how the planet is lived: the hard one under strain, the easy one otherwise.
    public string DailyLine(TransitTone tone) => Hard(tone) ? DailyHard : DailyEasy;
    public string ForecastLine(TransitTone tone) => Hard(tone) ? ForecastHard : ForecastEasy;

    private bool Hard(TransitTone tone) => tone switch
    {
        TransitTone.Tension => true,
        TransitTone.Flow => false,
        _ => Lived == MirrorLived.Strain,
    };

    public MirrorVerdict Verdict => (Expectation, Lived) switch
    {
        (MirrorExpectation.Easy, MirrorLived.Well) => MirrorVerdict.AsWritten,
        (MirrorExpectation.Easy, MirrorLived.Strain) => MirrorVerdict.Unlived,
        (MirrorExpectation.Strained, MirrorLived.Strain) => MirrorVerdict.AsWrittenHard,
        (MirrorExpectation.Strained, MirrorLived.Well) => MirrorVerdict.HardWon,
        _ => MirrorVerdict.NoContrast,
    };
}

// The reading that sets a person's inventory profile beside their chart.
public sealed class MirrorReading
{
    // The chart it was made for. The other readings take wording from it only for this
    // chart, so that one person's answers can never colour another's reading.
    public required string ChartId { get; init; }

    // A closing section for the Report: what in it comes from the answers, and any
    // planet whose placement and answers disagree.
    public IReadOnlyList<string> ReportParagraphs { get; init; } = [];

    // What the Report's Overview takes: a sentence each for "Sun", "Moon" and "Rising",
    // where there is one, and a line on the five broad traits taken together.
    public IReadOnlyDictionary<string, string> OverviewLines { get; init; } = new Dictionary<string, string>();
    public string Summary { get; init; } = "";

    // A sentence for an aspect of the birth chart, by AspectKey; and for an element or
    // mode the chart leans toward or lacks.
    public IReadOnlyDictionary<string, string> AspectLines { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<Element, string> ElementLines { get; init; } = new Dictionary<Element, string>();
    public IReadOnlyDictionary<Modality, string> ModalityLines { get; init; } = new Dictionary<Modality, string>();

    public static string AspectKey(NatalPoint a, NatalPoint b, AspectType type) => $"{a.Name}|{type}|{b.Name}";

    public string AspectLine(NatalPoint a, NatalPoint b, AspectType type) =>
        AspectLines.GetValueOrDefault(AspectKey(a, b, type)) ?? AspectLines.GetValueOrDefault(AspectKey(b, a, type)) ?? "";

    // The planet as this reading has it, if the reading is this chart's and reads it.
    public MirrorPlanet? For(NatalChart chart, Planet planet) =>
        chart.Celebrity.Id == ChartId ? Planets.FirstOrDefault(p => p.Planet == planet) : null;

    public bool IsFor(NatalChart chart) => chart.Celebrity.Id == ChartId;

    public required string Name { get; init; }
    public required DateOnly TakenOn { get; init; }
    public required IReadOnlyList<MirrorPlanet> Planets { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }
}
