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
    ];

    // ── Layout fractions (of chart radius) ───────────────────────────────────
    private const float OuterRing   = 1.00f;
    private const float SignOuter   = 0.95f;
    private const float SignInner   = 0.78f;
    private const float HouseOuter  = 0.78f;
    private const float PlanetRing  = 0.63f;
    private const float AspectInner = 0.45f;

    public static void Draw(CanvasDrawingSession ds, NatalChart chart, float width, float height)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        float r = Math.Min(cx, cy) * 0.93f;

        ds.Clear(Background);

        DrawZodiacRing(ds, cx, cy, r, chart.Ascendant);
        DrawHouses(ds, cx, cy, r, chart);
        DrawAspects(ds, cx, cy, r * AspectInner, chart);
        DrawPlanets(ds, cx, cy, r, chart);
        DrawAngles(ds, cx, cy, r, chart);
        DrawVerdictRim(ds, cx, cy, r, chart);
    }

    // A coloured halo just outside the zodiac ring for a notable chart — green for an
    // Extraordinary dignity score, red for an Alarming one. Ordinary charts get nothing,
    // so the outliers read at a glance (and it carries into the exported PNG/PDF).
    private static void DrawVerdictRim(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart)
    {
        var verdict = Services.DignityService.Compute(chart).Verdict;
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

    private static void DrawZodiacRing(CanvasDrawingSession ds, float cx, float cy, float r, double asc)
    {
        var fmt = new CanvasTextFormat { FontSize = r * 0.07f, HorizontalAlignment = CanvasHorizontalAlignment.Center };

        for (int i = 0; i < 12; i++)
        {
            var sign = (ZodiacSign)i;
            double startLon = i * 30.0;
            double midLon = startLon + 15.0;

            // Segment boundary line
            var inner = ToPoint(cx, cy, r * SignInner, startLon, asc);
            var outer = ToPoint(cx, cy, r * SignOuter, startLon, asc);
            ds.DrawLine(inner, outer, LineColor, 1.5f);

            // Sign symbol at mid-segment
            var mid = ToPoint(cx, cy, r * (SignInner + (SignOuter - SignInner) / 2f), midLon, asc);
            var el = sign.GetElement();
            ds.DrawText(sign.Symbol(), mid, ElementColors[(int)el], fmt);
        }

        // Outer and inner ring circles
        ds.DrawEllipse(cx, cy, r * SignOuter, r * SignOuter, RingOuter, 2.5f);
        ds.DrawEllipse(cx, cy, r * SignInner, r * SignInner, RingOuter, 1.5f);
    }

    // ── Houses ───────────────────────────────────────────────────────────────

    private static void DrawHouses(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart)
    {
        var fmt = new CanvasTextFormat { FontSize = r * 0.045f, HorizontalAlignment = CanvasHorizontalAlignment.Center };

        for (int i = 0; i < 12; i++)
        {
            var cusp = chart.Houses[i];
            var nextCusp = chart.Houses[(i + 1) % 12];

            // House cusp line from center to inner ring
            var pt = ToPoint(cx, cy, r * HouseOuter, cusp.Longitude, chart.Ascendant);
            ds.DrawLine(new Vector2(cx, cy), pt, HouseColor, 1.25f);

            // House number at midpoint of sector
            double mid = MidArc(cusp.Longitude, nextCusp.Longitude);
            var labelPt = ToPoint(cx, cy, r * (AspectInner + 0.06f), mid, chart.Ascendant);
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
        var fmt = new CanvasTextFormat { FontSize = r * 0.05f, HorizontalAlignment = CanvasHorizontalAlignment.Center };
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

    private static void DrawAspects(CanvasDrawingSession ds, float cx, float cy, float innerR, NatalChart chart)
    {
        foreach (var aspect in chart.Aspects)
        {
            var pA = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetA);
            var pB = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetB);
            if (pA is null || pB is null) continue;

            var ptA = ToPoint(cx, cy, innerR, pA.Longitude, chart.Ascendant);
            var ptB = ToPoint(cx, cy, innerR, pB.Longitude, chart.Ascendant);
            var color = AspectColors[(int)aspect.Type];

            // Fade strong-orb aspects
            byte alpha = (byte)(160 - (int)(aspect.Orb / aspect.Type.Orb() * 100));
            color = Color.FromArgb(alpha, color.R, color.G, color.B);

            ds.DrawLine(ptA, ptB, color, 1.25f);
        }
    }

    // ── Planets ──────────────────────────────────────────────────────────────

    private static void DrawPlanets(CanvasDrawingSession ds, float cx, float cy, float r, NatalChart chart)
    {
        var symFmt = new CanvasTextFormat { FontSize = r * 0.072f, HorizontalAlignment = CanvasHorizontalAlignment.Center };

        // Cluster avoidance: track used angles to offset overlapping planets
        var usedAngles = new List<double>();

        foreach (var planet in chart.Planets)
        {
            double displayLon = ResolveOverlap(planet.Longitude, chart.Ascendant, usedAngles, r);
            usedAngles.Add(displayLon);

            var el = planet.Sign.GetElement();
            var color = ElementColors[(int)el];

            var pt = ToPoint(cx, cy, r * PlanetRing, displayLon, chart.Ascendant);

            // Small dot at exact position
            var exactPt = ToPoint(cx, cy, r * HouseOuter * 0.88f, planet.Longitude, chart.Ascendant);
            ds.FillEllipse(exactPt, 3f, 3f, color);
            ds.DrawLine(exactPt, pt, Color.FromArgb(90, color.R, color.G, color.B), 0.75f);

            ds.DrawText(planet.Planet.Symbol(), pt, color, symFmt);

            // Retrograde marker
            if (planet.IsRetrograde)
            {
                var retFmt = new CanvasTextFormat { FontSize = r * 0.038f };
                var retPt = new Vector2(pt.X + r * 0.04f, pt.Y + r * 0.05f);
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
