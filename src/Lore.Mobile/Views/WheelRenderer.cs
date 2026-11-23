using Lore.Models;
using SkiaSharp;

namespace Lore.Mobile.Views;

// The chart wheels, drawn with SkiaSharp. A port of the Windows app's Win2D
// ChartRenderer (src/Lore.App/Views/ChartRenderer.cs): the same layout, colours and
// rules, so a wheel reads the same on either. A change to one belongs in the other.
//
// Sizes are in pixels. `px` is pixels per device-independent unit, so that lines and
// dots keep their weight on a dense phone screen; text is sized from the radius.
internal sealed class WheelRenderer
{
    // ── Colors ────────────────────────────────────────────────────────────────
    public static readonly SKColor Background = new(28, 27, 25);   // JupiterWindowColor
    private static readonly SKColor RingOuter    = new(43, 40, 36);
    private static readonly SKColor LineColor    = new(140, 132, 120, 160);
    private static readonly SKColor HouseColor   = new(200, 192, 178, 100);
    private static readonly SKColor AngularColor = new(230, 200, 100);
    private static readonly SKColor OuterAngleColor = new(150, 200, 235, 220);
    private static readonly SKColor GoldenColor  = new(230, 180, 34);
    private static readonly SKColor MotionColor  = new(200, 160, 100, 180);

    private static readonly SKColor[] ElementColors =
    [
        new(220, 80, 60, 220),   // Fire  – warm red
        new(80, 180, 80, 220),   // Earth – green
        new(90, 160, 230, 220),  // Air   – sky blue
        new(60, 180, 200, 220),  // Water – teal
    ];

    private static readonly SKColor[] AspectColors =
    [
        new(220, 220, 220, 160), // Conjunction  – white
        new(90, 160, 230, 160),  // Sextile      – blue
        new(220, 60, 60, 160),   // Square       – red
        new(80, 200, 100, 160),  // Trine        – green
        new(220, 140, 40, 160),  // Opposition   – orange
        new(150, 150, 170, 160), // Semi-sextile   – grey
        new(200, 110, 110, 160), // Semi-square    – dull red
        new(200, 110, 110, 160), // Sesquiquadrate – dull red
        new(170, 130, 200, 160), // Quincunx       – violet
        new(210, 190, 90, 160),  // Quintile       – pale gold
        new(210, 190, 90, 160),  // Biquintile     – pale gold
    ];

    // ── Layout fractions (of chart radius) ───────────────────────────────────
    private const float SignOuter   = 0.95f;
    private const float SignInner   = 0.78f;
    private const float HouseOuter  = 0.78f;
    private const float PlanetRing  = 0.63f;
    private const float AspectInner = 0.45f;

    private const float BiSignInner  = 0.83f;
    private const float BiOuterGlyph = 0.755f;
    private const float BiDivider    = 0.68f;
    private const float BiInnerGlyph = 0.585f;
    private const float BiAspect     = 0.45f;

    private const float TriThird      = 0.755f;
    private const float TriSecondEdge = 0.68f;
    private const float TriSecond     = 0.61f;
    private const float TriFirstEdge  = 0.545f;
    private const float TriFirst      = 0.475f;
    private const float TriInnerEdge  = 0.41f;
    private const float TriTriangles  = 0.385f;
    private const int   TriTriangleCount = 3;

    private readonly SKCanvas _c;
    private readonly float _px, _cx, _cy, _r, _height;

    private WheelRenderer(SKCanvas canvas, float width, float height, float px)
    {
        _c = canvas;
        _px = px;
        _cx = width / 2f;
        _cy = height / 2f;
        _r = Math.Min(_cx, _cy) * 0.93f;
        _height = height;
        canvas.Clear(Background);
    }

    // ── Fonts ─────────────────────────────────────────────────────────────────
    // The glyphs come from the two Noto Sans Symbols fonts packed with the app (set by
    // WheelFonts.LoadAsync); anything neither has, from the phone's own fonts.
    public static SKTypeface? Symbols { get; set; }
    public static SKTypeface? Symbols2 { get; set; }

