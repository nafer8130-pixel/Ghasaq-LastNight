# إعدادات الوصولية — Accessibility settings

**Status:** built (2026-10-04) in the Godot layer. The four settings of plan §6
(phase B) — reduced camera shake, the Dimming's distortion switch, a text size
and colour-blind cues — live on one menu page. Every one of them is a
**presentation preference, never a rule**: the settings are read by
`scripts/GameRoot.cs` and pushed onto the camera, the HUD, the menu and the
enemy telegraph. The core never reads a line of them, so no fight can be made
easier or harder from a settings row.

## The page

- `إعدادات الوصولية — ACCESSIBILITY` is the second row of the main menu (after
  RESUME), on purpose: the row pool is capped at seventeen, and a full bag
  spends most of it above the run's pages. A settings row the menu can drop is
  one the players who need it could not open. The smoke test opens it with the
  bag full and the pool at its cap.
- It opens **any time** — in any region, in or out of combat — unlike the
  Hearth's forge: an accessibility setting that demanded the camp would be one
  that is unreachable exactly when a fight is hard to read. The one screen that
  does not offer it is the defeat screen, which holds a single row by design
  (its rule is "one screen and nothing else"); a fallen player reaches the
  page again at the Hearth. Nothing can be changed about a fight that is over.
- Each press applies and saves at once (`GameRoot.CommitSettings` →
  `SettingsStore.Save`), so a choice survives the next launch even if the app
  is killed from inside a fight.

## The four settings

| Setting | Default | What it changes |
| --- | --- | --- |
| تقليل اهتزاز الكاميرا — REDUCE CAMERA SHAKE | OFF | Multiplies camera shake impulses by `0.2`. Hit-stop (the frame hold on a landed blow) is untouched: a held frame is not motion. |
| تشويش العَتْمة — DIMMING DISTORTION | ON | The Dimming's edge vignette (see below). The bar and the word عَتْمة carry the state either way, so switching it off loses no information. |
| حجم الخط — TEXT SIZE | 100% | Steps through 100% / 125% / 150%. Scales the HUD's drawn text and the blocks around it (bars, the Sigil card, the objective line) and the menu's own text. Hit targets do not scale: a thumb's target is not a text size. |
| عمى الألوان — COLOUR-BLIND CUES | OFF | Swaps the HUD's state colours for the colour-blind-safe (Okabe-Ito) palette, and adds a shape to the states a hue still carries (see below). |

Chosen shapes, not just toggles:

- **Reduce, not remove.** The shake setting keeps a fifth of the impulse rather
  than none, so the "a blow landed on you" feedback survives in the smallest
  form the plan's juice budget allows. Hit-stop keeps the weight.
- **The distortion is built with its off-switch, not before it.** The plan's
  "light visual distortion" (Soot.md) is the Dimming's edge vignette: an alpha
  band that pulses with the frame and deepens with the meter
  (`Hud.DimmingVignetteAlpha`, zero below the threshold or when switched off).
  The text of the *option* defaults to ON because the effect is part of how the
  state reads; the ones who need it off can reach it in two taps.
- **The text steps scale the layout with the words.** Bars, the Sigil card and
  its line advance grow with the font, so nothing overlaps itself; the smoke
  test measures every card line and every menu page at every step.
- **A palette is not a cue by itself.** In colour-blind mode a critical damage
  number gets the suffix `!` in addition to its colour: the state is never
  carried by a hue alone. The enemy wind-up telegraph switches to the safe
  palette too, because that warning is the most important colour in the fight.

## Persistence

`SettingsStore` writes `user://settings.cfg` (a Godot `ConfigFile`, writable on
Android) with one `[accessibility]` section. A missing or unreadable file loads
the defaults rather than failing; a stored text-size index is clamped back into
range on load. The file is **per device, not per save slot**: an accessibility
preference belongs to the person holding the phone.

## What this pass does not cover

- **Nothing here has been seen on a device.** The vignette, the palette and the
  text steps are exercised as arithmetic and through the live menu in the
  headless smoke test; a headless run cannot observe a pixel. Whether the
  vignette reads as intended on a phone at night is a device question.
- No text-to-speech, no dialogue/subtitle options, no control remapping beyond
  the existing touch layout — none of those surfaces exist yet to make
  accessible.
- The colour-blind palette covers the HUD's state colours and the telegraph; it
  does not repaint the world or the enemy body tints themselves.
