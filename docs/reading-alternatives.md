# Authored reading alternatives

5.1 adds `content/reading-variants.json` to the five existing reading corpora.
Each key identifies an existing prose string, for example
`interpretations/sunSigns/Aries` or `synastry/tones/Mixed`. Its array contains
additional complete treatments of the same interpretation. The original string
remains an available treatment. Nothing is generated at runtime.

| Reader | Entries expanded | Additional treatments |
| --- | ---: | ---: |
| Natal | 136 | 189 |
| Daily and forecast | 58 | 91 |
| Synastry and Trine | 45 | 65 |
| Davison | 60 | 60 |
| Self-report | 26 | 26 |
| Total | 325 | 431 |

The expansion covers Sun, Moon and rising profiles; the seven classical planets
in signs; elemental and modal balance; daily Moon signs and phases; day tones,
retrogrades and selected personal transits; pair and trio tones, element pairings
and selected contacts; Davison profiles and Sun/Moon houses; and the self-report's
higher/lower expressions. It does not change questionnaires, scoring or aspect
geometry, and contains no writing advice.

The seed uses the birth instant and Ascendant, with the date/range and zone added
for daily/forecast readings. Group seeds sort their member seeds so reversing the
people does not roll the wording again. Names and current runtime randomness
are excluded. Changing a name therefore changes references without selecting a
new treatment. Three pair summaries in a trio avoid choosing identical text when
their categories match. Fixed method explanations and numerical facts stay exact.

Each composition gets a private corpus snapshot. Interpreters never store a
mutable shared seed. Original-only standalone fixtures remain supported when no
sidecar is present; installed releases require the sidecar. Paths, nonempty text,
duplicate choices and the exact set of placeholders are validated when readers
load. An alternative must preserve point ownership: in a synastry contact, `{a}`
owns the first named point and `{b}` the second.

Writers delegates to these readers. Saved briefs store their text and corpus
version, so a corpus upgrade does not rewrite a saved reading. No writer-specific
interpretive categories or prompt corpus are used.
