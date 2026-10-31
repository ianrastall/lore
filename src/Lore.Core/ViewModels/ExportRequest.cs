using Lore.Models;
using System.Globalization;

namespace Lore.ViewModels;

// Everything one export will write, taken at one moment and not changed afterwards.
//
// It is made before the save dialog is shown. While that dialog is open another chart
// may be selected, a calculation may finish, or answers may be deleted; the file must
// still hold exactly what was on screen when the export was asked for, under the name
// of the person it was asked for. So nothing is read from a view model once this exists:
// the window draws and writes from the request alone. Kinds that are plain text made
// from view-model state (the personality profile and the chart-and-answers reading) are
// carried as their finished bytes.
public sealed record ExportRequest
{
    public required string Kind { get; init; }
    public required string Label { get; init; }           // "PDF document"
    public required string Extension { get; init; }       // ".pdf"
    public required string SuggestedName { get; init; }   // a file name, without the extension

    public required NatalChart Chart { get; init; }
    public IReadOnlyList<ReportSection> Report { get; init; } = [];
    public Worksheet? Worksheet { get; init; }
    public TimeSensitivity? Sensitivity { get; init; }

    public DailyReading? Daily { get; init; }
    public ForecastReading? Forecast { get; init; }
    public TimingReading? Timing { get; init; }
    public Synastry? Comparison { get; init; }
    public SynastryReading? Synastry { get; init; }
    public DavisonReading? Davison { get; init; }

    // The whole file, for the kinds that need no drawing.
    public byte[]? Text { get; init; }

    // The kinds there are, with what the save dialog calls each and its extension.
    public static (string Label, string Extension) Format(string kind) => kind switch
    {
        "pdf" or "dailypdf" or "forecastpdf" or "timingpdf" or "synastrypdf" or "davisonpdf" => ("PDF document", ".pdf"),
        "png" or "sheetpng" or "dailypng" or "synastrypng" or "davisonpng" or "davisonsheetpng" => ("PNG image", ".png"),
        "json" => ("JSON data", ".json"),
        "xml" => ("XML data", ".xml"),
        "worksheettxt" or "dailytxt" or "forecasttxt" or "timingtxt" or "davisontxt" or "inventorytxt" or "mirrortxt"
            => ("Text file", ".txt"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // The request for an export of `kind` of what `vm` is showing now, or null with the
    // reason (for the status bar) if that cannot be exported yet.
    public static ExportRequest? Prepare(MainViewModel vm, string kind, out string refusal)
    {
        var (label, extension) = Format(kind);
        refusal = "";

        var chart = vm.ChartVM.Chart;
        if (chart is null)
        {
            refusal = "Select a chart first, then export.";
            return null;
        }
        // Someone else has just been picked and their chart is still being calculated:
        // what is on screen is the person before, and must not be saved as this one.
        if (!vm.ChartIsCurrent)
        {
            refusal = "The chart is still being calculated — try again in a moment.";
            return null;
        }
        string name = chart.Celebrity.Name;
        static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // The daily exports write the reading already on screen (for whatever date the
        // Daily view is set to) rather than recalculating it — but only once it is the
        // reading for this chart, not the previous one still showing while this one loads.
        var daily = vm.DailyVM.Reading;
        bool isDaily = kind.StartsWith("daily", StringComparison.Ordinal);
        if (isDaily && (daily is null || vm.DailyVM.IsBusy || daily.Name != name))
        {
            refusal = "The daily horoscope is not ready yet — try again in a moment.";
            return null;
        }

        var forecast = vm.ForecastVM.Reading;
        bool isForecast = kind.StartsWith("forecast", StringComparison.Ordinal);
        if (isForecast && (forecast is null || vm.ForecastVM.IsBusy || forecast.Name != name))
        {
            refusal = "The forecast is not ready yet — open the Forecast view, then try again in a moment.";
            return null;
        }

        var timing = vm.TimingVM.Reading;
        bool isTiming = kind.StartsWith("timing", StringComparison.Ordinal);
        if (isTiming && (timing is null || vm.TimingVM.IsBusy || timing.Name != name))
        {
            refusal = "The solar return and progressions are not ready yet — open the Timing view, then try again in a moment.";
            return null;
        }

        // Likewise the synastry exports write the comparison already on screen, and the
        // Davison exports the Davison chart of the same two people.
        var comparison = vm.SynastryVM.Comparison;
        var synastry = vm.SynastryVM.Reading;
        var davison = vm.SynastryVM.Davison;
        bool isSynastry = kind.StartsWith("synastry", StringComparison.Ordinal);
        bool isDavison = kind.StartsWith("davison", StringComparison.Ordinal);
        if ((isSynastry || isDavison) &&
            (comparison is null || vm.SynastryVM.IsBusy || !ReferenceEquals(comparison.First, chart) ||
             (isSynastry ? synastry is null : davison is null)))
        {
            refusal = "Open the Synastry view and choose someone to compare with first, then export.";
            return null;
        }

        // The personality profile, or the chart-and-answers reading, as text: written
        // out here and now, so that it is this person's whatever is selected next.
        byte[]? text = null;
        if (kind is "inventorytxt" or "mirrortxt")
        {
            text = vm.InventoryVM.ExportText(chart, reading: kind == "mirrortxt");
            if (text is not { Length: > 0 })
            {
                refusal = "There is no personality profile for this chart yet: open the Inventory view and answer the questionnaire first.";
                return null;
            }
        }

        string suggested =
            isDaily ? $"{name} - daily {Day(daily!.Date)}" :
            isSynastry ? $"{synastry!.FirstName} and {synastry.SecondName} - synastry" :
            isDavison ? $"{davison!.FirstName} and {davison.SecondName} - Davison chart" +
                        (kind == "davisonsheetpng" ? " with tables" : kind == "davisontxt" ? " worksheet" : "") :
            kind == "worksheettxt" ? $"{name} - worksheet" :
            kind == "inventorytxt" ? $"{name} - personality profile" :
            kind == "mirrortxt" ? $"{name} - chart and answers" :
            kind == "sheetpng" ? $"{name} - chart with tables" :
            isTiming ? $"{name} - return and progressions {Day(timing!.AsOf)}" :
            isForecast ? $"{name} - forecast from {Day(forecast!.Start)}" :
            name;

        return new ExportRequest
        {
            Kind = kind,
            Label = label,
            Extension = extension,
            SuggestedName = SafeFileName(suggested),
            Chart = chart,
            Report = vm.ChartVM.ReportSections,
            Worksheet = vm.ChartVM.Worksheet,
            Sensitivity = vm.ChartVM.Sensitivity,
            Daily = isDaily ? daily : null,
            Forecast = isForecast ? forecast : null,
            Timing = isTiming ? timing : null,
            Comparison = isSynastry || isDavison ? comparison : null,
            Synastry = isSynastry ? synastry : null,
            Davison = isDavison ? davison : null,
            Text = text,
        };
    }

    public static string SafeFileName(string name)
    {
        var cleaned = string.Join("_", name.Split(Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(cleaned) ? "chart" : cleaned;
    }
}
