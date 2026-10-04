# Camera decision — 3/4 tilted top-down

**Status:** decided in this document; implemented as **first-draft defaults**
(2026-10-04: `scripts/PlayerInputReader.cs` starts at pitch 50° with a 20–60°
range, `scripts/CameraRig.cs` follows at 11 m). The feel and readability gates
below remain **unmeasured** — they need eyes and a device. **Awaiting the
combat-designer signature** (plan §13, days 1–14).

## The decision

The game's default camera is a **3/4 tilted top-down view**: a perspective camera
above and behind the Sigilbearer at a fixed default yaw, pitched down into a
45–55° band, framed so the player and **three hostiles** fit on screen at once
(plan §7).

Rules that come with it:

- **Left is left.** Stick left / `A` moves the character left on screen. The
  camera never inverts that mapping, and a manual turn never flips it mid-input
  (plan §7).
- **The camera never rotates by itself.** Turning the view is an explicit action
  (Q/E on keyboard, drag on touch) and stays optional; the default yaw is chosen
  so the fight reads without turning.
- **Framing shows the fight, not the scenery.** Projection and zoom changes are
  allowed only as feedback (a small zoom on a heavy blow, hit-stop shake), never
  to hide gameplay state.
- **One decision, one place.** The framing numbers live in the camera rig; the
  core neither knows nor cares where the camera stands.

## Why 3/4 tilted and not strict isometric

- The slice's hardest readability problem is a **≥400 ms telegraph on a large
  boss (2.2× scale)** while two other hostiles move around it (plan §7). A tilted
  perspective keeps vertical separation between bodies; a flattened projection
  stacks them into one silhouette.
- The plan's feedback budget ("juice": hit-stop, shake, a spark on the hit) is
  cheap and legible in a perspective view; the same feedback costs a re-tune of
  every authored frame in a strict isometric one.
- The control rule above is a one-to-one match with a fixed-yaw view. An
  orthographic diamond plus camera-relative movement makes eight-way input read
  diagonally — the exact failure the plan bans ("left on screen = left of the
  world").

Strict isometric is not forbidden forever: if the slice's art pass shows the
readability budget below is met more cheaply with it, this decision is reopened
**at the gate**, against the same criteria. (Plan §13 allows either.)

## Why third-person is deferred, not chosen

The plan allows a third-person view only if the slice **proves 60 fps on the
named mid-range device** (plan §6, §13). No device has been named yet and no
measurement exists, so third-person cannot be the default today. If it is ever
reopened, it re-enters through the same gate: measured frame times, not
screenshots.

## The frames that matter (readability budget)

| Constraint | Source | Measured how |
| --- | --- | --- |
| Player + 3 hostiles visible without turning | plan §7 | in the slice scene |
| Every lethal wind-up readable (≥400 ms) at the default framing | plan §7 | playtest review |
| Lethal telegraphs not hidden by the HUD thumb zones | plan §7 | touch-layout review |
| 60 fps on the target mid-range device, combat scene | plan §6, §13 | frame-time capture on the device named in the technical doc |

## Implementation notes (for days 15–45)

`scripts/CameraRig.cs` today is a third-person follow rig (default distance 6.5 m,
default pitch 12°, manual yaw driven by the input reader, shake, occlusion
push-in). The slice raises the default framing into the 3/4 band and keeps:

- the input-driven yaw/pitch (one source of truth for what "forward" means),
- the occlusion push-in,
- `Shake(...)` for hit feedback.

Nothing here touches the core: the view layer copies core state and never writes
back.

## What would falsify this decision

- Measured frame times below 60 fps in the combat scene on the named device.
- Playtesters failing to read a ≥400 ms telegraph at the default framing.
- Playtesters turning the camera to understand left from right (the mapping
  test).

## Sign-off (the day 1–14 gate)

The plan's completion line for this deliverable is "the combat designer signs the
feel". Signing here means:

- [ ] the framing above matches the combat intent,
- [ ] the five Sigils in [Sigils.md](Sigils.md) are readable and their Prices are
      felt,
- [ ] the measurable criteria above are accepted as the gate for days 15–45.

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Combat designer | | | |
