using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using Lore.Models;
using Lore.Views;
using Microsoft.Graphics.Canvas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Windows.Storage.Streams;

namespace Lore.Services;

// Produces exportable artifacts from a computed NatalChart: a rendered PNG of the
// chart wheel, structured JSON/XML data, and a formatted PDF "reading" (birthdata
// + wheel image + the written report). Returns bytes; the caller writes the file.
public static class ExportService
{
    static ExportService()
    {
        // QuestPDF Community licence (free for individuals / <$1M revenue).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── PNG (Win2D offscreen render, no on-screen control needed) ─────────────
    // `showVerdict`: the coloured rim for the dignity verdict, which belongs to a birth
    // chart and is left off a solar return's wheel, as it is on screen.
    public static Task<byte[]> RenderChartPngAsync(NatalChart chart, int size = 1600, bool showVerdict = true) =>
        RenderPngAsync(size, ds => ChartRenderer.Draw(ds, chart, size, size, showVerdict: showVerdict));

    // The synastry bi-wheel: the first chart inside, the second around it.
    public static Task<byte[]> RenderBiWheelPngAsync(Synastry synastry, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.DrawBiWheel(ds, synastry, size, size));

    // The Daily view's wheel: the birth chart inside, the day's sky around it.
    public static Task<byte[]> RenderTransitWheelPngAsync(NatalChart natal, DailyReading reading, int size = 1600) =>
        RenderPngAsync(size, ds => ChartRenderer.DrawTransitWheel(ds, natal, reading, size, size));

    // The wheel with the Worksheet's tables around and under it. The page is drawn on a
    // surface taller than it can need, then cut to the height it used.
    public static async Task<byte[]> RenderChartSheetPngAsync(NatalChart chart)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var scratch = new CanvasRenderTarget(device, ChartSheetRenderer.Width, ChartSheetRenderer.MaxHeight, 96);
        float used;
        using (var ds = scratch.CreateDrawingSession())
        {
            used = ChartSheetRenderer.Draw(ds, chart);
        }

        int height = (int)Math.Ceiling(used);
        using var target = new CanvasRenderTarget(device, ChartSheetRenderer.Width, height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.DrawImage(scratch, 0, 0, new Windows.Foundation.Rect(0, 0, ChartSheetRenderer.Width, height));
        }
        return await SavePngAsync(target);
    }

