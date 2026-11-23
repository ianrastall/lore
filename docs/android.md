# Lore for Android

Started 22 November 2026, when Ian resumed Lore after shelving it on 9 November. Version 0.1.0 is a first build, not yet released.

## Decisions (Ian, 22 November 2026)

- **Stack: .NET MAUI over the existing C# core.** Jupiter's Android precedent (Offline Maps) is Kotlin and Jetpack Compose over a Rust core, but Lore's core is 19,000 lines of tested C#, and Kotlin cannot call it. With MAUI the phone runs the same chart, reading and library code as Windows, and the core's view models bind to the phone's pages directly. Recorded in [Jupiter exceptions](jupiter-exceptions.md#android-edition).
- **First scope:** the library and My Charts (with city and hospital search), the chart wheel and Report, Daily, Sky, Synastry (with matches and the Davison chart) and Timing.

Left out for now: Forecast, Worksheet, Inventory, Writers, Trine, the Legend, Settings (the phone uses the defaults, or a `settings.json` already in its data folder), My Charts import and export, and every export. The PDF exports cannot come as they are: QuestPDF has no Android build.

## Layout

| What | Where |
| --- | --- |
| App | `src/Lore.Mobile` (MAUI, `net10.0-android`, app ID `io.github.ianrastall.lore`) |
| Core for Android | `src/Lore.Core.Android`: builds `src/Lore.Core`'s own source files as `Lore.Core` for any processor. Nothing is written there; the root `Directory.Build.props` holds the Windows projects to x64. |
| Wheel | `Views/WheelRenderer.cs`, a SkiaSharp port of the Windows `ChartRenderer`. A change to one belongs in the other. |
| Swiss Ephemeris | `build/build-sweph-android.ps1` compiles `libsweph.so` for arm64-v8a and x86_64 (16 KB pages) into `Native/android/` (not committed). The core's import is now `sweph`, which .NET resolves to `sweph.dll` on Windows and `libsweph.so` on Android. |
| Content | `content/*.json` and the ephemeris files are packed in the APK and copied on first start (and after each update) to the app's private `files/bundle/<build>-<install time>/`, which the core reads by path. A half-finished copy is never used. |
| Reader's data | `files/Lore/` (the core's `AppFolder`: `mycharts.json`, `settings.json`). Android backups are off for the whole app. |
| Glyphs | Noto Sans Symbols and Symbols 2 (SIL OFL 1.1) are packed for the wheel and the tab icons; ordinary text adds U+FE0E after each symbol so Android does not draw zodiac signs as emoji. |

## Building

```powershell
pwsh build\build-android.ps1              # tests, libsweph.so, signed APK in dist\ with its SHA-256
pwsh build\build-android.ps1 -SkipTests
```

The script needs a .NET 10 SDK with the `maui-android` workload. On Ian's machine that is `C:\Program Files\dotnet` (SDK 10.0.115, workload set 10.0.115), not the `D:\lang\dotnet\sdk` on the path: that SDK's workload installs are MSI-based, and its MSIs install into `C:\Program Files\dotnet`, leaving `D:\lang\dotnet\sdk` with stale 10.0.0 manifests. Two elevated installs on 22 November (approved by Ian) put the workload in place. The script uses the Program Files SDK when it exists; `-Dotnet` picks another.

The Android SDK and NDK are at `D:\lang\android` (`ANDROID_HOME`), Java at `D:\lang\java\current`. Ian accepted the Android SDK licence on 15 November 2026 (see Offline Maps).

**Signing.** The release key was made on 22 November 2026 at `%LOCALAPPDATA%\Jupiter\Lore\AndroidSigning\lore.keystore` (alias `lore`, RSA 4096), with its password in `signing.credential.xml` protected by DPAPI for Ian's account. Every later APK must be signed with it, or it will not install over the one before: keep that folder. Certificate SHA-256 `4b7304d2815edcc8e49fc42f9c8f8f5eae8ee5795c625d90c0722980a265df50`.

**Not trimmed, not compiled ahead of time.** The core's JSON is read by reflection, so trimming is off, and Mono's AOT compilation needs trimming. The app is compiled as it runs. Trimming with the core's types preserved, then AOT, would shorten the first start; it has not been tried.

For a debug build on a device or emulator: `dotnet build src\Lore.Mobile -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true`, then `adb install -r` the `-Signed.apk`. A debug build is debuggable, so `adb shell run-as io.github.ianrastall.lore cat cache/lore-crash.txt` reads its log.

## Verification, 22 November 2026

On the Android 16 emulator (x86_64, API 36), debug build:

- First start unpacked the content and loaded all 11,163 figures with their dignity marks (Swiss Ephemeris through `libsweph.so`). The library's scoring pass took about 80 seconds in this debug build on the emulator; the tabs are shown first and the list fills in when it is done.
- Einstein: the wheel (houses, angles, aspects, glyphs, the green Extraordinary rim), dignity +33 Extraordinary, Report, Daily with its transit bi-wheel, Timing with the 2026–2027 solar return and its wheel, and Synastry: 11,162 matches, then the reading and bi-wheel with Don Fisher.
- Sky for 22 November 2026 with its week strip and sky wheel.
- A new chart (Chicago from the city search) saved with America/Chicago and UTC−6, and opened on its wheel; Edit and Delete are offered for it.
- Fixed on the way: a stall (ANR) building all 11,162 matches at once (now a virtualised list), the solar-return wheel not showing, "Demanding" cut off in the week strip, and labels running off the screen.

The signed release APK (92,135,497 bytes, SHA-256 `9afc713d9e492de6eeeb87059ae11947d972d27e65b5c5102a35f611a5ef7671`) verifies with `apksigner`, is 16 KB aligned, and installed and loaded the library on the emulator.

**Not yet done:** installing on Ian's Galaxy S26 FE (its ADB connection dropped before the install), timing the first start and the scoring pass on the phone, an upgrade install that keeps My Charts, dark theme, TalkBack, and publication.
