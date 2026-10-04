# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## The Reliquary's dismantling and the Hearth page (2026-10-04)

The first half of plan §3.3 lands: gear held in the bag breaks down at the
الموقد / Hearth (plan §3.7) into السُّخام / Soot, and the bank is a permanent
balance that rides every save. The menu gains an `الموقد — HEARTH` page listing,
for each dismantlable piece, the Soot its hammer would pay. Forging (the sink),
affix prefixes and the comparison view are deliberately **not** built yet —
[Reliquary.md](Reliquary.md) states exactly what is and is not.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **633 passed, 0 failed** (317 ms; 619 before this change, 14 new) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 50/50 checks** (33 before; 17 new) |
| The main scene assembles and runs | `godot --headless --path . --quit-after 1800` | **Pass** — 30 s, no errors; `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1, sigil 'lantern', soot 0` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 104,001,758 bytes (99 MB), sha256 `dabba298e00efbe8c8f329335644a0db8877e1c4387d57d24ea3ae30ff2d1746`, signed and verified with `apksigner` |

New in code:

- `Core/Items/SootBank.cs` — the permanent balance, deliberately separate from
  the in-run meter: `Deposit` accumulates, saturates instead of wrapping, and
  ignores non-positive amounts; `LoadFrom` clamps a hand-edited negative value.
- `Core/Items/Salvage.cs` — `SalvageTuning.SootFor(rarity)` (draft yields
  2 / 6 / 15 / 40 / 80 for Common → Mythic) and the seven `SalvageFailure`
  reasons.
- `GameSession.TrySalvage` — the rule: only non-bound gear the bag holds, only
  at a camp region with no hostiles standing (the same reading of "the Hearth"
  the Sigil swap uses). Every check lands before anything moves, so a refused
  dismantle loses nothing; the piece leaving the bag and the soot landing in the
  bank happen in one call. The equipped copy is out of reach by construction —
  it is not in the bag.
- `SaveGame.SootBalance` + `SaveSerializer` (JSON key `soot`) — an older save
  without the field loads with an empty bank (`Deserialize_DefaultsMissingFields`
  pins it).
- `scripts/GameMenu.cs` — `MenuPage.Hearth`: the bank note, one row per
  dismantlable piece with its yield (`فكّ  Ember Relic … -> 15 سُخام`), refusals
  reported as sentences by `DescribeSalvageFailure`, and a link to the Sigil
  stand. Reached from the main page's `الموقد — HEARTH   (سُخام: N)` row.
- Fourteen core tests (`SalvageTests`, `SootBankTests`) plus three assertions in
  `SaveSystemTests` — the yields, the loop, the five refusals, the save
  round-trip, and the bank's arithmetic.
- Seventeen smoke checks — eight for the core loop (refused away from the camp
  without loss, rare gear pays 15, the piece leaves the bag, bound and material
  refusals, save round-trip), seven driving the menu's own buttons, and two
  glyph sweeps of the built rows. The sweeps earned their place immediately:
  the first draft of the salvage row used `→` (U+2192), which the vendored UI
  font does not carry - it would have drawn as tofu on screen while every
  string assertion passed - so the row now uses the menu's `->` convention and
  the sweep measures the built labels, not a copy of them.

**Not measured:** the Hearth page is drawing. The environment still has no
display; the page has been exercised by compilation, by the smoke test pressing
its buttons, and by an error-free headless run, but never *seen*. Whether the
trade ("keep this rare piece" vs "15 Soot") reads clearly with thumbs on a phone
is exactly the question the feel gate in Reliquary.md leaves open.

One detail worth recording: the first version of the menu check entered the camp
with `GameSession.EnterRegion` and left the Grey Wilds' five hostiles standing
on the encounter, and the Hearth duly refused with `InCombat`. That was the rule
binding correctly at the menu; the check now travels through
`GameRoot.TravelTo` — the same call the World page makes — which clears the old
region's encounter and spawns the camp's (none).

Both workflows ran on GitHub for the change, on `7a16ab8`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #15 | **success** — naming gate, purity, the 633 tests, layout, build, smoke (48 checks) |
| `android.yml` | Android ARM64 #15 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

## The Soot pass: the meter and the Dimming (2026-10-04)