    private static async Task<byte[]> RenderPngAsync(int size, Action<CanvasDrawingSession> draw)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96);
        using (var ds = target.CreateDrawingSession())
        {
            draw(ds);
        }
        return await SavePngAsync(target);
    }

    private static async Task<byte[]> SavePngAsync(CanvasRenderTarget target)
    {
        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    // ── JSON / XML (structured chart data) ────────────────────────────────────
    public static byte[] ToJson(NatalChart chart)
    {
        var dto = BuildDto(chart);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, opts));
    }

    public static byte[] ToXml(NatalChart chart)
    {
        var dto = BuildDto(chart);
        var serializer = new XmlSerializer(typeof(ChartExport));
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(false)))
        {
            serializer.Serialize(writer, dto);
        }
        return ms.ToArray();
    }

    // ── PDF (full reading) ────────────────────────────────────────────────────
    // `worksheet` and `sensitivity`, when given, add the numbers behind the chart after
    // the reading: how it was calculated, the positions, cusps and aspects, and what
    // would change if the birth time were off.
    public static byte[] ToPdf(NatalChart chart, IReadOnlyList<ReportSection> report, byte[] chartPng,
        Worksheet? worksheet = null, TimeSensitivity? sensitivity = null)
    {
        var vm = chart.Celebrity;
        string subtitle = $"{vm.BirthDateLabel}  ·  {vm.BirthPlace}" +
            (vm.BirthTimeKnown ? $"  ·  {vm.BirthTime}" : "  ·  time unknown") +
            (string.IsNullOrWhiteSpace(vm.RoddenRating) ? "" : $"  ·  Rodden {vm.RoddenRating}");
        // Lead with the "Big Three" (Sun first — that's the everyday "sign"); keep the
        // Ascendant/Midheaven as a small, clearly-labelled technical line so the MC can't
        // be mistaken for someone's sign.
        string rising = ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name();
        var bigParts = new List<string>();
        if (chart.GetPlanet(Planet.Sun) is { } sunP)  bigParts.Add($"☉ Sun {sunP.Sign.Name()}");
        // With no birth time the Moon may have changed sign during the day: name both.
        if (!chart.Timed && sensitivity is { MoonSigns.Count: > 1 })
            bigParts.Add($"☽ Moon {string.Join(" or ", sensitivity.MoonSigns.Select(s => s.Name()))}");
        else if (chart.GetPlanet(Planet.Moon) is { } moonP) bigParts.Add($"☽ Moon {moonP.Sign.Name()}");
        if (chart.Timed) bigParts.Add($"↑ Rising {rising}");
        string bigThree = string.Join("     ·     ", bigParts);
        string angles = chart.AnglesLine;

        // No dignity score without a birth time (see DignityService.ComputeIfTimed).
        var score = DignityService.ComputeIfTimed(chart);
        string verdictHex = score?.Verdict.ColorHex() ?? ChartVerdict.Ordinary.ColorHex();
        string verdictLine = score is null
            ? "Chart assessment:  not scored — it needs a birth time"
            : $"Chart assessment:  {score.Total:+#;-#;0}  —  {score.Verdict.Label()}";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                // Segoe UI covers the text; the trailing families are a fallback chain
                // supplying the astrological glyphs (☉ ♈ △ …) the primary font lacks.
                page.DefaultTextStyle(x => x.FontSize(11)
                    .FontFamily("Segoe UI", "Segoe UI Symbol", "Segoe UI Historic"));

                page.Header().Column(col =>
                {
                    col.Item().Text(vm.Name).FontSize(22).SemiBold();
                    col.Item().Text(subtitle).FontSize(11).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(8);

                    if (chartPng is { Length: > 0 })
                        col.Item().AlignCenter().Width(340).Image(chartPng);

                    col.Item().AlignCenter().Text(bigThree).FontSize(13).SemiBold();
                    col.Item().AlignCenter().Text(angles)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().AlignCenter().Text(verdictLine)
                        .FontSize(12).SemiBold().FontColor(verdictHex);

                    foreach (var section in report)
                    {
                        col.Item().PaddingTop(8).Text(section.Heading).FontSize(15).SemiBold();
                        foreach (var para in section.Paragraphs)
                            col.Item().Text(para).FontSize(11).LineHeight(1.35f);
                    }

                    if (score is not null)
                        ChartAssessment(col, score);

                    if (worksheet is not null)
                        WorksheetPdf.Compose(col, worksheet, sensitivity);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated by Lore · ").FontSize(9).FontColor(Colors.Grey.Medium);
                    x.Span(DateTime.Now.ToString("yyyy-MM-dd")).FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    // Traditional dignity breakdown: the verdict, a plain-language note, and a per-planet
    // table showing where each planet's points came from.
    private static void ChartAssessment(QuestPDF.Fluent.ColumnDescriptor col, ChartScore score)
    {
        col.Item().PaddingTop(10).Text("Chart Assessment").FontSize(15).SemiBold();
        col.Item().Text(
            "A traditional dignity score for the seven classical planets (Sun–Saturn): essential " +
            "dignity by sign and degree, plus accidental dignity by house, motion, and closeness to " +
            "the Sun. Higher means more traditionally dignified. Most charts fall between −1 and +28; " +
            "29 or more is flagged Extraordinary and −2 or less Alarming.")
            .FontSize(9).FontColor(Colors.Grey.Darken1).LineHeight(1.3f);

        col.Item().PaddingTop(2).Text(t =>
        {
            t.Span($"Total {score.Total:+#;-#;0} — {score.Verdict.Label()}")
                .SemiBold().FontColor(score.Verdict.ColorHex());
            t.Span($"   ({(score.IsDayChart ? "day chart" : "night chart")})")
                .FontSize(9).FontColor(Colors.Grey.Medium);
        });

        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);    // planet
                c.RelativeColumn(1.5f); // essential
                c.RelativeColumn(1.6f); // accidental
                c.RelativeColumn(1.2f); // total
                c.RelativeColumn(6);    // notes
            });

            static IContainer Head(IContainer c) =>
                c.PaddingVertical(2).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);

            table.Header(h =>
            {
                h.Cell().Element(Head).Text("Planet").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Essential").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Accidental").SemiBold().FontSize(9);
                h.Cell().Element(Head).AlignRight().Text("Total").SemiBold().FontSize(9);
                h.Cell().Element(Head).Text("Why").SemiBold().FontSize(9);
            });

            foreach (var pd in score.Planets)
            {
                table.Cell().PaddingVertical(1).Text($"{pd.Planet.Symbol()} {pd.Planet.Name()}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Essential:+#;-#;0}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Accidental:+#;-#;0}").FontSize(9);
                table.Cell().PaddingVertical(1).AlignRight().Text($"{pd.Total:+#;-#;0}").SemiBold().FontSize(9);
                table.Cell().PaddingVertical(1).Text(pd.Notes).FontSize(8).FontColor(Colors.Grey.Darken1);
            }
        });
    }

    // ── DTO ───────────────────────────────────────────────────────────────────
    private static ChartExport BuildDto(NatalChart chart)
    {
        var c = chart.Celebrity;
        var m = NatalMetricsService.Compute(chart);
        var score = DignityService.ComputeIfTimed(chart);
        double? Point(string name) => m.LongitudeOf(name) is { } lon ? Round(lon) : null;
        static List<TallyExport> Tallies(IReadOnlyList<Tally> tallies) =>
            tallies.Select(t => new TallyExport { Name = t.Name, Count = t.Count }).ToList();

        static List<WeightExport> Weights(IReadOnlyList<Weight> weights) =>
            weights.Select(w => new WeightExport { Name = w.Name, Points = w.Points }).ToList();

        return new ChartExport
        {
            GeneratedBy = "Lore",
            GeneratedAt = DateTime.Now.ToString("s"),
            Name = c.Name,
            Category = c.Category,
            BirthDate = c.BirthDate,
            Calendar = c.JulianCalendar ? "Julian (Old Style)" : "Gregorian",
            GregorianBirthDate = BirthTimeResolver.GregorianDate(c).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            BirthTime = c.BirthTime,
            BirthTimeKnown = c.BirthTimeKnown,
            BirthTimeUncertaintyMinutes = c.BirthTimeUncertaintyMinutes,
            RoddenRating = c.RoddenRating ?? "",
            Source = c.Source ?? "",
            BirthPlace = c.BirthPlace,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            UtcOffsetHours = c.UtcOffsetHours,
            UniversalTime = chart.CalculatedForUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            JulianDay = Math.Round(SwissEphemeris.DateTimeToJulianDay(chart.CalculatedForUtc), 6),
            TimeConversion = BirthTimeResolver.Explain(c).offset,
            TimeZoneId = c.UtcOffsetFixed ? "" : BirthTimeResolver.ResolveZoneId(c.TimeZoneId, c.Latitude, c.Longitude) ?? "",
            UtcOffsetSetByHand = c.UtcOffsetFixed,
            EphemerisNote = chart.EphemerisNote,
            HouseSystem = chart.HouseSystemLabel,
            NodeType = chart.Settings.Node.Name(),
            LilithType = chart.Settings.Lilith.Name(),
            AspectOrbs = chart.Settings.Orbs.Describe(),
            // The angles and houses are left out, not guessed, when there is no birth time.
            Ascendant = chart.Timed ? Round(chart.Ascendant) : null,
            AscendantSign = chart.Timed ? ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name() : "",
            Midheaven = chart.Timed ? Round(chart.Midheaven) : null,
            MidheavenSign = chart.Timed ? ZodiacSignExtensions.FromLongitude(chart.Midheaven).Name() : "",
            Descendant = Point("Descendant"),
            ImumCoeli = Point("Imum Coeli"),
            Vertex = Point("Vertex"),
            AntiVertex = Point("Anti-Vertex"),
            PartOfFortune = Point("Part of Fortune"),
            PartOfSpirit = Point("Part of Spirit"),
            SouthNode = Point("South Node"),
            Sect = chart.Timed ? (chart.IsDayChart ? "Day" : "Night") : "",
            SiderealTimeDegrees = chart.Timed ? Round(chart.Armc) : null,
            Obliquity = chart.Obliquity is { } eps ? Math.Round(eps, 6) : null,
            MoonPhase = m.Moon is { } moon ? new MoonPhaseExport
            {
                Phase = moon.Phase.Name(),
                MoonAheadOfSun = Round(moon.Elongation),
                Waxing = moon.Waxing,
                IlluminatedFraction = Round(moon.Illumination),
                AgeDays = chart.Events?.NewMoonBefore is { } since ? Round(since.DaysBefore) : null,
            } : null,
            Lunations = new[] { chart.Events?.NewMoonBefore, chart.Events?.FullMoonBefore }.OfType<Lunation>()
                .OrderByDescending(l => l.Utc).Select(l => new LunationExport
                {
                    Type = l.Full ? "Full Moon" : "New Moon",
                    UniversalTime = l.Utc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    Longitude = Round(l.Longitude),
                    Sign = ZodiacSignExtensions.FromLongitude(l.Longitude).Name(),
                    DaysBeforeBirth = Round(l.DaysBefore),
                    Eclipse = l.Eclipse ?? "",
                }).ToList(),
            PlanetaryHour = chart.Events?.Hour is { } hour ? new PlanetaryHourExport
            {
                DayRuler = hour.DayRuler.Name(),
                HourRuler = hour.HourRuler.Name(),
                Hour = hour.Hour,
                ByDay = hour.ByDay,
                Sunrise = hour.SunriseUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                Sunset = hour.SunsetUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            } : null,
            AlternatePoints = chart.Alternates.Select(a => new AlternatePointExport
            {
                Name = a.Name,
                Sign = a.Position.Sign.Name(),
                DegreeInSign = Round(a.Position.DegreeInSign),
                Longitude = Round(a.Position.Longitude),
                SpeedPerDay = Round(a.Position.SpeedLongitude),
            }).ToList(),
            Planets = chart.Planets.Select(p => new PlanetExport
            {
                Name = p.PlanetName,
                Sign = p.Sign.Name(),
                DegreeInSign = Round(p.DegreeInSign),
                Longitude = Round(p.Longitude),
                Latitude = Round(p.Latitude),
                Declination = p.HasEquatorial ? Round(p.Declination) : null,
                RightAscension = p.HasEquatorial ? Round(p.RightAscension) : null,
                SpeedPerDay = Round(p.SpeedLongitude),
                LatitudeSpeedPerDay = Round(p.SpeedLatitude),
                DeclinationSpeedPerDay = p.HasEquatorial ? Round(p.SpeedDeclination) : null,
                // Only a physical body has a distance worth the name.
                DistanceAu = p.Planet is Planet.NorthNode or Planet.Lilith ? null : Math.Round(p.Distance, 8),
                OutOfBoundsBy = m.OutOfBounds.FirstOrDefault(o => o.Planet == p.Planet) is { } oob ? Round(oob.Excess) : null,
                DegreesFromSun = m.Solar.FirstOrDefault(r => r.Planet == p.Planet) is { } solar ? Round(solar.Elongation) : null,
                SideOfSun = m.Solar.FirstOrDefault(r => r.Planet == p.Planet) is { } side ? (side.EastOfSun ? "East" : "West") : "",
                SolarCondition = m.Solar.FirstOrDefault(r => r.Planet == p.Planet) is { Condition: not SolarCondition.None } cond
                    ? cond.Condition.ToString() : "",
                NearestAngle = m.Angularity.FirstOrDefault(a => a.Planet == p.Planet)?.NearestAngle ?? "",
                DegreesFromNearestAngle = m.Angularity.FirstOrDefault(a => a.Planet == p.Planet) is { } ang ? Round(ang.Distance) : null,
                SignRuler = m.Rulers.Dispositions.First(d => d.Planet == p.Planet).SignRuler.Name(),
                TermRuler = m.Rulers.Dispositions.First(d => d.Planet == p.Planet).TermRuler.Name(),
                FaceRuler = m.Rulers.Dispositions.First(d => d.Planet == p.Planet).FaceRuler.Name(),
                TriplicityRulers = string.Join(", ", m.Rulers.Dispositions.First(d => d.Planet == p.Planet).Triplicity.Select(x => x.Name())),
                House = chart.Timed ? chart.GetHouseForLongitude(p.Longitude) : null,
                Retrograde = p.IsRetrograde,
                Stationary = p.IsStationary,
                SpeedAgainstAverage = m.Motion.FirstOrDefault(x => x.Planet == p.Planet)?.Ratio is { } ratio ? Round(ratio) : null,
                Altitude = m.Sky.FirstOrDefault(x => x.Planet == p.Planet)?.Altitude is { } altitude ? Round(altitude) : null,
                Azimuth = m.Sky.FirstOrDefault(x => x.Planet == p.Planet)?.Azimuth is { } azimuth ? Round(azimuth) : null,
                Antiscion = Round(m.Antiscia.First(x => x.Point == NatalPoint.Of(p.Planet)).Longitude),
                ContraAntiscion = Round(m.Antiscia.First(x => x.Point == NatalPoint.Of(p.Planet)).Contra),
                StationBefore = StationOf(chart.Events?.Stations.FirstOrDefault(s => s.Planet == p.Planet)?.Before),
                StationAfter = StationOf(chart.Events?.Stations.FirstOrDefault(s => s.Planet == p.Planet)?.After),
            }).ToList(),
            Houses = (chart.Timed ? chart.Houses : []).Select(h => new HouseExport
            {
                House = h.House,
                Longitude = Round(h.Longitude),
                Sign = h.Sign.Name(),
                DegreeInSign = Round(h.DegreeInSign),
                Ruler = DignityService.RulerOf(h.Sign).Name()
            }).ToList(),
            Aspects = chart.Aspects.Select(a => new AspectExport
            {
                PlanetA = a.PlanetA.Name(),
                PlanetB = a.PlanetB.Name(),
                Type = a.Type.Name(),
                ExactAngle = a.Type.Angle(),
                Orb = Round(a.Orb),
                AllowedOrb = a.Allowed,
                Applying = a.IsApplying,
                OutOfSign = a.OutOfSign
            }).Concat(chart.AngleAspects.Concat(WorksheetService.PointAspects(chart)).Select(a => new AspectExport
            {
                PlanetA = a.Planet.Name(),
                PlanetB = a.Angle.Name,
                Type = a.Type.Name(),
                ExactAngle = a.Type.Angle(),
                Orb = Round(a.Orb),
                AllowedOrb = a.Allowed,
                Applying = a.IsApplying,
                OutOfSign = a.OutOfSign
            })).ToList(),
            MidpointContacts = m.MidpointContacts.Select(c => new MidpointContactExport
            {
                Point = c.Point.Name, MidpointOfA = c.A.Name, MidpointOfB = c.B.Name,
                Orb = Round(c.Orb), AllowedOrb = NatalMetricsService.MidpointOrb,
            }).ToList(),
            Midpoints = m.Midpoints.Select(x => new MidpointExport
            {
                PointA = x.A.Name, PointB = x.B.Name, Longitude = Round(x.Longitude),
                Sign = ZodiacSignExtensions.FromLongitude(x.Longitude).Name(),
            }).ToList(),
            AntisciaContacts = m.AntisciaContacts.Select(c => new AntisciaContactExport
            {
                PointA = c.A.Name, PointB = c.B.Name, Type = c.Contra ? "Contra-antiscion" : "Antiscion",
                Orb = Round(c.Orb), AllowedOrb = NatalMetricsService.AntisciaOrb,
            }).ToList(),
            Almutens = m.Almutens.Select(a => new AlmutenExport
            {
                Place = a.Point, Longitude = Round(a.Longitude),
                Almuten = string.Join(" and ", a.Rulers.Select(x => x.Name())), Points = a.Points,
            }).ToList(),
            Patterns = AspectPatternService.Detect(chart).Select(p => new PatternExport
            {
                Type = p.Type.ToString(),
                Planets = p.Points.Select(x => x.Name).ToList(),
                Sign = p.Sign.HasValue ? p.Sign.Value.Name() : "",
                Element = p.Element.HasValue ? p.Element.Value.ToString() : "",
                Modality = p.Modality.HasValue ? p.Modality.Value.ToString() : "",
                Apex = p.Apex.HasValue ? p.Apex.Value.Name : ""
            }).ToList(),
            DeclinationContacts = m.Parallels.Select(d => new DeclinationContactExport
            {
                PlanetA = d.A.Name(),
                PlanetB = d.B.Name(),
                Type = d.Contra ? "Contra-parallel" : "Parallel",
                Orb = Round(d.Orb),
                AllowedOrb = NatalMetricsService.ParallelOrb,
                Applying = d.Applying
            }).ToList(),
            Balance = new BalanceExport
            {
                BodiesCounted = m.Distribution.Total,
                Elements = Tallies(m.Distribution.Elements),
                Modalities = Tallies(m.Distribution.Modalities),
                Polarities = Tallies(m.Distribution.Polarities),
                HouseTypes = Tallies(m.Distribution.HouseTypes),
                Hemispheres = Tallies(m.Distribution.Hemispheres),
                Quadrants = Tallies(m.Distribution.Quadrants),
                PerHouse = m.Distribution.PerHouse.ToList(),
                PlanetsWithinDegrees = m.Spread is { } spread ? Round(spread.Arc) : null,
                WidestGapDegrees = m.Spread is { } gap ? Round(gap.LargestGap) : null,
                SpreadFrom = m.Spread?.First.Name() ?? "",
                SpreadTo = m.Spread?.Last.Name() ?? "",
            },
            Rulers = new RulersExport
            {
                ChartRuler = m.Rulers.ChartRuler?.Name() ?? "",
                ModernChartRuler = m.Rulers.ModernChartRuler?.Name() ?? "",
                FinalDispositor = m.Rulers.FinalDispositor?.Name() ?? "",
                InOwnSign = m.Rulers.InDomicile.Select(x => x.Name()).ToList(),
                MutualReceptions = m.Rulers.Loops.Where(l => l.Count == 2)
                    .Select(l => $"{l[0].Name()} and {l[1].Name()}").ToList(),
                Unaspected = m.Aspects.Unaspected.Select(x => x.Name()).ToList(),
            },
            Dominants = new DominantsExport
            {
                Planet = m.Dominants.Planet?.Name() ?? "",
                Planets = m.Dominants.Planets.Select(p => new PlanetStrengthExport
                {
                    Name = p.Planet.Name(), Points = Round(p.Score), MadeUpOf = string.Join("; ", p.Parts),
                }).ToList(),
                Signs = Weights(m.Dominants.Signs),
                Elements = Weights(m.Dominants.Elements),
                Modalities = Weights(m.Dominants.Modalities),
            },
            Dignity = score is null ? null : new DignityExport
            {
                Total = score.Total,
                Verdict = score.Verdict.Label(),
                Planets = score.Planets.Select(d => new PlanetDignityExport
                {
                    Name = d.Planet.Name(),
                    Essential = d.Essential,
                    Accidental = d.Accidental,
                    Total = d.Total,
                    Notes = d.Notes
                }).ToList()
            }
        };
    }

    private static double Round(double v) => Math.Round(v, 4);

    private static StationExport? StationOf(StationPoint? s) => s is null ? null : new StationExport
    {
        UniversalTime = s.Utc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        Turns = s.TurnsRetrograde ? "Retrograde" : "Direct",
        DaysFromBirth = Round(s.Days),
    };
}

