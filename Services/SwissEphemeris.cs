using System.Runtime.InteropServices;

namespace Lore.Services;

// P/Invoke wrapper for the Swiss Ephemeris C library (sweph.dll, x64).
// Download the DLL and ephemeris data files from https://www.astro.com/swisseph/
// Place sweph.dll in the Native\ folder and *.se1 data files in Assets\Ephemeris\.
internal static partial class SwissEphemeris
{
    private const string Dll = "sweph.dll";

    // Planet body numbers (ipl parameter)
    public const int SE_SUN        = 0;
    public const int SE_MOON       = 1;
    public const int SE_MERCURY    = 2;
    public const int SE_VENUS      = 3;
    public const int SE_MARS       = 4;
    public const int SE_JUPITER    = 5;
    public const int SE_SATURN     = 6;
    public const int SE_URANUS     = 7;
    public const int SE_NEPTUNE    = 8;
    public const int SE_PLUTO      = 9;
    public const int SE_MEAN_NODE  = 10; // North Node (mean)
    public const int SE_CHIRON     = 15;

    // Calculation flags (iflag)
    public const int SEFLG_SWIEPH = 2;   // use Swiss Ephemeris data files
    public const int SEFLG_SPEED  = 256; // include speed in result array

    // ascmc array indices
    public const int SE_ASC    = 0;
    public const int SE_MC     = 1;
    public const int SE_VERTEX = 3;

    public const int SE_GREG_CAL = 1;

    // ── Native imports ────────────────────────────────────────────────────────

    // CALL_CONV is empty in the MAKE_DLL build without PASCAL defined → default cdecl.
    // On x64 Windows there is only one calling convention anyway, so this is academic.

    [LibraryImport(Dll, EntryPoint = "swe_set_ephe_path", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void SetEphePath(string path);

    [LibraryImport(Dll, EntryPoint = "swe_julday")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial double JulDay(int year, int month, int day, double hour, int gregflag);

    [LibraryImport(Dll, EntryPoint = "swe_calc_ut")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int CalcUt(
        double tjdUt,
        int ipl,
        int iflag,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 6)] double[] xx,
        nint serr); // pass nint.Zero (null) — error text is not used

    [LibraryImport(Dll, EntryPoint = "swe_houses")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int Houses(
        double tjdUt,
        double geolat,
        double geolon,
        int hsys,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 13)] double[] cusps, // [0] unused, [1..12] = house cusps
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 10)] double[] ascmc);

    [LibraryImport(Dll, EntryPoint = "swe_close")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static double DateTimeToJulianDay(DateTime utc)
    {
        double hour = utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0;
        return JulDay(utc.Year, utc.Month, utc.Day, hour, SE_GREG_CAL);
    }
}