The remaining slice work of [Soot.md](Soot.md) is done: committing to a
Ghasaq-keyed ability fills a run-long meter, and past its threshold the العَتْمة
/ Dimming prices the fight in both directions - heavier blows, frailer body -
with the meter and the state shown on the HUD (plan §3.6). The plan's "light
visual distortion" is deliberately **not** built: it is gated on accessibility
settings ("can be turned off", plan §3.6/§6) and this build has no settings
screen; the bar and the عَتْمة mark carry the state until there is one.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **619 passed, 0 failed** (311 ms; 604 before this change, 15 new) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 33/33 checks** (27 before; 6 new) |
| The main scene assembles and runs | `godot --headless --path . --quit-after 1800` | **Pass** — 30 s, no errors or warnings; `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1, sigil 'lantern', soot 0` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,997,662 bytes (99 MB), signed and verified with `apksigner` |

The APK was first built on `6df2aeb` (103,997,662 bytes, sha256
`e1eb6320aeb408129f59cf418762cc1db0ea51103c8604df26d3f9aa6e7381d3`) and rebuilt
byte-size-identical after the wording fix, at `1c79b7d`:
`dd08ed993880e3579814d3c20e1ca4759947b81f162cc34e76904c5f0cadc737`.

New in code:

- `Core/Combat/Soot.cs` — `SootTuning` (draft numbers: 100 full, +20 per
  Ghasaq commitment, −2/s fade, threshold 60, +25% damage dealt and +35% damage
  taken at full) and `SootMeter` (`Soot`, `Fraction`, `IsDimming`,
  `DimmingFraction`, the two multipliers). The bonuses scale from the threshold
  and are exactly zero at it; the meter owns no damage path, so it can never be
  lethal on its own.
- `Combatant.Soot` + `OutgoingDamageMultiplier` / `IncomingDamageMultiplier` —
  the damage pipeline now reads the combined status + Dimming multipliers, and
  `Combatant.Tick` fades the meter while `Revive` washes it off (a retry starts
  at zero, like the Sigil's Price). A combatant without a meter is one null
  check from neutral.
- `AbilityController.TryActivate` — a `UsesGhasaqPower` commitment is charged at
  the moment the stamina is spent, so an interrupted wind-up keeps its soot; a
  refused activation burns nothing.
- `AttackResolver` — both `Resolve` and `ApplyRadialBurst` read the combined
  multipliers, so a dimmed Ash or Glass burst is priced like any other blow.
- `scripts/GameRoot.cs` — the run hands the player its meter
  (`player.Soot = new SootMeter()`); it is run state and never enters a save.
  The readiness line now ends `sigil 'lantern', soot 0`.
- `scripts/Hud.cs` — `DrawSootBar`: a third bar under the vitals, grey while
  clear and dimming toward red, labelled `السُّخام: N / 100` with the word
  `عَتْمة` the moment the state begins. `BarWidth` and `BarLabelSize` are public
  so the smoke test can measure the label the way it is drawn.
- Fifteen core tests (`SootTests`) — the meter, both edges, the fade, the
  threshold, the commitment and refusal rules, revival, and the couplings to
  `AbilityController` and `AttackResolver`.
- Six smoke checks — the kit carries a Ghasaq-keyed ability for the meter to
  follow, the third burn lands exactly on the threshold (still neutral), a full
  meter sharpens both edges, half a minute of rest clears it, and every glyph of
  the bar's label exists in the font and fits the bar.

**Not measured:** the bar itself is drawing, and this environment still has no
display. Its layout, colours and the عَتْمة mark have been exercised by
compilation, by the label-fit measurement and by a 30-second error-free headless
run, but never *seen*; whether the Dimming *feels* like a fair trade is exactly
the question the plan says only a device and a player can answer.

One CI run is worth recording. `ci.yml` #12 failed on the first push
(`6df2aeb`): `check-naming.sh` greps **tracked** files, and the three new files
were still untracked when the gate was run locally - so the gate was green on
this machine and red on the runner. The wording was fixed in `1c79b7d` (the two
files rewritten around the retired word) and nothing else about the change
moved; the local habit is now to run the gate after `git add`, not before.

The workflows ran on GitHub for the fix, on `1c79b7d`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #13 | **success** — naming gate, purity, the 619 tests, layout, build, smoke (33 checks) |
| `android.yml` | Android ARM64 #13 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

(The Android export of the first push, #12, had succeeded as well: the naming
gate lives only in `ci.yml`.)

## The juice pass: telegraph, hit-stop and the hit spark (2026-10-04)

Phase A's mandatory juice, minus what already existed (camera shake, the colour
flash, the floating damage number). The plan's numbers were already pinned in
`CombatTuning` (section 7: ≥ 400 ms telegraph, 40–80 ms hit-stop); what was
missing was that the fight actually showed them.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **604 passed, 0 failed** (285 ms; 601 before this change) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** — also covers the two new view scripts |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 27/27 checks** (24 before; 3 new) |
| The main scene assembles and runs | `godot --headless --path . --quit-after 1800` | **Pass** — 30 s, no errors or warnings |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,993,566 bytes (99 MB) |

