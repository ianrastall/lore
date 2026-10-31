using System.Runtime.InteropServices;

// sweph.dll is looked for beside Lore itself (and, for Windows' own libraries, in
// System32) and nowhere else. Without this a copy of Lore whose DLL had gone missing
// would load one of the same name from whatever folder it happened to be started in.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]

namespace Lore.Services;

// P/Invoke wrapper for the Swiss Ephemeris C library (sweph.dll, x64).
// Build-SwephDll.ps1 compiles the DLL into Native\ from the source bundled under Data\;
// the .csproj copies it beside the exe and the *.se1 data files to Assets\Ephemeris\.
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
    public const int SE_TRUE_NODE  = 11; // North Node (true / osculating)
    public const int SE_MEAN_APOG  = 12; // Black Moon Lilith (mean lunar apogee)
    public const int SE_OSCU_APOG  = 13; // Black Moon Lilith (true / osculating lunar apogee)
    public const int SE_CHIRON     = 15;
    public const int SE_ECL_NUT    = -1; // not a body: obliquity of the ecliptic and nutation

    // Calculation flags (iflag)
    public const int SEFLG_SWIEPH = 2;   // use Swiss Ephemeris data files
    public const int SEFLG_SPEED  = 256; // include speed in result array
    public const int SEFLG_EQUATORIAL = 2048; // right ascension / declination instead of longitude / latitude

    // ascmc array indices
    public const int SE_ASC    = 0;
    public const int SE_MC     = 1;
    public const int SE_ARMC   = 2;
    public const int SE_VERTEX = 3;

    public const int SE_GREG_CAL = 1;

    // Kinds of eclipse, as bits in what the eclipse searches return.
    public const int SE_ECL_TOTAL         = 4;
    public const int SE_ECL_ANNULAR       = 8;
    public const int SE_ECL_PARTIAL       = 16;
    public const int SE_ECL_ANNULAR_TOTAL = 32; // annular along part of the track, total along the rest
    public const int SE_ECL_PENUMBRAL     = 64;

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

    // Returns a negative value when the requested system cannot be calculated at this
    // latitude (Placidus and Koch inside the polar circles); the cusps are then Porphyry.
    [LibraryImport(Dll, EntryPoint = "swe_houses")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int Houses(
        double tjdUt,
        double geolat,
        double geolon,
        int hsys,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 13)] double[] cusps, // [0] unused, [1..12] = house cusps
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 10)] double[] ascmc);

    // Houses from the sidereal time (ARMC) directly, rather than from a moment and a
    // longitude: what a progressed Midheaven needs to find the Ascendant that goes with it.
    [LibraryImport(Dll, EntryPoint = "swe_houses_armc")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int HousesArmc(
        double armc,
        double geolat,
        double eps,
        int hsys,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 13)] double[] cusps,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 10)] double[] ascmc);

    // The next eclipse of the Sun seen from anywhere on Earth after tjdStart (ifltype 0:
    // of any kind). Returns the kind as SE_ECL_* bits, negative on error; tret[0] is the
    // moment of greatest eclipse.
    [LibraryImport(Dll, EntryPoint = "swe_sol_eclipse_when_glob")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int SolEclipseWhenGlob(
        double tjdStart,
        int ifl,
        int ifltype,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 10)] double[] tret,
        int backward,
        nint serr);

    // The same for eclipses of the Moon.
    [LibraryImport(Dll, EntryPoint = "swe_lun_eclipse_when")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int LunEclipseWhen(
        double tjdStart,
        int ifl,
        int ifltype,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 10)] double[] tret,
        int backward,
        nint serr);

    public const int SE_CALC_RISE = 1;
    public const int SE_CALC_SET  = 2;

    // The next rising or setting (rsmi) of a body after tjdUt at a place; geopos is
    // longitude, latitude and height in metres. By default the moment the upper edge of
    // the disc touches the horizon, with refraction. Returns −2 if the body does not rise
    // or set there (the midnight Sun), negative on error; tret[0] is the moment.
    [LibraryImport(Dll, EntryPoint = "swe_rise_trans")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int RiseTrans(
        double tjdUt,
        int ipl,
        nint starname,   // null: a planet, not a star
        int epheflag,
        int rsmi,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] geopos,
        double atpress,
        double attemp,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 1)] double[] tret,
        nint serr);

    [LibraryImport(Dll, EntryPoint = "swe_close")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void Close();

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static double DateTimeToJulianDay(DateTime utc)
    {
        double hour = utc.TimeOfDay.TotalHours; // fractions of a second included
        return JulDay(utc.Year, utc.Month, utc.Day, hour, SE_GREG_CAL);
    }

    // Inverse of the above for dates in the Gregorian calendar: JD 2440587.5 is the
    // Unix epoch, and a Julian day is exactly 86,400 seconds.
    public static DateTime JulianDayToDateTime(double jd) =>
        DateTime.UnixEpoch.AddSeconds((jd - 2440587.5) * 86400.0);
}
