namespace Lore.Mobile.Views;

// The version, and whose work Lore is built on, under which licences.
public sealed class AboutPage : ContentPage
{
    public AboutPage()
    {
        Title = "About Lore";
        Label Para(string text, string style = "Body") =>
            new() { Text = text, Style = (Style)Application.Current!.Resources[style] };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Lore", Style = (Style)Application.Current!.Resources["Title"] },
                    Para($"Version {AppInfo.VersionString} for Android", "Secondary"),
                    Para("Natal charts, readings and daily horoscopes, worked out on this phone. Nothing is sent anywhere."),
                    Para("Calculations by the Swiss Ephemeris (Astrodienst AG), used under the GNU Affero General Public License, " +
                         "as Lore itself is. The source is at github.com/ianrastall/lore."),
                    Para("The figures' birth data and Rodden Ratings are from Astro-Databank (astro.com), on its own terms, which " +
                         "allow non-commercial use only; many came by way of VedAstro's list of famous people."),
                    Para("Cities: simplemaps World Cities Database (CC BY 4.0). Hospitals: © OpenStreetMap contributors (ODbL), " +
                         "Wikidata and OpenHistoricalMap (CC0 1.0)."),
                    Para("Time zones from the IANA time zone database through NodaTime (Apache License 2.0) and GeoTimeZone (MIT). " +
                         "Built with .NET MAUI and CommunityToolkit.Mvvm (MIT) and SkiaSharp (MIT)."),
                    Para("Glyphs drawn in Noto Sans Symbols and Noto Sans Symbols 2, under the SIL Open Font License 1.1."),
                    Para("Your own charts are kept in this app's private storage, and are removed if the app is uninstalled.", "Secondary"),
                },
            },
        };
    }
}
