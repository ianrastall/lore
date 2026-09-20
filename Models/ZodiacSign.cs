namespace Lore.Models;

public enum ZodiacSign
{
    Aries = 0, Taurus, Gemini, Cancer, Leo, Virgo,
    Libra, Scorpio, Sagittarius, Capricorn, Aquarius, Pisces
}

public enum Element { Fire, Earth, Air, Water }
public enum Modality { Cardinal, Fixed, Mutable }

public static class ZodiacSignExtensions
{
    private static readonly string[] Symbols =
        ["♈", "♉", "♊", "♋", "♌", "♍", "♎", "♏", "♐", "♑", "♒", "♓"];

    private static readonly string[] Names =
        ["Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
         "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"];

    public static string Symbol(this ZodiacSign s) => Symbols[(int)s];
    public static string Name(this ZodiacSign s) => Names[(int)s];

    public static ZodiacSign FromLongitude(double eclipticLongitude)
    {
        double normalized = ((eclipticLongitude % 360) + 360) % 360;
        return (ZodiacSign)(int)(normalized / 30);
    }

    public static double DegreeInSign(double eclipticLongitude)
    {
        double normalized = ((eclipticLongitude % 360) + 360) % 360;
        return normalized % 30;
    }

    public static Element GetElement(this ZodiacSign s) => s switch
    {
        ZodiacSign.Aries or ZodiacSign.Leo or ZodiacSign.Sagittarius => Element.Fire,
        ZodiacSign.Taurus or ZodiacSign.Virgo or ZodiacSign.Capricorn => Element.Earth,
        ZodiacSign.Gemini or ZodiacSign.Libra or ZodiacSign.Aquarius => Element.Air,
        _ => Element.Water
    };

    public static Modality GetModality(this ZodiacSign s) => s switch
    {
        ZodiacSign.Aries or ZodiacSign.Cancer or ZodiacSign.Libra or ZodiacSign.Capricorn => Modality.Cardinal,
        ZodiacSign.Taurus or ZodiacSign.Leo or ZodiacSign.Scorpio or ZodiacSign.Aquarius => Modality.Fixed,
        _ => Modality.Mutable
    };
}
