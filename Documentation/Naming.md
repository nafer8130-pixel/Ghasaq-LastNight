# Naming contract — غَسَق: الليلة الأخيرة / GHASAQ: THE LAST NIGHT

This file is the authority for the product's names and the vocabulary of its
systems. It exists so that code, content, store listings and community text use
one set of original terms, and so that the retired words of the earlier working
title cannot come back in through a rename, a comment or a test string.

## Identity

| What | Value |
| --- | --- |
| Arabic title | غَسَق: الليلة الأخيرة |
| English title | GHASAQ: THE LAST NIGHT |
| Internal name (project, code, packages, files) | `ghasaq` — never the earlier working title |
| Android package | `com.ghasaq.thelastnight` |
| Godot C# assembly | `Ghasaq` (`Ghasaq.csproj`, `Ghasaq.sln`) |
| Engine-free core assembly | `Ghasaq.Core` (`Tests/Ghasaq.Core.Build`) |
| Test assembly | `Ghasaq.Core.Tests` |
| Display labels | Arabic-first: `config/name` in `project.godot` and the Android launcher label are `غَسَق: الليلة الأخيرة` |

## The dictionary (system names)

Every system has one Arabic name, one English name, and a definition of what it
is and what it must not be:

| Arabic | English | What it is | What it must not be |
| --- | --- | --- | --- |
| الوَسْم | Sigil | One combat-changing mark per run; swapped at the Hearth | Not a gacha character pull |
| الثمن | Price | The visible mechanical cost of a Sigil, shown on the HUD | Never hidden, never a text-only curse |
| الأثر | Reliquary | Gear loot with prefixes, dismantling and crafting | Not spirit-bound loot tied to an enemy |
| الجمرة | Cinder | One equippable support ability, taken from bosses | Not a growing permanent companion |
| الحجاب | Veil | Account progression ladder: قبس → فتيل → مشكاة → موقد → كسوف (Spark → Wick → Lantern → Pyre → Eclipse) | Not another work's rank names |
| السُخام | Soot | Ghasaq residue: in-run build-up that drives the Dimming, and the upgrade currency | Not a corruption bar that loses the character |
| الموقد | Hearth | The between-runs hub: lamp, anvil, map, Sigil swap, Cinder, battle pass | Not a territory or a PvP kingdom |
| المَيْدان | Ashfield | The 6–10 minute action map — the heart of the product | Not an open world |
| مدّ الليل | Night Tide | Weekly live event | Not a borrowed invasion mode |

## Retired words

These words must not appear in the repository as identifiers, display strings,
file names or document text (case-insensitive):

| Retired | Replacement | Why |
| --- | --- | --- |
| `shadowbound` | `ghasaq` | The earlier working title is retired: name collisions and proximity to existing works |
| `umbra`, `umbral` | `ghasaq` | The living dusk is الغَسَق; the earlier draft's separate entity name is retired |
| `shadow power` / `shadowpower` | `ghasaq power` | Shadow is allowed as visual lighting only, never as a power system |
| `warden` | `sigilbearer` | The player accepted a Sigil to live one more night; the old role name is retired |
| `veilwarden` | `veilwatch` | Elite creature renamed with the role |
| `memory`, `memories` | (no gameplay use; prose uses "remembers") | The spiritual-loot system is gone; gear drops belong to the Reliquary |
| `echo`, `echoes` | `cinder` | The captive growing-enemy concept is gone |
| `aspect`, `aspects` | `sigil` | The unique-power-with-a-curse concept is replaced by the Sigil/Price pair |
| `flaw`, `flaws` | `price` | The mechanical cost is the Price, shown on the HUD |
| `domain`, `domains` | `hearth` | There is a hub, not a player-ruled realm |
| `corruption` | `veil` (ladder) / `soot` (residue) | Progression and residue are two named systems, not one bar |
| `awakened`, `ascended`, `sacred`, `divine` | the Veil ladder names | Ranks are قبس / فتيل / مشكاة / موقد / كسوف |
| `nightmare`, `dream realm` | — | Replaced by original setting material |
| package id `com.shadowbound.thelastnight` | `com.ghasaq.thelastnight` | Packages carry the internal name |
| `ItemRarity.Umbral` | `ItemRarity.Eclipse` | Drop tiers use the dictionary too |
| `Memory of Ash` / `memory-of-ash` | `Soot Draught` / `soot-draught` | Item names use the dictionary |
| `InMemorySaveStorage` | `InProcessSaveStorage` | Even a technical identifier avoids the retired word |

## The gate

```bash
bash Tools/check-naming.sh
```

It scans tracked files (case-insensitive) and fails if any retired word above is
present. The exemptions are deliberately narrow and are engine or shell
built-ins, not content:

- `window/stretch/aspect` — a Godot project setting key.
- `echo` as the shell builtin (`.sh` files and workflow `.yml`/`.yaml`).
- `"echo":` — Godot's serialised `InputEventKey` property in `project.godot`
  and `scenes/`.
- Visual shadow/lighting names such as `shadow_enabled` — shadow as lighting is
  allowed; only the retired *systems* are banned, by name.

Two files necessarily contain the retired words and are skipped: this contract
and the gate script itself. The approved production plan
([Plan-v2.md](Plan-v2.md)) is an archival document whose change log quotes the
retired names; it is skipped for the same reason.
