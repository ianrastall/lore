using Lore.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System.Numerics;
using Windows.UI;

namespace Lore.Views;

// Draws a traditional Western natal chart onto a Win2D ICanvasDrawingSession.
internal static class ChartRenderer
{
    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color Background   = Color.FromArgb(255, 18, 18, 30);
    private static readonly Color RingOuter    = Color.FromArgb(255, 45, 45, 65);
    private static readonly Color RingInner    = Color.FromArgb(255, 30, 30, 50);
    private static readonly Color LineColor    = Color.FromArgb(160, 120, 120, 160);
    private static readonly Color HouseColor   = Color.FromArgb(100, 180, 180, 220);
    private static readonly Color AngularColor = Color.FromArgb(255, 230, 200, 100);

    private static readonly Color[] ElementColors =
    [
        Color.FromArgb(220, 220, 80, 60),   // Fire  – warm red
        Color.FromArgb(220, 80, 180, 80),   // Earth – green
        Color.FromArgb(220, 90, 160, 230),  // Air   – sky blue
        Color.FromArgb(220, 60, 180, 200),  // Water – teal
    ];

    private static readonly Color[] AspectColors =
    [
        Color.FromArgb(160, 220, 220, 220), // Conjunction  – white
        Color.FromArgb(160, 90, 160, 230),  // Sextile      – blue
        Color.FromArgb(160, 220, 60, 60),   // Square       – red
        Color.FromArgb(160, 80, 200, 100),  // Trine        – green
        Color.FromArgb(160, 220, 140, 40),  // Opposition   – orange
        Color.FromArgb(160, 150, 150, 170), // Semi-sextile   – grey
        Color.FromArgb(160, 200, 110, 110), // Semi-square    – dull red
        Color.FromArgb(160, 200, 110, 110), // Sesquiquadrate – dull red
        Color.FromArgb(160, 170, 130, 200), // Quincunx       – violet
    ];

    // Minor aspects are drawn dashed, so they never read as one of the five majors.
    private static readonly Microsoft.Graphics.Canvas.Geometry.CanvasStrokeStyle MinorStroke =
        new() { DashStyle = Microsoft.Graphics.Canvas.Geometry.CanvasDashStyle.Dash };

    // ── Layout fractions (of chart radius) ───────────────────────────────────
    private const float OuterRing   = 1.00f;
    private const float SignOuter   = 0.95f;
    private const float SignInner   = 0.78f;
    private const float HouseOuter  = 0.78f;
    private const float PlanetRing  = 0.63f;
    private const float AspectInner = 0.45f;

    // `selected`: a planet picked in the list beside the wheel. Its glyph is ringed and
    // its aspects drawn at full strength, with every other aspect faded back.
    // `showVerdict`: the coloured dignity rim; off for a chart that is not a birth chart
    // (a solar return), where the score would mean nothing.
    public static void Draw(CanvasDrawingSession ds, NatalChart chart, float width, float height,
        Planet? selected = null, bool showVerdict = true)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        float r = Math.Min(cx, cy) * 0.93f;

        ds.Clear(Background);