New in code:

- `Core/Combat/Combatant.Struck` — an event raised only when a blow actually
  lands through `ReceiveDamage`: not for a bleed or famine tick (those still
  report through `Damaged`), and not for a blow that was refused outright. That
  lets the view treat "a hit" and "health changed" as different things.
- `scripts/BattleFeedback.cs` — where feedback falls inside the signed budgets:
  hit-stop scales inside the 40–80 ms band (a chip takes the minimum; a blow
  worth 15% of the victim's health, or any crit, takes the maximum), the camera
  kicks hardest when the player is the one hit, and the telegraph ramps from 0
  at commit to 1 at the blow.
- `scripts/CombatantView.cs` — an enemy view that tracks its `AbilityController`
  warms toward the warning colour as the wind-up approaches, so the ≥ 400 ms
  window is something the player reads rather than something only the validator
  knows; `Hit` now fires from `Struck`, so shake, hit-stop and the spark answer
  landed blows only.
- `scripts/HitSpark.cs` + `GameRoot` — a pool of eight code-built sparks
  (unshaded, expanding and fading over 0.16 s, cycled per hit, no allocation at
  hit time), and the hold itself: a landed blow stops the host from stepping the
  simulation for its band seconds, so the frame freezes. The core is still the
  only authority over state; the host only chooses when to step it.
- Three core tests pin the `Struck` line: a landed blow strikes, a damage over
  time tick does not, and a refused blow does not.
- Three smoke checks pin the arithmetic: the hit-stop band holds and grows with
  the blow, a crit takes a heavier stop, and the telegraph ramps from 0 to 1 and
  is silent when ready.

**Not measured:** all of this is drawing, and this environment still has no
display. The telegraph colour, the spark and the hold have been exercised by
compilation, by the rules' checks and by a 30-second error-free headless run,
but never *seen*. Whether a 40–80 ms hold feels right is exactly the question
the plan says only a device and a player can answer.

The workflows ran on GitHub for this change, on `0fb9ed3`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #10 | **success** — naming gate, purity, the 604 tests, layout, build, smoke (27 checks) |
| `android.yml` | Android ARM64 #10 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

## The Price surface: the Sigil on the HUD (2026-10-04)

The remaining slice work of [Sigils.md](Sigils.md) is done: every carried ثمن /
Price is drawn on the HUD at all times (plan §3.2), the Silence lock is marked on
the button it takes away, the swap is refused away from the Hearth's camp, and
the Arabic lines have a font to draw with.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **601 passed, 0 failed** (299 ms; 600 before this change) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** — now also checks the UI font files and the theme setting |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 24/24 checks** (16 before; 8 new) |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1, sigil 'lantern'` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,985,224 bytes (99 MB) |

New in code:

- `scripts/Hud.cs` — the Sigil surface, drawn every frame while a Sigil is
  carried: its name, its verb and its Price (in a distinct colour, as the
  contract asks), plus the live state where the loadout records one - the
  Glass shield's remaining-of-capacity, its reform clock and the exposure, the
  Hunger famine clock and the famine itself, and the name of the ability
  Silence has locked. The locked button itself carries a red frame and a `×`,
  and tapping it shows the Price line instead of doing nothing.
- `scripts/GameMenu.cs` — the Hearth's sigil stand: what is carried and what it
  costs, then the five to choose from, each row carrying its own Price. The
  menu asks; the simulation decides.
- `Core/Simulation/GameSession.cs` — the swap is now refused outside a camp
  (`SigilEquipFailure.NotAtHearth`) as well as mid-fight, and the session lists
  the Sigils it was handed (`GameSession.Sigils`). The rule is asked of
  `RegionKind.Camp`, not of a region name.
- `Core/Combat/SigilLoadout.cs` — `ShieldCapacity`, snapshotted when the shield
  forms, so the HUD's "remaining of" is the strength the shield actually has.
- `scripts/GameRoot.cs` — every new run takes up its basic Sigil in the camp
  (`StartingSigil`, default المشكاة / `lantern`) before the first hostile exists;
  the ready line now names it.
- `assets/fonts/` — Noto Sans (Latin) and Noto Sans Arabic, composed by a
  `FontVariation` and selected project-wide by `gui/theme/custom_font`, with
  their OFL licence and provenance. Measured before vendoring: Godot's built-in
  font has **no Arabic glyphs** (`has_char('م') == false`), and the joined form
  of `المشكاة` measures 75 px where the same letters unjoined measure 137 px -
  so the shaping itself is exercised, not just the codepoints.
- `Tests/Godot/GodotSmoke.cs` — 8 new checks: the font covers Arabic and Latin,
  all five Sigils have their lines, every line fits the HUD's card at its drawing
  size, the swap is refused away from the camp, it is taken up in the camp, the
  carried Price line reaches the HUD, the Silence Sigil can be taken up, and
  Silence marks the loudest ability on its HUD button.

`SigilTests` gained the Hearth rule: refused in the wilds, allowed in the camp.

### The APK really ships the font

The fresh APK was inspected with the SDK's tools; the font is not merely in the
tree, it is in the package:

```
assets/.godot/imported/NotoSans-Regular.ttf-*.fontdata        296,548 B
assets/.godot/imported/NotoSansArabic-Regular.ttf-*.fontdata  127,702 B
assets/.godot/exported/.../ghasaq-ui-font.res                     672 B
assets/project.binary names res://assets/fonts/ghasaq-ui-font.tres
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
sha256  6bae37540be825183b775d85a1dee2b5e2a415b692c72b8651b245279f4702f8
```

**Not measured:** the card has never been *rendered*. This environment has no
display and headless Godot has no rasteriser, so the drawing path is exercised
only by compilation, by the smoke test's font and fit measurements, and by the
APK shipping the font. Whether the surface reads well at 1920x1080, whether the
Arabic shaping *looks* right, and the whole feel gate of
[Sigils.md](Sigils.md) still need a screen and a player.

The workflows ran on GitHub for this change, on `f23f1e7`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #8 | **success** — naming gate, purity, the 601 tests, layout, build, smoke (24 checks) |
| `android.yml` | Android ARM64 #8 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

## The five الوَسْم / Sigils in the Core (2026-10-04)

[Documentation/Sigils.md](Sigils.md) is no longer a design in prose only: all
five verbs and all five Prices are code, and each one is pinned by a test.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **600 passed, 0 failed** (321 ms; 567 before this change) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 16/16 checks** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,550,360 bytes (98 MB) |

The fresh APK was inspected with the Android SDK's own tools, as before:
`package=com.ghasaq.thelastnight`, `targetSdkVersion=35`, launcher label
`غَسَق: الليلة الأخيرة`, `native-code: arm64-v8a`, with both
`assets/.godot/mono/publish/arm64/Ghasaq.dll` and `Ghasaq.Core.dll` inside, signed
by Godot's certificate.

New in code:

- `Core/Combat/Sigil.cs` — the id, the definition (name, verb line, Price line),
  the awareness probe and every draft number in `SigilTuning`.
- `Core/Combat/SigilLoadout.cs` — the carried Sigil's runtime: the blink's
  i-frames, the acquisition request, the overkill bite, the heal and the famine
  clock, the shield, the exposure and the shatter request.
- The hooks: `Vitals.GrantInvulnerability` (a window every damage path
  respects), `StatusKind.Starving` (the famine, deliberately not a Bleeding),
  `StatusEffectSystem.Apply` returning the live effect plus `Contains`/`Remove`,
  `EnemyBrain.ForceAcquire` (holds a target through cover for its window),
  `Combatant.ReceiveDamage` reporting overkill and letting the shield take its
  share, `AbilityFailure.Locked` and `LoudestAbilityIndex` on the controller,
  and `EncounterSimulation` advancing Sigils and finishing their world requests.
- Carrying: `GameSession.TryEquipSigil` refuses mid-fight, and the carried id
  round-trips through a save. `ContentValidator` now fails a Sigil with no Price
  line, with fault-injection tests proving the rule fires.

Both workflows ran on GitHub for this change, on the Sigil commit (`cae645c`):

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #6 | **success** — naming gate, purity, the core tests, layout, build, smoke |
| `android.yml` | Android ARM64 #6 | **success** — the ARM64 APK exported on a GitHub runner |

**Not measured:** the feel gate of [Sigils.md](Sigils.md) — whether a player can
say what their Sigil does and what it costs within one fight — is a designer and
playtest question. The HUD now draws the Price line (see the section above), but
none of these five has been played on a device. The numbers are still draft:
what is verified is the behaviour, not that the tuning is fun.

## After the naming change (2026-10-04)

The product, its packages and its assemblies were renamed to غَسَق / GHASAQ and
the repository was swept for the retired words of the earlier working title
([Naming.md](Naming.md) is the contract). Everything below was re-executed
**after** that change, in this environment, with Godot 4.5-stable (.NET)
downloaded for the run:

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** — no retired word remains |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **563 passed, 0 failed** (327 ms) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors → `Ghasaq.dll` |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 16/16 checks** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,542,168 bytes (98 MB) |

The fresh APK was then inspected with the Android SDK's own tools (`aapt`,
`unzip`, `apksigner`):

```
package: name='com.ghasaq.thelastnight' versionCode='1' versionName='1.0.0'
sdkVersion:'24'  targetSdkVersion:'35'
application-label:'غَسَق: الليلة الأخيرة'
native-code: 'arm64-v8a'
assets/.godot/mono/publish/arm64/Ghasaq.dll
assets/.godot/mono/publish/arm64/Ghasaq.Core.dll
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
```

Still not verified, because they need hardware: installing the APK on a physical
device, touch controls, frame rate, and the camera on real devices.

Both workflows have **now run on GitHub**, on the rename commit `f6832c2`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #2 | **success** — naming gate, core purity, 563 tests, layout, build, smoke test |
| `android.yml` | Android ARM64 #2 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

The table below records the runs made **before** the renaming — including the
pre-rename proof that the core and the Godot layer were healthy; its command
names have been updated to the current identity.

## Slice budgets and camera (days 15–45), 2026-10-04

Two verifiable pieces of the slice have landed: the combat budgets from plan §7
are now code the build enforces, and the decided 3/4 camera has its first-draft
defaults.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core test suite (with the new budget tests) | `bash Tools/test-core.sh` | **567 passed, 0 failed** (365 ms) |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', ...` |

New in code:

- `Core/Combat/CombatTuning.cs` — the plan's numbers in one place: telegraph
  floor 0.40 s, hit-stop band 40–80 ms, three hostiles on screen.
- `ContentValidator` now fails the build when a damaging enemy move is faster
  than the telegraph floor; the fault-injection test proves the rule fires. The
  shipped Cinder Maul was authored at 0.38 s and was raised to 0.40 s.
- Camera defaults for [CameraDecision.md](CameraDecision.md): pitch 50° (range
  20–60°), distance 11 m.

**Not measured:** the camera's readability and feel, the hit-stop animation
itself (the band is recorded in code; the view work is not done), and the 60 fps
budget on the target device. Those need eyes, hands and hardware.

## What has been run, and passed

All of the following were run in the environment this port was written in, which
has Godot 4.5 (.NET) but no Unity and no Unreal Engine.

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** — the core is engine-free |
| Core compiles | `bash Tools/test-core.sh` | **Pass** — portable library, 0 warnings |
| Core test suite | `bash Tools/test-core.sh` | **563 passed, 0 failed** |
| Godot project layout is complete and engine-clean | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Godot imports the project | `godot --headless --path . --import` | **Pass** |
| Headless smoke test inside Godot | `godot --headless --path . res://Tests/Godot/GodotSmoke.tscn` | **Pass — 15/15 checks** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — 300 physics frames, no errors |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 98 MB validated APK |

### The headless smoke test

`Tests/Godot/GodotSmoke.cs` runs inside the engine and exercises the core through it:

```
ok - DeterministicRng(42) uint #1 / #2 / #3      (492690617, 1919685028, 3561993920)
ok - DeterministicRng(42) first float             (0.1147134)
ok - StableHash("a")                              (af63dc4c8601ec8c)
ok - world graph has five regions
ok - wilds starts with five hostiles
ok - a fight actually resolved (hostiles fell)
ok - the opening fight reached a conclusion
ok - the Sigilbearer took damage
ok - defeats granted experience
ok - save serialises to JSON / deserialises
ok - experience / inventory / position survive a save round-trip
```

It exits non-zero if any check fails, so CI classifies on it. The parity checks
pin the exact RNG values, so a future change that silently alters how a fight plays
out fails the build.

### The main scene

Running `scenes/Main.tscn` headless builds the whole game and logs:

```
Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1
```

That is the assembled session: arena, player view, five enemy views, the HUD bound
to the session, the menu wired to the HUD's menu button, and the opening quest
offered.

## The Android APK

`Tools/build-android.sh` produced a real APK and validated it. Observed with
`aapt`, `unzip` and `apksigner`:

```
package: name='com.ghasaq.thelastnight' versionName='1.0.0'
sdkVersion:'24'  targetSdkVersion:'35'
android:screenOrientation = 0                 # landscape
lib/arm64-v8a/libgodot_android.so             # + 25 more arm64 libraries
assets/.godot/mono/publish/arm64/Ghasaq.dll
assets/.godot/mono/publish/arm64/Ghasaq.Core.dll
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
```

The APK is `build/android/ghasaq.apk` (98 MB) and is not committed (it is
gitignored).

### What the APK proves, and what it does not

Proven: the project configures, imports, compiles, publishes the .NET assembly, and
packages a signed, ARM64, landscape APK whose package name and target SDK match the
project's earlier targets.

Not proven: the APK has **not been installed on a physical device**. Touch controls,
frame rate and the camera on real hardware are untested. That requires a phone.

## What has NOT been run

| Not verified | Why | What it would take |
| --- | --- | --- |
| **Installing and playing the APK on a device** | Requires hardware. | Install the APK on a phone. |
| **Performance on target hardware** | Requires hardware. | Profile on a mid-range phone. |
| **A release-signed APK** | No release keystore exists in the repository. | Add one and set `GHASAQ_EXPORT_MODE=release`. |

The workflows' steps were validated locally first, and then on GitHub itself:
`ci.yml` and `android.yml` both succeeded on `f6832c2` (2026-10-04).

## Migration history (all engine migrations recorded honestly)

| Era | Engine layer | Core | Status |
| --- | --- | --- | --- |
| Original | Unity 6 (`Assets/`, `Packages/`, `ProjectSettings/`) | C# in `Core/` | Removed |
| Second | Unreal Engine 5 (`Ghasaq.uproject`, `Source/`, `Config/`) | C# in `Core/`; an incomplete C++ port | Removed. **Never compiled or run** — no Unreal Engine was available. |
| Current | Godot 4.5 (`project.godot`, `scripts/`, `scenes/`) | C# in `Core/` | Compiles, runs headless, and exports a validated APK |

The Unreal layer was removed without ever having been compiled; the Godot layer has
been. The engine-free core (and its 567 tests) carried through all three engines
unchanged, which is the entire point of the boundary.

## Reproducing the verification

```bash
# The engine-free core: purity gate, then the test suite.
bash Tools/test-core.sh

# The Godot project: layout, C# build, import, headless smoke test.
bash Tools/check-godot-project.sh
bash Tools/test-godot.sh /path/to/godot

# The Android ARM64 APK. Needs Godot (.NET) and the Android SDK.
ANDROID_HOME=/path/to/android-sdk JAVA_HOME=/path/to/jdk \
  GODOT_BIN=/path/to/godot bash Tools/build-android.sh
```
