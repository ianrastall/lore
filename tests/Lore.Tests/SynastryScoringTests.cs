using System.Text.Json;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The five-step compatibility scale, and the search of everyone against one chart.
public class SynastryScoringTests
{
    private static SynastryAspect Contact(Planet first, AspectType type, Planet second, double orb = 0) =>
        new() { First = NatalPoint.Of(first), Second = NatalPoint.Of(second), Type = type, Orb = orb };

    private static readonly (Planet a, Planet b)[] Pairs =
    [
        (Planet.Sun, Planet.Moon), (Planet.Moon, Planet.Sun), (Planet.Sun, Planet.Mercury), (Planet.Moon, Planet.Mercury),
        (Planet.Mercury, Planet.Sun), (Planet.Mercury, Planet.Moon), (Planet.Sun, Planet.Sun), (Planet.Moon, Planet.Moon),
        (Planet.Mercury, Planet.Mercury), (Planet.Sun, Planet.Jupiter),
    ];

    // Ten exact contacts between personal points, `easy` of them trines and the rest squares.
    private static Compatibility Mix(int easy) => SynastryScoring.Assess(
        Pairs.Select((p, i) => Contact(p.a, i < easy ? AspectType.Trine : AspectType.Square, p.b)));

    [Fact]
    public void The_scale_runs_from_plus_two_to_minus_two()
    {
        Assert.Equal(2, Mix(10).Level);
        Assert.Equal(SynastryTone.Soulmates, Mix(10).Tone);
        Assert.Equal(-2, Mix(0).Level);
        Assert.Equal(SynastryTone.Adversaries, Mix(0).Tone);

        // More easy contacts never lower the level.
        var levels = Enumerable.Range(0, 11).Select(n => Mix(n).Level).ToList();
        Assert.Equal(levels.OrderBy(x => x), levels);
        Assert.Contains(0, levels);
        Assert.Contains(1, levels);
        Assert.Contains(-1, levels);
    }

    [Fact]
    public void A_pair_joined_by_one_stray_contact_is_not_called_soulmates()
    {
        var one = SynastryScoring.Assess([Contact(Planet.Sun, AspectType.Trine, Planet.Moon)]);
        Assert.Equal(1.0, one.Share, 9);
        Assert.Equal(SynastryTone.Harmonious, one.Tone);

        var hard = SynastryScoring.Assess([Contact(Planet.Sun, AspectType.Square, Planet.Moon)]);
        Assert.Equal(SynastryTone.Challenging, hard.Tone);
    }

    [Fact]
    public void Contacts_between_two_slow_planets_count_for_nothing()
    {
        var c = SynastryScoring.Assess([Contact(Planet.Neptune, AspectType.Square, Planet.Pluto)]);
        Assert.Equal(SynastryTone.Light, c.Tone);
        Assert.Equal(0, c.Level);
    }

    [Fact]
    public void A_conjunction_takes_its_colour_from_the_planets_meeting()
    {
        Assert.True(SynastryScoring.Assess([Contact(Planet.Sun, AspectType.Conjunction, Planet.Venus)]).Net > 0);
        Assert.True(SynastryScoring.Assess([Contact(Planet.Sun, AspectType.Conjunction, Planet.Saturn)]).Net < 0);
    }

    [Fact]
    public void The_search_leaves_out_the_chart_itself_and_runs_best_to_worst()
    {
        var einstein = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        var ranked = SynastryScoring.Rank(Repo.Charts, einstein, Repo.Figures);

        Assert.Equal(Repo.Figures.Count - 1, ranked.Count);
        Assert.DoesNotContain(ranked, m => m.Person.Id == "albert-einstein");
        Assert.Equal(ranked.OrderByDescending(m => m.Compatibility.Level).Select(m => m.Compatibility.Level),
                     ranked.Select(m => m.Compatibility.Level));
        Assert.True(ranked[0].Compatibility.Share > ranked[^1].Compatibility.Share);

        // The search and the reading must agree on any one pair.
        var curie = ranked.Single(m => m.Person.Id == "marie-curie");
        var reading = new SynastryInterpreter(Repo.Data("synastry.json"))
            .Compose(SynastryService.Compare(einstein, Repo.Charts.Calculate(Repo.Figure("marie-curie"))));
        Assert.Equal(reading.Tone, curie.Compatibility.Tone);
        Assert.Equal(reading.Compatibility, curie.Compatibility);
    }

    [Fact]
    public void Compatibility_is_the_same_whichever_chart_comes_first()
    {
        var a = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        var b = Repo.Charts.Calculate(Repo.Figure("frida-kahlo"));
        var ab = SynastryScoring.Assess(SynastryService.Compare(a, b).Aspects);
        var ba = SynastryScoring.Assess(SynastryService.Compare(b, a).Aspects);
        Assert.Equal(ab.Tone, ba.Tone);
        Assert.Equal(ab.Share, ba.Share, 9);
    }

    [Fact]
    public void The_bands_keep_their_proportions_across_the_library()
    {
        var charts = Repo.Figures.Select(f => Repo.Charts.Calculate(f)).ToList();
        var counts = new Dictionary<int, int>();
        int pairs = 0;
        for (int i = 0; i < charts.Count; i++)
            for (int j = i + 1; j < charts.Count; j++, pairs++)
            {
                int level = SynastryScoring.Assess(SynastryService.Compare(charts[i], charts[j]).Aspects).Level;
                counts[level] = counts.GetValueOrDefault(level) + 1;
            }
        double Share(int level) => counts.GetValueOrDefault(level) / (double)pairs;

        // About 7% / 18% / 50% / 18% / 7%. If the library grows and one drifts out of
        // range, reset the shares in SynastryScoring.
        Assert.InRange(Share(2), 0.05, 0.10);
        Assert.InRange(Share(1), 0.14, 0.22);
        Assert.InRange(Share(0), 0.44, 0.56);
        Assert.InRange(Share(-1), 0.14, 0.22);
        Assert.InRange(Share(-2), 0.05, 0.10);
    }

    [Fact]
    public void The_corpus_has_a_sentence_for_every_tone()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("synastry.json")));
        var tones = doc.RootElement.GetProperty("tones");
        foreach (var tone in Enum.GetValues<SynastryTone>())
            Assert.False(string.IsNullOrWhiteSpace(tones.GetProperty(tone.ToString()).GetString()), tone.ToString());
    }
}
