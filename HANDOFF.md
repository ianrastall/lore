# Lore handoff

Version 4.0.0 · 31 October 2026 · Repository `D:/jupiter/lore`, branch `main`, remote `ianrastall/lore`

## Where things live

| What | Where |
| --- | --- |
| Shell | `src/Lore.App` (WinUI 3, Jupiter.UI, Win2D). Builds `Lore.exe`. |
| Domain core | `src/Lore.Core` (C#; see [exceptions](docs/jupiter-exceptions.md)) |
| Content | `content/` in the repository, `Data\` in the installed app |
| Swiss Ephemeris | `third_party/swisseph`, compiled to `Native/sweph.dll` (not committed) |
| Release | `build/build.ps1`, `build/installer.iss` |
| Installed app | Ian's machine: `D:\Lore`, all-users install (admin needed to upgrade) |
| Reader's data | `%LOCALAPPDATA%\Lore`. Never in the repository. |

## Commands

```powershell
pwsh build\build.ps1                # tests, publish, self-check, personal-data gate, installer
pwsh build\build.ps1 -SkipTests     # packaging only
dotnet build src\Lore.App -c Debug
dotnet test tests\Lore.Tests
```

A debug build honors `LORE_HOME`, so it can be run with made-up charts. Use it for every screenshot and UI check, so that nobody's real charts or answers are shown. A release build ignores it.

## Toolchain

.NET SDK 10.0.113 or later 10.0 (`global.json`), Windows App SDK 2.5.1, Win2D 1.4.0, Inno Setup 6, Visual Studio Build Tools (MSVC, for `sweph.dll`). Package versions are in `Directory.Packages.props`. Jupiter.UI must sit beside this repository at `..\jupiter-ui`.

When the XAML compiler reports unknown types with `WMC9999` or `WMC1509`, the cause is usually a C# error hidden in the pre-compile. Run `dotnet build -v d` and look for `error CS`.

## Delivered

See [validation](docs/validation-4.0.0.md) for what was tested, [migration](docs/jupiter-migration.md) for what moved, and [exceptions](docs/jupiter-exceptions.md) for recorded departures.

## Next

1. **Synastry trines.** Ian's first improvement after this migration, and a very important part of the app. The existing synastry code is in `src/Lore.Core/Services/SynastryService.cs` (contacts), `SynastryInterpreter.cs` (the reading, with trines and sextiles together under "What comes easily", four at most) and `src/Lore.App/Views/SynastryView.xaml`. Exactly what the section shows still needs Ian's definition.
2. **Rust core decision.** Open; see the exceptions record.
3. **Theme setting.** Needs a light wheel palette.