        DrawZodiacRing(ds, cx, cy, r, WheelAsc(chart));
        if (chart.Timed) DrawHouses(ds, cx, cy, r, chart);
        DrawAspects(ds, cx, cy, r * AspectInner, chart, selected);
        DrawPlanets(ds, cx, cy, r, chart);
        if (selected is { } s && chart.GetPlanet(s) is { } sp)
        {
            var at = ToPoint(cx, cy, r * (HouseOuter * 0.88f), sp.Longitude, WheelAsc(chart));
            ds.DrawCircle(at, r * 0.022f, AngularColor, 2f);
        }
        if (chart.Timed) DrawAngles(ds, cx, cy, r, chart);
        if (showVerdict) DrawVerdictRim(ds, cx, cy, r, chart);
    }

    // The longitude the wheel is turned to, at 9 o'clock: the Ascendant, or for a chart
    // with no birth time (no Ascendant, no houses, no angles drawn) 0° Aries.
    private static double WheelAsc(NatalChart chart) => chart.Timed ? chart.Ascendant : 0;

    // ── Bi-wheel (synastry) ──────────────────────────────────────────────────
    // Two charts on one wheel. The first person's chart is drawn as usual but smaller,
    // in the middle; the second person's planets go in a band around it, at the same
    // zodiac positions, so a conjunction between the two shows as glyphs side by side.
    // The wheel is turned to the first person's Ascendant and carries their houses.
    private const float BiSignInner  = 0.83f;  // zodiac band, 0.95 → 0.83
    private const float BiOuterGlyph = 0.755f; // second person's planets
    private const float BiDivider    = 0.68f;  // ring between the two people
    private const float BiInnerGlyph = 0.585f; // first person's planets
    private const float BiAspect     = 0.45f;  // circle the aspect lines are strung across

    private static readonly Color OuterAngleColor = Color.FromArgb(220, 150, 200, 235);

    public static void DrawBiWheel(CanvasDrawingSession ds, Synastry synastry, float width, float height)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        float r = Math.Min(cx, cy) * 0.93f;
        NatalChart inner = synastry.First, outer = synastry.Second;
        double asc = WheelAsc(inner);

        ds.Clear(Background);

        DrawZodiacRing(ds, cx, cy, r, asc, BiSignInner, centred: true);
        ds.DrawEllipse(cx, cy, r * BiDivider, r * BiDivider, RingOuter, 1.5f);
        if (inner.Timed) DrawHouses(ds, cx, cy, r, inner, BiDivider, BiAspect + 0.045f);
        DrawSynastryAspects(ds, cx, cy, r * BiAspect, synastry);
        DrawPlanets(ds, cx, cy, r, inner, asc, BiInnerGlyph, BiDivider - 0.02f, 0.062f, centred: true);
        DrawPlanets(ds, cx, cy, r, outer, asc, BiOuterGlyph, BiSignInner - 0.015f, 0.062f, centred: true);
        if (inner.Timed) DrawAngles(ds, cx, cy, r, inner);
        if (outer.Timed)
        {
            DrawOuterAngle(ds, cx, cy, r, outer.Ascendant, asc, "ASC");
            DrawOuterAngle(ds, cx, cy, r, outer.Midheaven, asc, "MC");
        }
        DrawBiWheelKey(ds, r, height, inner, outer);
    }

    // The contacts between the two charts, each a line from the first person's point to
    // the second's. The ones the reading writes up are drawn heavier.
    private static void DrawSynastryAspects(CanvasDrawingSession ds, float cx, float cy, float ringR, Synastry synastry)
    {
        double asc = WheelAsc(synastry.First);
        foreach (var aspect in synastry.Aspects)
        {
            if (synastry.First.LongitudeOf(aspect.First) is not { } lonA ||
                synastry.Second.LongitudeOf(aspect.Second) is not { } lonB) continue;

            var color = AspectColors[(int)aspect.Type];
            double closeness = 1 - aspect.Orb / Services.SynastryService.Orb(aspect.Type);
            byte alpha = (byte)(aspect.Shown ? 150 + (int)(closeness * 90) : 45 + (int)(closeness * 70));
            color = Color.FromArgb(alpha, color.R, color.G, color.B);

            ds.DrawLine(ToPoint(cx, cy, ringR, lonA, asc), ToPoint(cx, cy, ringR, lonB, asc),
                        color, aspect.Shown ? 1.75f : 0.9f);
        }
    }

    // The second person's Ascendant or Midheaven: a tick across their band, with a label.
    private static void DrawOuterAngle(CanvasDrawingSession ds, float cx, float cy, float r,
        double longitude, double asc, string label)
    {
        using var fmt = new CanvasTextFormat
        {
            FontSize = r * 0.03f,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        ds.DrawLine(ToPoint(cx, cy, r * BiDivider, longitude, asc),
                    ToPoint(cx, cy, r * BiSignInner, longitude, asc), OuterAngleColor, 2f);
        // Tucked under the zodiac ring, clear of the planet glyphs in the middle of the band.
        ds.DrawText(label, ToPoint(cx, cy, r * (BiSignInner - 0.032f), longitude + 3.5, asc), OuterAngleColor, fmt);
    }

    // Which wheel is whose, in the bottom-left corner (so it carries into the PNG and PDF).
    private static void DrawBiWheelKey(CanvasDrawingSession ds, float r, float height, NatalChart inner, NatalChart outer)
    {
        float size = r * 0.042f;
        using var fmt = new CanvasTextFormat { FontSize = size, WordWrapping = CanvasWordWrapping.NoWrap };
        float x = size * 0.6f;
        ds.DrawText($"Inner wheel: {inner.Celebrity.Name}", x, height - size * 3.2f, HouseColor, fmt);
        ds.DrawText($"Outer wheel: {outer.Celebrity.Name}", x, height - size * 1.8f, OuterAngleColor, fmt);
    }

    // A coloured halo just outside the zodiac ring for a notable chart — green for an
    // Extraordinary dignity score, red for an Alarming one. Ordinary charts get nothing,
    // so the outliers read at a glance (and it carries into the exported PNG/PDF).
    private static void DrawVerdictRim(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart)
    {
        var verdict = Services.DignityService.ComputeIfTimed(chart)?.Verdict;
        Color? rim = verdict switch
        {
            ChartVerdict.Extraordinary => Color.FromArgb(255, 63, 184, 79),  // green
            ChartVerdict.Alarming      => Color.FromArgb(255, 229, 83, 75),  // red
            _                          => null,
        };
        if (rim is { } c)
            ds.DrawEllipse(cx, cy, r * (SignOuter + 0.03f), r * (SignOuter + 0.03f), c, 4f);
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────

    // Maps an ecliptic longitude to a point on a circle of given radius, in the standard
    // Western layout: Ascendant pinned at 9 o'clock (left) and the zodiac running
    // counter-clockwise (increasing longitude downward from the ASC). That places the
    // Midheaven near the top and the IC near the bottom — matching astro.com and the
    // familiar wheel. Screen y grows downward, so the sin term is ADDED (a vertical
    // mirror of the naive math layout, which would otherwise put the MC at the bottom).
    private static Vector2 ToPoint(float cx, float cy, float r, double longitude, double asc)
    {
        double relative = ((longitude - asc) % 360 + 360) % 360;
        double mathDeg = 180.0 - relative;
        double rad = mathDeg * Math.PI / 180.0;
        return new Vector2(cx + r * (float)Math.Cos(rad), cy + r * (float)Math.Sin(rad));
    }

    // ── Zodiac ring ──────────────────────────────────────────────────────────

    private static void DrawZodiacRing(CanvasDrawingSession ds, float cx, float cy, float r, double asc,
        float signInner = SignInner, bool centred = false)
    {
        using var fmt = new CanvasTextFormat { FontSize = r * 0.07f, HorizontalAlignment = CanvasHorizontalAlignment.Center };
        if (centred) fmt.VerticalAlignment = CanvasVerticalAlignment.Center;

        for (int i = 0; i < 12; i++)
        {
            var sign = (ZodiacSign)i;
            double startLon = i * 30.0;
            double midLon = startLon + 15.0;

            // Segment boundary line
            var inner = ToPoint(cx, cy, r * signInner, startLon, asc);
            var outer = ToPoint(cx, cy, r * SignOuter, startLon, asc);
            ds.DrawLine(inner, outer, LineColor, 1.5f);

            // Sign symbol at mid-segment
            var mid = ToPoint(cx, cy, r * (signInner + (SignOuter - signInner) / 2f), midLon, asc);
            var el = sign.GetElement();
            ds.DrawText(sign.Symbol(), mid, ElementColors[(int)el], fmt);
        }

        // Outer and inner ring circles
        ds.DrawEllipse(cx, cy, r * SignOuter, r * SignOuter, RingOuter, 2.5f);
        ds.DrawEllipse(cx, cy, r * signInner, r * signInner, RingOuter, 1.5f);
    }

    // ── Houses ───────────────────────────────────────────────────────────────

    private static void DrawHouses(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart,
        float houseOuter = HouseOuter, float labelRing = AspectInner + 0.06f)
    {
        using var fmt = new CanvasTextFormat { FontSize = r * 0.045f, HorizontalAlignment = CanvasHorizontalAlignment.Center };

        for (int i = 0; i < 12; i++)
        {
            var cusp = chart.Houses[i];
            var nextCusp = chart.Houses[(i + 1) % 12];

            // House cusp line from center to inner ring
            var pt = ToPoint(cx, cy, r * houseOuter, cusp.Longitude, chart.Ascendant);
            ds.DrawLine(new Vector2(cx, cy), pt, HouseColor, 1.25f);

            // House number at midpoint of sector
            double mid = MidArc(cusp.Longitude, nextCusp.Longitude);
            var labelPt = ToPoint(cx, cy, r * labelRing, mid, chart.Ascendant);
            ds.DrawText((i + 1).ToString(), labelPt, HouseColor, fmt);
        }
    }

    private static double MidArc(double start, double end)
    {
        double s = ((start % 360) + 360) % 360;
        double e = ((end % 360) + 360) % 360;
        if (e < s) e += 360;
        double mid = (s + e) / 2.0;
        return mid % 360;
    }

    // ── Angles (ASC / MC / DSC / IC) ─────────────────────────────────────────

    private static void DrawAngles(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart)
    {
        using var fmt = new CanvasTextFormat { FontSize = r * 0.05f, HorizontalAlignment = CanvasHorizontalAlignment.Center };
        DrawAngleLine(ds, cx, cy, r, chart.Ascendant, chart.Ascendant, "ASC", fmt);
        DrawAngleLine(ds, cx, cy, r, chart.Midheaven, chart.Ascendant, "MC", fmt);
        DrawAngleLine(ds, cx, cy, r, (chart.Ascendant + 180) % 360, chart.Ascendant, "DSC", fmt);
        DrawAngleLine(ds, cx, cy, r, (chart.Midheaven + 180) % 360, chart.Ascendant, "IC", fmt);
    }

    private static void DrawAngleLine(CanvasDrawingSession ds, float cx, float cy, float r,
        double longitude, double asc, string label, CanvasTextFormat fmt)
    {
        var outer = ToPoint(cx, cy, r * SignOuter, longitude, asc);
        var inner = ToPoint(cx, cy, r * SignInner, longitude, asc);
        ds.DrawLine(new Vector2(cx, cy), outer, AngularColor, 2.5f);
        var labelPt = ToPoint(cx, cy, r * (SignOuter + 0.03f), longitude, asc);
        ds.DrawText(label, labelPt, AngularColor, fmt);
    }

    // ── Aspects ──────────────────────────────────────────────────────────────

    private static void DrawAspects(CanvasDrawingSession ds, float cx, float cy, float innerR, NatalChart chart, Planet? selected = null)
    {
        // With a planet selected: its own aspects at full strength, the rest held back.
        byte Shade(byte alpha, bool involved) =>
            selected is null ? alpha : involved ? (byte)230 : (byte)(alpha / 5);

        foreach (var aspect in chart.Aspects)
        {
            var pA = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetA);
            var pB = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetB);
            if (pA is null || pB is null) continue;

            var ptA = ToPoint(cx, cy, innerR, pA.Longitude, WheelAsc(chart));
            var ptB = ToPoint(cx, cy, innerR, pB.Longitude, WheelAsc(chart));
            var color = AspectColors[(int)aspect.Type];

            // Fade strong-orb aspects
            double allowed = aspect.Allowed > 0 ? aspect.Allowed : aspect.Type.Orb();
            byte alpha = (byte)(160 - (int)(Math.Min(1, aspect.Orb / allowed) * 100));
            bool involved = aspect.PlanetA == selected || aspect.PlanetB == selected;
            color = Color.FromArgb(Shade(alpha, involved), color.R, color.G, color.B);
            float width = selected is not null && involved ? 2.25f : 1.25f;

            if (aspect.Type.IsMajor()) ds.DrawLine(ptA, ptB, color, width);
            else ds.DrawLine(ptA, ptB, color, width - 0.25f, MinorStroke);
        }

        // Aspects to the Ascendant and Midheaven: from the planet to the angle's own
        // place on the circle, a little lighter so the planets' aspects still lead.
        foreach (var aspect in chart.AngleAspects)
        {
            if (chart.GetPlanet(aspect.Planet) is not { } p || chart.LongitudeOf(aspect.Angle) is not { } angleLon) continue;

            var color = AspectColors[(int)aspect.Type];
            byte alpha = (byte)(120 - (int)(Math.Min(1, aspect.Orb / aspect.Allowed) * 75));
            var from = ToPoint(cx, cy, innerR, p.Longitude, WheelAsc(chart));
            var to = ToPoint(cx, cy, innerR, angleLon, WheelAsc(chart));
            bool involved = aspect.Planet == selected;
            var faded = Color.FromArgb(Shade(alpha, involved), color.R, color.G, color.B);
            float width = selected is not null && involved ? 2f : 1f;
            if (aspect.Type.IsMajor()) ds.DrawLine(from, to, faded, width);
            else ds.DrawLine(from, to, faded, width, MinorStroke);
        }
    }

    // ── Planets ──────────────────────────────────────────────────────────────

    private static void DrawPlanets(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart) =>
        DrawPlanets(ds, cx, cy, r, chart, WheelAsc(chart), PlanetRing, HouseOuter * 0.88f, 0.072f);

    // `asc` is the Ascendant the wheel is turned to: the chart's own, or in a bi-wheel
    // the inner chart's for both rings. `centred` sets each glyph squarely on its ring
    // rather than hanging below it, which the bi-wheel's narrower bands need.
    private static void DrawPlanets(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart,
        double asc, float glyphRing, float dotRing, float glyphSize, bool centred = false)
    {
        using var symFmt = new CanvasTextFormat { FontSize = r * glyphSize, HorizontalAlignment = CanvasHorizontalAlignment.Center };
        if (centred) symFmt.VerticalAlignment = CanvasVerticalAlignment.Center;

        // Cluster avoidance: track used angles to offset overlapping planets
        var usedAngles = new List<double>();

        foreach (var planet in chart.Planets)
        {
            double displayLon = ResolveOverlap(planet.Longitude, asc, usedAngles, r);
            usedAngles.Add(displayLon);

            var el = planet.Sign.GetElement();
            var color = ElementColors[(int)el];

            var pt = ToPoint(cx, cy, r * glyphRing, displayLon, asc);

            // Small dot at exact position
            var exactPt = ToPoint(cx, cy, r * dotRing, planet.Longitude, asc);
            ds.FillEllipse(exactPt, 3f, 3f, color);
            ds.DrawLine(exactPt, pt, Color.FromArgb(90, color.R, color.G, color.B), 0.75f);

            ds.DrawText(planet.Planet.Symbol(), pt, color, symFmt);

            // Retrograde marker
            if (planet.IsRetrograde)
            {
                using var retFmt = new CanvasTextFormat { FontSize = r * 0.038f };
                var retPt = new Vector2(pt.X + r * 0.04f, pt.Y + r * (centred ? 0.008f : 0.05f));
                ds.DrawText("℞", retPt, Color.FromArgb(180, 200, 160, 100), retFmt);
            }
        }
    }

    private static double ResolveOverlap(double longitude, double asc, List<double> used, float r)
    {
        const double minSep = 7.0; // degrees
        double candidate = longitude;
        double relative = ((longitude - asc) % 360 + 360) % 360;

        int attempts = 0;
        while (used.Any(u =>
        {
            double uRel = ((u - asc) % 360 + 360) % 360;
            double diff = Math.Abs(relative - uRel);
            if (diff > 180) diff = 360 - diff;
            return diff < minSep;
        }) && attempts++ < 8)
        {
            relative += minSep;
            if (relative >= 360) relative -= 360;
        }

        return (asc + relative) % 360;
    }
}
