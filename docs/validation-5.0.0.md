# Validation: Lore 5.0.0

1 November 2026. Writers: character birth anchors and historical pair/trio discovery.

## Final direction

Uses the existing natal, synastry and Trine readers, measures and order. Ian
rejected the prototype's writing prompts and invented interaction categories;
both were removed before delivery. No writer-specific prose corpus ships.
Historical groups require the same field (library category), nearby birthplaces,
at most ten years between oldest and youngest, and every member deceased for
at least fifty years. Today's death cutoff is 1 November 1976.

## Passed

| Check | Result |
| --- | --- |
| Full core suite | 632 passed, 0 failed |
| Writers tests | 24 passed: reader/geometry parity, all-member filters, death boundaries, missing data, exact events, historical mean time, cancellation, snapshots and private-store release rejection |
| CI for source commit `c19a243` | Passed |
| Debug build | 0 warnings/errors; Jupiter style check at Error severity |
| Release publish and self-check | Passed |
| Packaging | Installer and portable zip produced |
| Personal-data gate | Passed: 11,163 figures, 0 personal charts |

## Complete-library discovery

At 100 km with all four mandatory filters, 2,502 figures pass the initial
historical/data requirements. Each complete search takes about 0.4 seconds:

| Search | Results |
| --- | --- |
| Pairs | All 3,068 |
| Trios | All 5,193 |
| Golden trios | All 148 |

Every eligible group is assessed. The default retained limit of 10,000 shows all
these results. Fast pair assessments and three-person geometry are checked
against the existing implementations; readings call the existing interpreters.

## Desktop checks

Isolated debug profiles were used for the tab without a selected chart, city
selection, New/Full Moon discovery, naming/saving without adding My Charts,
the underlying chart preview and a completed native PDF export. The initial pair
finder and narrow detail navigation were exercised before Ian's final death/field
requirements; those final requirements have core parity/eligibility tests and a
complete-library benchmark.

Checks led to fixes for a date-picker default appearing a day early, clipped
search buttons, space for readings, preview selection after saving and chart
window sizing at 200% DPI. Narrow detail views hide the search form and offer
Back to results. The final debug launch logged no new unhandled exceptions.
A full manual session through every final historical filter remains unverified.

## Installed

5.0.0 over 4.5.1 at `D:\Lore`, keeping the all-users install.

| Check | Result |
| --- | --- |
| Installer | Exit 0 |
| Apps registration | Lore 5.0.0 at `D:\Lore` |
| Installed self-check | Exit 0, OK |
| Start Menu | Shortcut targets `D:\Lore\Lore.exe`; launched through it, responding, 11,163 figures and 4 personal charts loaded, then closed cleanly |
| Saved data | All 5 existing files retained SHA-256 checksums after upgrade and launch |

Daily caches were excluded because the HUD updates them. No screenshot or
accessibility-tree capture was taken of the installed copy's personal charts.
Private logs, fixtures and baseline hashes remain under ignored
`artifacts/qa-5.0.0`, outside the release assets.