// XmlSerializer requires public parameterless types with get/set members.
public sealed class ChartExport
{
    public string GeneratedBy { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string Calendar { get; set; } = "";
    public string GregorianBirthDate { get; set; } = "";
    public string? BirthTime { get; set; }
    public bool BirthTimeKnown { get; set; }
    public int BirthTimeUncertaintyMinutes { get; set; }
    public string RoddenRating { get; set; } = "";
    public string Source { get; set; } = "";
    public string BirthPlace { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    // The stored fallback offset. It is NOT necessarily the one used: see TimeConversion
    // and UniversalTime for what the calculation actually did.
    public double UtcOffsetHours { get; set; }
    public string UniversalTime { get; set; } = "";
    public double JulianDay { get; set; }               // of that instant, in Universal Time
    public string TimeConversion { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    public bool UtcOffsetSetByHand { get; set; }
    public string EphemerisNote { get; set; } = "";
    public string HouseSystem { get; set; } = "";
    public string NodeType { get; set; } = "";
    public string LilithType { get; set; } = "";
    public string AspectOrbs { get; set; } = "";
    public double? Ascendant { get; set; }   // null without a birth time
    public string AscendantSign { get; set; } = "";
    public double? Midheaven { get; set; }   // null without a birth time
    public string MidheavenSign { get; set; } = "";
    // Points worked out from the others. All but the South Node need a birth time.
    public double? Descendant { get; set; }
    public double? ImumCoeli { get; set; }
    public double? Vertex { get; set; }
    public double? AntiVertex { get; set; }
    public double? PartOfFortune { get; set; }
    public double? PartOfSpirit { get; set; }
    public double? SouthNode { get; set; }
    public string Sect { get; set; } = "";              // Day / Night; empty without a birth time
    public double? SiderealTimeDegrees { get; set; }    // ARMC at the birthplace
    public double? Obliquity { get; set; }              // true obliquity of the ecliptic, degrees
    public MoonPhaseExport? MoonPhase { get; set; }
    // The New and Full Moon before the birth, the later first. Empty, like PlanetaryHour
    // and the planets' stations, in a file not written from a chart on screen.
    public List<LunationExport> Lunations { get; set; } = [];
    public PlanetaryHourExport? PlanetaryHour { get; set; }   // null without a birth time
    // The North Node and Lilith of the kind the settings did not choose.
    public List<AlternatePointExport> AlternatePoints { get; set; } = [];
    public List<PlanetExport> Planets { get; set; } = [];
    public List<HouseExport> Houses { get; set; } = [];
    // Body to body, then body to Ascendant, Midheaven, Vertex and the two lots.
    public List<AspectExport> Aspects { get; set; } = [];
    public List<MidpointContactExport> MidpointContacts { get; set; } = [];   // closest first
    public List<MidpointExport> Midpoints { get; set; } = [];
    public List<AntisciaContactExport> AntisciaContacts { get; set; } = [];   // closest first
    public List<AlmutenExport> Almutens { get; set; } = [];                   // empty without a birth time
    public List<PatternExport> Patterns { get; set; } = [];
    public List<DeclinationContactExport> DeclinationContacts { get; set; } = [];
    public BalanceExport Balance { get; set; } = new();
    public RulersExport Rulers { get; set; } = new();
    public DominantsExport Dominants { get; set; } = new();
    public DignityExport? Dignity { get; set; }         // null without a birth time
}

public sealed class MoonPhaseExport
{
    public string Phase { get; set; } = "";
    public double MoonAheadOfSun { get; set; }           // degrees of longitude, 0–360
    public bool Waxing { get; set; }
    public double IlluminatedFraction { get; set; }      // 0–1
    public double? AgeDays { get; set; }                 // since the New Moon before birth
}

public sealed class LunationExport
{
    public string Type { get; set; } = "";               // New Moon / Full Moon
    public string UniversalTime { get; set; } = "";
    public double Longitude { get; set; }                // the Moon's
    public string Sign { get; set; } = "";
    public double DaysBeforeBirth { get; set; }
    public string Eclipse { get; set; } = "";            // total / partial / annular…; empty if it was none
}

public sealed class PlanetaryHourExport
{
    public string DayRuler { get; set; } = "";
    public string HourRuler { get; set; } = "";
    public int Hour { get; set; }                        // 1 at sunrise to 24
    public bool ByDay { get; set; }
    public string Sunrise { get; set; } = "";            // the sunrise the day began with, UT
    public string Sunset { get; set; } = "";
}

public sealed class AlternatePointExport
{
    public string Name { get; set; } = "";
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
    public double Longitude { get; set; }
    public double SpeedPerDay { get; set; }
}

public sealed class StationExport
{
    public string UniversalTime { get; set; } = "";
    public string Turns { get; set; } = "";              // Retrograde / Direct
    public double DaysFromBirth { get; set; }
}

public sealed class MidpointExport
{
    public string PointA { get; set; } = "";
    public string PointB { get; set; } = "";
    public double Longitude { get; set; }                // the nearer midpoint; the other is opposite
    public string Sign { get; set; } = "";
}

public sealed class MidpointContactExport
{
    public string Point { get; set; } = "";
    public string MidpointOfA { get; set; } = "";
    public string MidpointOfB { get; set; } = "";
    public double Orb { get; set; }
    public double AllowedOrb { get; set; }
}

public sealed class AntisciaContactExport
{
    public string PointA { get; set; } = "";
    public string PointB { get; set; } = "";
    public string Type { get; set; } = "";               // Antiscion / Contra-antiscion
    public double Orb { get; set; }
    public double AllowedOrb { get; set; }
}

public sealed class AlmutenExport
{
    public string Place { get; set; } = "";
    public double Longitude { get; set; }
    public string Almuten { get; set; } = "";            // more than one name if level
    public int Points { get; set; }
}

public sealed class DeclinationContactExport
{
    public string PlanetA { get; set; } = "";
    public string PlanetB { get; set; } = "";
    public string Type { get; set; } = "";               // Parallel / Contra-parallel
    public double Orb { get; set; }
    public double AllowedOrb { get; set; }
    public bool Applying { get; set; }
}

public sealed class TallyExport
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public sealed class BalanceExport
{
    public int BodiesCounted { get; set; }
    public List<TallyExport> Elements { get; set; } = [];
    public List<TallyExport> Modalities { get; set; } = [];
    public List<TallyExport> Polarities { get; set; } = [];
    public List<TallyExport> HouseTypes { get; set; } = [];    // empty without a birth time
    public List<TallyExport> Hemispheres { get; set; } = [];   // empty without a birth time
    public List<TallyExport> Quadrants { get; set; } = [];     // empty without a birth time
    [XmlArrayItem("Count")]
    public List<int> PerHouse { get; set; } = [];              // twelve counts, house 1 first
    // The smallest arc of the zodiac holding the ten planets, running from one to the other.
    public double? PlanetsWithinDegrees { get; set; }
    public double? WidestGapDegrees { get; set; }
    public string SpreadFrom { get; set; } = "";
    public string SpreadTo { get; set; } = "";
}

// Which planet stands out, by Lore's own weighting, and the chart's balance with the
// Sun, Moon and Ascendant counting for more than the slow planets.
public sealed class DominantsExport
{
    public string Planet { get; set; } = "";
    [XmlArrayItem("Planet")]
    public List<PlanetStrengthExport> Planets { get; set; } = [];   // strongest first
    [XmlArrayItem("Sign")]
    public List<WeightExport> Signs { get; set; } = [];
    [XmlArrayItem("Element")]
    public List<WeightExport> Elements { get; set; } = [];
    [XmlArrayItem("Modality")]
    public List<WeightExport> Modalities { get; set; } = [];
}

public sealed class PlanetStrengthExport
{
    public string Name { get; set; } = "";
    public double Points { get; set; }
    public string MadeUpOf { get; set; } = "";
}

public sealed class WeightExport
{
    public string Name { get; set; } = "";
    public double Points { get; set; }
}

public sealed class RulersExport
{
    public string ChartRuler { get; set; } = "";         // traditional ruler of the rising sign
    public string ModernChartRuler { get; set; } = "";
    public string FinalDispositor { get; set; } = "";
    [XmlArrayItem("Planet")]
    public List<string> InOwnSign { get; set; } = [];
    [XmlArrayItem("Pair")]
    public List<string> MutualReceptions { get; set; } = [];
    [XmlArrayItem("Planet")]
    public List<string> Unaspected { get; set; } = [];
}

public sealed class DignityExport
{
    public int Total { get; set; }
    public string Verdict { get; set; } = "";
    public List<PlanetDignityExport> Planets { get; set; } = [];
}

public sealed class PlanetDignityExport
{
    public string Name { get; set; } = "";
    public int Essential { get; set; }
    public int Accidental { get; set; }
    public int Total { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class PlanetExport
{
    public string Name { get; set; } = "";
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public double? Declination { get; set; }             // null if it could not be calculated
    public double? RightAscension { get; set; }          // degrees
    public double SpeedPerDay { get; set; }
    public double LatitudeSpeedPerDay { get; set; }
    public double? DeclinationSpeedPerDay { get; set; }
    public double? DistanceAu { get; set; }              // null for the node and Lilith
    public double? OutOfBoundsBy { get; set; }           // degrees beyond the obliquity; null when within bounds
    public double? DegreesFromSun { get; set; }          // Moon to Pluto
    public string SideOfSun { get; set; } = "";          // East (evening) / West (morning)
    public string SolarCondition { get; set; } = "";     // Cazimi / Combust / UnderBeams
    public string NearestAngle { get; set; } = "";       // empty without a birth time
    public double? DegreesFromNearestAngle { get; set; }
    public string SignRuler { get; set; } = "";
    public string TermRuler { get; set; } = "";
    public string FaceRuler { get; set; } = "";
    public string TriplicityRulers { get; set; } = "";   // by day, by night, sharing
    public int? House { get; set; }          // null without a birth time
    public bool Retrograde { get; set; }
    public bool Stationary { get; set; }
    public double? SpeedAgainstAverage { get; set; }     // 1 = its average speed; Sun to Saturn
    public double? Altitude { get; set; }                // degrees above the horizon; null without a birth time
    public double? Azimuth { get; set; }                 // degrees from north through east
    public double Antiscion { get; set; }
    public double ContraAntiscion { get; set; }
    public StationExport? StationBefore { get; set; }    // Mercury to Pluto
    public StationExport? StationAfter { get; set; }
}

public sealed class HouseExport
{
    public int House { get; set; }
    public double Longitude { get; set; }
    public string Sign { get; set; } = "";
    public double DegreeInSign { get; set; }
    public string Ruler { get; set; } = "";
}

public sealed class AspectExport
{
    public string PlanetA { get; set; } = "";
    public string PlanetB { get; set; } = "";
    public string Type { get; set; } = "";
    public double ExactAngle { get; set; }
    public double Orb { get; set; }
    public double AllowedOrb { get; set; }
    public bool Applying { get; set; }
    public bool OutOfSign { get; set; }
}

public sealed class PatternExport
{
    public string Type { get; set; } = "";                 // Stellium / GrandTrine / TSquare / GrandCross / Yod / Kite / MysticRectangle
    [XmlArrayItem("Planet")]
    public List<string> Planets { get; set; } = [];
    public string Sign { get; set; } = "";                 // Stellium: the shared sign
    public string Element { get; set; } = "";              // Grand Trine: the shared element
    public string Modality { get; set; } = "";             // T-Square / Grand Cross: the shared modality
    public string Apex { get; set; } = "";                 // T-Square: the body squaring both ends; Yod and Kite: the focal point
}