    private void Text(string text, float x, float y, SKColor color, float size, bool centreV, SKTextAlign align = SKTextAlign.Center)
    {
        using var font = new SKFont(PickTypeface(text), size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var m = font.Metrics;
        // Win2D places text from the top of its line, or its middle; Skia from the baseline.
        float baseline = centreV ? y - (m.Ascent + m.Descent) / 2f : y - m.Ascent;
        _c.DrawText(text, x, baseline, align, font, paint);
    }

    private static SKTypeface PickTypeface(string text)
    {
        foreach (var face in new[] { Symbols, Symbols2 })
        {
            if (face is null) continue;
            using var probe = new SKFont(face);
            if (probe.ContainsGlyphs(text)) return face;
        }
        return text.Length > 0 && char.IsSymbol(text[0])
            ? SKFontManager.Default.MatchCharacter(text[0]) ?? SKTypeface.Default
            : SKTypeface.Default;
    }

    private void Line(SKPoint a, SKPoint b, SKColor color, float width, bool dashed = false)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = width * _px, IsAntialias = true, Style = SKPaintStyle.Stroke };
        if (dashed) paint.PathEffect = SKPathEffect.CreateDash([2 * width * _px, 2 * width * _px], 0);
        _c.DrawLine(a, b, paint);
    }

    private void Circle(float radius, SKColor color, float width) => Circle(new SKPoint(_cx, _cy), radius, color, width);

    private void Circle(SKPoint at, float radius, SKColor color, float width)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = width * _px, IsAntialias = true, Style = SKPaintStyle.Stroke };
        _c.DrawCircle(at, radius, paint);
    }

    private void Dot(SKPoint at, float radius, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        _c.DrawCircle(at, radius * _px, paint);
    }

    private static SKColor WithAlpha(SKColor c, int alpha) => c.WithAlpha((byte)Math.Clamp(alpha, 0, 255));

    // ── Natal wheel ───────────────────────────────────────────────────────────
    public static void Draw(SKCanvas canvas, NatalChart chart, float width, float height, float px,
        Planet? selected = null, bool showVerdict = true)
    {
        var w = new WheelRenderer(canvas, width, height, px);
        double asc = WheelAsc(chart);
        w.DrawZodiacRing(asc);
        if (chart.Timed) w.DrawHouses(chart);
        w.DrawAspects(w._r * AspectInner, chart, selected);
        w.DrawPlanets(chart.Planets, asc, PlanetRing, HouseOuter * 0.88f, 0.072f);
        if (selected is { } s && chart.GetPlanet(s) is { } sp)
            w.Circle(w.ToPoint(w._r * (HouseOuter * 0.88f), sp.Longitude, asc), w._r * 0.022f, AngularColor, 2f);
        if (chart.Timed) w.DrawAngles(chart);
        if (showVerdict) w.DrawVerdictRim(chart);
    }

    private static double WheelAsc(NatalChart chart) => chart.Timed ? chart.Ascendant : 0;

    // ── Bi-wheel (synastry) ──────────────────────────────────────────────────
    public static void DrawBiWheel(SKCanvas canvas, Synastry synastry, float width, float height, float px)
    {
        var w = new WheelRenderer(canvas, width, height, px);
        NatalChart inner = synastry.First, outer = synastry.Second;
        double asc = WheelAsc(inner);

        w.DrawZodiacRing(asc, BiSignInner, centred: true);
        w.Circle(w._r * BiDivider, RingOuter, 1.5f);
        if (inner.Timed) w.DrawHouses(inner, BiDivider, BiAspect + 0.045f);
        w.DrawSynastryAspects(w._r * BiAspect, synastry);
        w.DrawPlanets(inner.Planets, asc, BiInnerGlyph, BiDivider - 0.02f, 0.062f, centred: true);
        w.DrawPlanets(outer.Planets, asc, BiOuterGlyph, BiSignInner - 0.015f, 0.062f, centred: true);
        if (inner.Timed) w.DrawAngles(inner);
        if (outer.Timed)
        {
            w.DrawOuterAngle(outer.Ascendant, asc, "ASC");
            w.DrawOuterAngle(outer.Midheaven, asc, "MC");
        }
        w.DrawKey($"Inner wheel: {inner.Celebrity.Name}", $"Outer wheel: {outer.Celebrity.Name}");
    }

    // ── Tri-wheel (three people) ─────────────────────────────────────────────
    public static void DrawTrioWheel(SKCanvas canvas, Trio trio, float width, float height, float px)
    {
        var w = new WheelRenderer(canvas, width, height, px);
        NatalChart first = trio.Charts[0], second = trio.Charts[1], third = trio.Charts[2];
        double asc = WheelAsc(first);

        w.DrawZodiacRing(asc, BiSignInner, centred: true);
        w.Circle(w._r * TriSecondEdge, RingOuter, 1.5f);
        w.Circle(w._r * TriFirstEdge, RingOuter, 1.5f);
        w.Circle(w._r * TriInnerEdge, RingOuter, 1.5f);

        // Weakest first, so that the strongest is drawn over the others.
        foreach (var (grand, strongest) in trio.GrandTrines.Take(TriTriangleCount)
                     .Select((g, i) => (g, i == 0)).Reverse())
        {
            var color = strongest && trio.Tone == TrioTone.Golden ? GoldenColor : AspectColors[(int)AspectType.Trine];
            var corners = grand.Corners.Select(c => w.ToPoint(w._r * TriTriangles, c.Longitude, asc)).ToList();
            for (int i = 0; i < corners.Count; i++)
                w.Line(corners[i], corners[(i + 1) % corners.Count], color, strongest ? 2.5f : 1.25f);
        }

        w.DrawPlanets(first.Planets, asc, TriFirst, TriFirstEdge - 0.015f, 0.05f, centred: true);
        w.DrawPlanets(second.Planets, asc, TriSecond, TriSecondEdge - 0.015f, 0.05f, centred: true);
        w.DrawPlanets(third.Planets, asc, TriThird, BiSignInner - 0.015f, 0.05f, centred: true);
        w.DrawKey($"Rings from the inside: {first.Celebrity.Name}, {second.Celebrity.Name}, {third.Celebrity.Name}",
            "Triangles: the strongest grand trines");
    }

    // ── Bi-wheel (transits) ──────────────────────────────────────────────────
    public static void DrawTransitWheel(SKCanvas canvas, NatalChart natal, DailyReading reading, float width, float height, float px)
    {
        var w = new WheelRenderer(canvas, width, height, px);
        double asc = WheelAsc(natal);

        w.DrawZodiacRing(asc, BiSignInner, centred: true);
        w.Circle(w._r * BiDivider, RingOuter, 1.5f);
        if (natal.Timed) w.DrawHouses(natal, BiDivider, BiAspect + 0.045f);
        w.DrawTransitLines(w._r * BiAspect, natal, reading);
        w.DrawPlanets(natal.Planets, asc, BiInnerGlyph, BiDivider - 0.02f, 0.062f, centred: true);
        w.DrawPlanets(reading.Midday, asc, BiOuterGlyph, BiSignInner - 0.015f, 0.062f, centred: true);
        if (natal.Timed) w.DrawAngles(natal);
        w.DrawKey($"Inner wheel: {natal.Celebrity.Name}", $"Outer wheel: the sky on {reading.Date:d MMMM yyyy}");
    }

    // ── Sky wheel ──────────────────────────────────────────────────────────────
    public static void DrawSkyWheel(SKCanvas canvas, SkyReading reading, float width, float height, float px)
    {
        var w = new WheelRenderer(canvas, width, height, px);
        w.DrawZodiacRing(0);
        w.Circle(w._r * AspectInner, RingOuter, 1f);

        foreach (var a in reading.Aspects)
        {
            if (reading.Midday.FirstOrDefault(p => p.Planet == a.First) is not { } p1 ||
                reading.Midday.FirstOrDefault(p => p.Planet == a.Second) is not { } p2) continue;
            var color = AspectColors[(int)a.Aspect];
            double closeness = 1 - Math.Min(1, a.MinOrb / Lore.Services.TransitService.SkyOrb(a.First));
            int alpha = a.Shown ? 150 + (int)(closeness * 90) : 50 + (int)(closeness * 70);
            w.Line(w.ToPoint(w._r * AspectInner, p1.Longitude, 0), w.ToPoint(w._r * AspectInner, p2.Longitude, 0),
                WithAlpha(color, alpha), a.Shown ? 2f : 1f);
        }

        var planets = reading.Midday.Where(p => Lore.Services.TransitService.SkyPlanets.Contains(p.Planet)).ToList();
        w.DrawPlanets(planets, 0, PlanetRing, HouseOuter * 0.88f, 0.072f);
    }

    private void DrawTransitLines(float ringR, NatalChart natal, DailyReading reading)
    {
        double asc = WheelAsc(natal);
        foreach (var e in reading.Events)
        {
            if (reading.Midday.FirstOrDefault(p => p.Planet == e.Mover) is not { } mover ||
                natal.LongitudeOf(e.Target) is not { } target) continue;

            double closeness = 1 - Math.Min(1, e.MinOrb / Lore.Services.TransitService.Orb);
            int alpha = e.Shown ? 150 + (int)(closeness * 90) : 60 + (int)(closeness * 70);
            Line(ToPoint(ringR, mover.Longitude, asc), ToPoint(ringR, target, asc),
                WithAlpha(AspectColors[(int)e.Aspect], alpha), e.Shown ? 2f : 1f);
        }
    }

    private void DrawSynastryAspects(float ringR, Synastry synastry)
    {
        double asc = WheelAsc(synastry.First);
        foreach (var aspect in synastry.Aspects)
        {
            if (synastry.First.LongitudeOf(aspect.First) is not { } lonA ||
                synastry.Second.LongitudeOf(aspect.Second) is not { } lonB) continue;

            double closeness = 1 - aspect.Orb / Lore.Services.SynastryService.Orb(aspect.Type);
            int alpha = aspect.Shown ? 150 + (int)(closeness * 90) : 45 + (int)(closeness * 70);
            Line(ToPoint(ringR, lonA, asc), ToPoint(ringR, lonB, asc),
                WithAlpha(AspectColors[(int)aspect.Type], alpha), aspect.Shown ? 1.75f : 0.9f);
        }
    }

    private void DrawOuterAngle(double longitude, double asc, string label)
    {
        Line(ToPoint(_r * BiDivider, longitude, asc), ToPoint(_r * BiSignInner, longitude, asc), OuterAngleColor, 2f);
        var at = ToPoint(_r * (BiSignInner - 0.032f), longitude + 3.5, asc);
        Text(label, at.X, at.Y, OuterAngleColor, _r * 0.03f, centreV: true);
    }

    // Which wheel is whose, in the bottom-left corner.
    private void DrawKey(string inner, string outer)
    {
        float size = _r * 0.042f;
        float x = size * 0.6f;
        Text(inner, x, _height - size * 3.2f, HouseColor, size, centreV: false, SKTextAlign.Left);
        Text(outer, x, _height - size * 1.8f, OuterAngleColor, size, centreV: false, SKTextAlign.Left);
    }

    private void DrawVerdictRim(NatalChart chart)
    {
        var verdict = Lore.Services.DignityService.ComputeIfTimed(chart)?.Verdict;
        SKColor? rim = verdict switch
        {
            ChartVerdict.Extraordinary => new SKColor(63, 184, 79),
            ChartVerdict.Alarming      => new SKColor(229, 83, 75),
            _                          => null,
        };
        if (rim is { } c) Circle(_r * (SignOuter + 0.03f), c, 4f);
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────
    // Ascendant at 9 o'clock, the zodiac running counter-clockwise (see ChartRenderer).
    private SKPoint ToPoint(float r, double longitude, double asc)
    {
        double relative = ((longitude - asc) % 360 + 360) % 360;
        double rad = (180.0 - relative) * Math.PI / 180.0;
        return new SKPoint(_cx + r * (float)Math.Cos(rad), _cy + r * (float)Math.Sin(rad));
    }

    // ── Zodiac ring ──────────────────────────────────────────────────────────
    private void DrawZodiacRing(double asc, float signInner = SignInner, bool centred = false)
    {
        for (int i = 0; i < 12; i++)
        {
            var sign = (ZodiacSign)i;
            double startLon = i * 30.0;
            Line(ToPoint(_r * signInner, startLon, asc), ToPoint(_r * SignOuter, startLon, asc), LineColor, 1.5f);
            var mid = ToPoint(_r * (signInner + (SignOuter - signInner) / 2f), startLon + 15.0, asc);
            Text(sign.Symbol(), mid.X, mid.Y, ElementColors[(int)sign.GetElement()], _r * 0.07f, centreV: centred);
        }
        Circle(_r * SignOuter, RingOuter, 2.5f);
        Circle(_r * signInner, RingOuter, 1.5f);
    }

    // ── Houses ───────────────────────────────────────────────────────────────
    private void DrawHouses(NatalChart chart, float houseOuter = HouseOuter, float labelRing = AspectInner + 0.06f)
    {
        for (int i = 0; i < 12; i++)
        {
            var cusp = chart.Houses[i];
            var nextCusp = chart.Houses[(i + 1) % 12];
            Line(new SKPoint(_cx, _cy), ToPoint(_r * houseOuter, cusp.Longitude, chart.Ascendant), HouseColor, 1.25f);
            var label = ToPoint(_r * labelRing, MidArc(cusp.Longitude, nextCusp.Longitude), chart.Ascendant);
            Text((i + 1).ToString(), label.X, label.Y, HouseColor, _r * 0.045f, centreV: false);
        }
    }

    private static double MidArc(double start, double end)
    {
        double s = ((start % 360) + 360) % 360;
        double e = ((end % 360) + 360) % 360;
        if (e < s) e += 360;
        return ((s + e) / 2.0) % 360;
    }

    // ── Angles (ASC / MC / DSC / IC) ─────────────────────────────────────────
    private void DrawAngles(NatalChart chart)
    {
        DrawAngleLine(chart.Ascendant, chart.Ascendant, "ASC");
        DrawAngleLine(chart.Midheaven, chart.Ascendant, "MC");
        DrawAngleLine((chart.Ascendant + 180) % 360, chart.Ascendant, "DSC");
        DrawAngleLine((chart.Midheaven + 180) % 360, chart.Ascendant, "IC");
    }

    private void DrawAngleLine(double longitude, double asc, string label)
    {
        Line(new SKPoint(_cx, _cy), ToPoint(_r * SignOuter, longitude, asc), AngularColor, 2.5f);
        var at = ToPoint(_r * (SignOuter + 0.03f), longitude, asc);
        Text(label, at.X, at.Y, AngularColor, _r * 0.05f, centreV: false);
    }

    // ── Aspects ──────────────────────────────────────────────────────────────
    private void DrawAspects(float innerR, NatalChart chart, Planet? selected)
    {
        int Shade(int alpha, bool involved) => selected is null ? alpha : involved ? 230 : alpha / 5;
        double asc = WheelAsc(chart);

        foreach (var aspect in chart.Aspects)
        {
            var pA = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetA);
            var pB = chart.Planets.FirstOrDefault(p => p.Planet == aspect.PlanetB);
            if (pA is null || pB is null) continue;

            double allowed = aspect.Allowed > 0 ? aspect.Allowed : aspect.Type.Orb();
            int alpha = 160 - (int)(Math.Min(1, aspect.Orb / allowed) * 100);
            bool involved = aspect.PlanetA == selected || aspect.PlanetB == selected;
            var color = WithAlpha(AspectColors[(int)aspect.Type], Shade(alpha, involved));
            float width = selected is not null && involved ? 2.25f : 1.25f;
            bool major = aspect.Type.IsMajor();
            Line(ToPoint(innerR, pA.Longitude, asc), ToPoint(innerR, pB.Longitude, asc), color, major ? width : width - 0.25f, dashed: !major);
        }

        foreach (var aspect in chart.AngleAspects)
        {
            if (chart.GetPlanet(aspect.Planet) is not { } p || chart.LongitudeOf(aspect.Angle) is not { } angleLon) continue;
            int alpha = 120 - (int)(Math.Min(1, aspect.Orb / aspect.Allowed) * 75);
            bool involved = aspect.Planet == selected;
            var color = WithAlpha(AspectColors[(int)aspect.Type], Shade(alpha, involved));
            float width = selected is not null && involved ? 2f : 1f;
            Line(ToPoint(innerR, p.Longitude, asc), ToPoint(innerR, angleLon, asc), color, width, dashed: !aspect.Type.IsMajor());
        }
    }

    // ── Planets ──────────────────────────────────────────────────────────────
    private void DrawPlanets(IReadOnlyList<PlanetPosition> planets, double asc, float glyphRing, float dotRing,
        float glyphSize, bool centred = false)
    {
        var usedAngles = new List<double>();
        foreach (var planet in planets)
        {
            double displayLon = ResolveOverlap(planet.Longitude, asc, usedAngles);
            usedAngles.Add(displayLon);

            var color = ElementColors[(int)planet.Sign.GetElement()];
            var pt = ToPoint(_r * glyphRing, displayLon, asc);
            var exact = ToPoint(_r * dotRing, planet.Longitude, asc);
            Dot(exact, 3f, color);
            Line(exact, pt, color.WithAlpha(90), 0.75f);
            Text(planet.Planet.Symbol(), pt.X, pt.Y, color, _r * glyphSize, centreV: centred);

            if (planet.MotionMark.Length > 0)
                Text(planet.MotionMark, pt.X + _r * 0.04f, pt.Y + _r * (centred ? 0.008f : 0.05f), MotionColor, _r * 0.038f,
                    centreV: false, SKTextAlign.Left);
        }
    }

    private static double ResolveOverlap(double longitude, double asc, List<double> used)
    {
        const double minSep = 7.0;
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
