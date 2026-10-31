using System.Globalization;

namespace Lore.Models;

// The character scales as they are kept in Data\character.json: ten IPIP scales for the
// character dimensions of Cloninger's Temperament and Character Inventory, with Lore's own
// description of a low, a typical and a high score. There are no reference figures for
// them, so a score is placed on the five-point scale itself (see CharacterScore).
public sealed class CharacterInstrument
{
    public InventoryInfo Instrument { get; init; } = new();
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<CharacterDimension> Dimensions { get; init; } = [];
    public IReadOnlyList<CharacterScale> Scales { get; init; } = [];
    public IReadOnlyList<CharacterItem> Items { get; init; } = [];
}

// One of the three: Self-Directedness, Cooperativeness, Self-Transcendence.
public sealed class CharacterDimension
{
    public string Key { get; init; } = "";       // SD, CO, ST
    public string Name { get; init; } = "";
    public string About { get; init; } = "";
    public string Low { get; init; } = "";
    public string Typical { get; init; } = "";
    public string High { get; init; } = "";
}

public sealed class CharacterScale
{
    public string Key { get; init; } = "";       // SD1 … ST2
    public string Dimension { get; init; } = "";
    public string Name { get; init; } = "";
    public double Alpha { get; init; }           // the reliability the IPIP gives for it
    public string Measures { get; init; } = "";
    public string Low { get; init; } = "";
    public string Typical { get; init; } = "";
    public string High { get; init; } = "";
}

public sealed class CharacterItem
{
    public int N { get; init; }
    public string Text { get; init; } = "";
    public string Scale { get; init; } = "";
    public bool Reversed { get; init; }
}

// One scale as scored: Mean is the average answer, 1 to 5, after reversing the statements
// keyed the other way. Z puts it in the same terms as a facet's so that the rules which
// read one can read the other: nought at 3.5, and one either way at 3.0 and 4.0. There is
// no reference group behind it, and the wording never says there is.
public sealed record CharacterScore(CharacterScale Scale, double Mean)
{
    public const double Middle = 3.5, Step = 0.5;

    public double Z => (Mean - Middle) / Step;
    public ScoreBand Band => ScoreBandExtensions.Of(Z);

    public string Name => Scale.Name;
    public string Measures => Scale.Measures;
    public string BandLabel => Band.Label();
    public string ScoreText => $"{Mean.ToString("0.0", CultureInfo.InvariantCulture)} of 5";
    public double Bar => (Mean - 1) * 25;
    public string Text => Band switch
    {
        ScoreBand.VeryLow or ScoreBand.Low => Scale.Low,
        ScoreBand.VeryHigh or ScoreBand.High => Scale.High,
        _ => Scale.Typical,
    };
}

public sealed record CharacterDimensionScore(CharacterDimension Dimension, double Mean, IReadOnlyList<CharacterScore> Scales)
{
    public double Z => (Mean - CharacterScore.Middle) / CharacterScore.Step;
    public ScoreBand Band => ScoreBandExtensions.Of(Z);

    public string Name => Dimension.Name;
    public string About => Dimension.About;
    public string BandLabel => Band.Label();
    public string ScoreText => $"{Mean.ToString("0.0", CultureInfo.InvariantCulture)} of 5";
    public double Bar => (Mean - 1) * 25;
    public string Text => Band switch
    {
        ScoreBand.VeryLow or ScoreBand.Low => Dimension.Low,
        ScoreBand.VeryHigh or ScoreBand.High => Dimension.High,
        _ => Dimension.Typical,
    };
}

public sealed class CharacterProfile
{
    public required string Name { get; init; }
    public required DateOnly TakenOn { get; init; }
    public required IReadOnlyList<CharacterDimensionScore> Dimensions { get; init; }
}

// The life-areas check-in as it is kept in Data\lifeareas.json: one statement for each
// house of the chart, with what the readings add when it is rated low or high.
public sealed class LifeAreaInstrument
{
    public string Prompt { get; init; } = "";
    public IReadOnlyList<string> Choices { get; init; } = [];
    public IReadOnlyList<LifeArea> Areas { get; init; } = [];
    public LifeForecast Forecast { get; init; } = new();
}

// One statement. Answers are numbered by house, so area 1 is the first house.
public sealed class LifeArea
{
    public int House { get; init; }
    public string Name { get; init; } = "";
    public string Statement { get; init; } = "";
    public LifeLines Low { get; init; } = new();
    public LifeLines High { get; init; } = new();
}

public sealed class LifeLines
{
    public string Report { get; init; } = "";
    public string Hard { get; init; } = "";      // on a square or opposition, in the Daily
    public string Easy { get; init; } = "";      // on a sextile or trine, in the Daily
    public string Year { get; init; } = "";      // on an annual profection that reaches the house
}

// A pass in the Forecast lasts days or weeks, and takes these in place of the Daily's.
public sealed class LifeForecast
{
    public string LowHard { get; init; } = "";
    public string LowEasy { get; init; } = "";
    public string HighHard { get; init; } = "";
    public string HighEasy { get; init; } = "";
}

// One area as rated, 1 to 5: only the two ends of the scale add anything to a reading.
public sealed record LifeAreaRating(LifeArea Area, int Rating, string Choice)
{
    public bool IsLow => Rating <= 2;
    public bool IsHigh => Rating == 5;
    public bool IsMarked => IsLow || IsHigh;
    public LifeLines? Lines => IsLow ? Area.Low : IsHigh ? Area.High : null;

    // For the list in the Inventory view.
    public string Name => Area.Name;
    public string HouseText => $"House {Area.House}";
    public double Bar => (Rating - 1) * 25;
}

public sealed class LifeAreasReport
{
    // Life moves on: ratings older than this are not used by the other readings.
    public const int FreshDays = 183;

    public required DateOnly TakenOn { get; init; }
    public required IReadOnlyList<LifeAreaRating> Ratings { get; init; }
    public LifeForecast Forecast { get; init; } = new();

    public bool IsFreshOn(DateOnly day) => day.DayNumber - TakenOn.DayNumber <= FreshDays;
}

// Everything a person has reported about themselves, at its latest: any of it may be
// missing. The earlier sittings are there to say what has moved since.
public sealed class SelfReport
{
    public InventoryProfile? Personality { get; init; }
    public InventoryProfile? EarlierPersonality { get; init; }
    public CharacterProfile? Character { get; init; }
    public CharacterProfile? EarlierCharacter { get; init; }
    public LifeAreasReport? LifeAreas { get; init; }

    public bool HasAny => Personality is not null || Character is not null || LifeAreas is not null;
}

// The three questionnaires a person can answer.
public enum InventoryPart { Personality, Character, LifeAreas }
