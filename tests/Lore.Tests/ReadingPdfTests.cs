using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// The Forecast and Timing readings saved as PDFs. What a PDF says cannot be read back
// here, so these check that one is produced, and that a longer reading makes a longer file.
public class ReadingPdfTests
{
    private static NatalChart Chart() => Repo.Charts.Calculate(Repo.Figure("albert-einstein"));

    private static void AssertIsPdf(byte[] bytes) =>
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

    [Fact]
    public void A_forecast_saves_as_a_PDF_that_grows_with_the_period()
    {
        var chart = Chart();
        var transits = new TransitService(Repo.Charts);
        var interpreter = new DailyInterpreter(Repo.Data("daily.json"));
        var start = new DateOnly(2026, 10, 1);
        byte[] Pdf(int days) => DailyExportService.ForecastToPdf(interpreter.ComposeForecast(
            chart, transits.Forecast(chart, start, days, DateTimeZone.Utc), start, days, DateTimeZone.Utc));

        byte[] month = Pdf(30), quarter = Pdf(91);

        AssertIsPdf(month);
        AssertIsPdf(quarter);
        Assert.True(quarter.Length > month.Length);
    }

    [Fact]
    public void The_solar_return_and_progressions_save_as_a_PDF_with_or_without_a_birth_time()
    {
        var timing = new TimingService(Repo.Charts);
        var timed = timing.Compose(Chart(), new DateOnly(2026, 10, 4), DateTimeZone.Utc);
        var untimed = timing.Compose(Repo.Charts.Calculate(Demo.Person(timed: false)), new DateOnly(2026, 10, 4), DateTimeZone.Utc);

        AssertIsPdf(DailyExportService.TimingToPdf(timed, returnWheelPng: null));
        AssertIsPdf(DailyExportService.TimingToPdf(untimed, returnWheelPng: null));
    }
}
