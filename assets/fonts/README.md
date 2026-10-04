# The bundled UI fonts

The game's authored text is Arabic-first: the Sigil names, and the verb and
الثمن / Price lines the HUD must show at all times
([Sigils.md](../../Documentation/Sigils.md), plan §3.2). Godot's built-in theme
font carries no Arabic glyphs at all - measured: `has_char('م')` is false - so
those lines would draw as nothing on every platform, and the one surface the
contract requires would silently not exist.

Two Noto fonts are vendored for that reason, composed by
`ghasaq-ui-font.tres` and selected project-wide by
`gui/theme/custom_font` in `project.godot`:

| File | Role | Size | sha256 |
| --- | --- | --- | --- |
| `NotoSans-Regular.ttf` | Latin, digits, punctuation (the base font) | 512,672 B | `89c3c497f618fdaa0b2d1e98fef93582f28c71debd2c4a8cdf41f190ced2909d` |
| `NotoSansArabic-Regular.ttf` | Arabic, with shaping (the fallback) | 244,072 B | `504d7407d86875acf7d04dfaa0fd7524d0b8797723bc4aa18022f29db25b0b6e` |

Each font covers what the other does not - Noto Sans has no Arabic letters and
Noto Sans Arabic has no Latin letters - so the `.tres` states one as the base
font and the other as its fallback, which is how Godot composes a family. Both
files were taken from the Debian `fonts-noto-core` package, version
`20201225-2`, which is the upstream Noto release of 2020-12-26.

Source: <https://github.com/notofonts> via Debian's `fonts-noto-core`.
License: the SIL Open Font License 1.1 - the full text is in `OFL.txt`.

## Why vendored instead of a system font

`SystemFont` would render or not depending on the fonts a device happens to
ship, and the Price line is required to be readable on the Android build this
project exports, not only on a machine that has an Arabic font installed. The
two files are small next to the APK they ship in (about 0.75 MB of ~103 MB),
and they remove the failure mode entirely.

Godot renders Arabic with the Advanced text server's shaping (HarfBuzz): text
joins, as it must. Measured on this project: `المشكاة` shapes to 75 px at
size 24 where the same letters, unjoined, measure 137 px.
