# السُّخام والعَتْمة — Soot and Dimming

**Status:** draft for the combat-designer signature (plan §13). Numbers marked
*draft* are placeholders to be tuned in the slice; the plan moves them to Remote
Config in a later phase. Terms: [Naming.md](Naming.md).

**Implemented in the Core (2026-10-04).** Every rule described below is now
code: the numbers in `SootTuning`, the runtime state in `SootMeter`, the
commitment hook in `AbilityController`, the two couplings to the damage
pipeline in `Combatant` and `AttackResolver`, and the meter on the HUD
(`Hud.DrawSootBar`). The numbers remain draft: what is signed off is the
behaviour, not the tuning. `SootTests` pins the meter, both edges, the
commitment rule and the refusal rule.

## The contract (plan §3.6)

- السُّخام / Soot is what the Ghasaq leaves behind in the body when it is used.
  In the full game it is also the upgrade currency; in this slice only the
  in-run state is built.
- Within a run, **using Ghasaq-powered abilities raises the meter**. It fades
  while the Ghasaq is left alone.
- Past a threshold, العَتْمة / Dimming begins: **stronger and frailer at once**
  - more damage dealt, more damage taken.
- **No corruption bar.** Nothing in the Soot ever takes the character away from
  the player: no self-damage, no death, no lost control. The player controls the
  risk; the meter only re-prices the fight.
- The plan's "light visual distortion" is deliberately **not built yet**: it is
  gated on accessibility settings ("can be turned off in accessibility
  settings", plan §3.6/§6), and this build has no settings screen. The bar and
  the عَتْمة mark carry the state until there is one; the distortion will be
  added together with its off-switch, not before.

## The numbers (draft)

| Value | Draft | Meaning |
| --- | --- | --- |
| `SootTuning.Max` | 100 | Full meter. Dimming is strongest here. |
| `SootTuning.PerGhasaqUse` | 20 | Soot added per Ghasaq-powered commitment (five burns to full). |
| `SootTuning.DecayPerSecond` | 2 | Fade when the Ghasaq is put down (50 s to empty from full, 20 s from the threshold). |
| `SootTuning.DimmingStart` | 60 | Where the Dimming begins (the third burn lands exactly on it). |
| `SootTuning.DimmingDamageDealtBonus` | +25% at full | The edge that makes the risk worth taking. |
| `SootTuning.DimmingDamageTakenBonus` | +35% at full | The Price side - deliberately larger than the gain. |

Chosen shapes, not just numbers:

- **Linear in both directions.** The meter climbs in fixed steps and fades at a
  fixed rate; a player who counts their burns can predict the state, which is
  the point of a visible meter.
- **The bonuses scale from the threshold, and are zero at it.** Crossing into
  the Dimming costs nothing by itself; the last stretch of the meter is the
  dangerous one. `DimmingFraction` is that depth, 0 at 60 and 1 at 100.
- **Commitment, not outcome.** The soot is charged inside
  `AbilityController.TryActivate` at the moment the stamina is spent
  (`ability.UsesGhasaqPower`). An interrupted wind-up and a missed blow keep
  their soot, the same rule the Lantern's i-frame window follows: the risk
  begins with the decision. A refused activation (no stamina, cooldown, locked)
  burns nothing and leaves nothing.
- **Never lethal, never persistent.** The meter owns no damage path at all. It
  is run state, not saved state: a new run starts clean, and `Revive` washes it
  off, so a retry begins at zero rather than dimmed by the attempt that failed.

## Where it lives

| Piece | Where |
| --- | --- |
| Numbers | `Core/Combat/Soot.cs` → `SootTuning` |
| Meter | `Core/Combat/Soot.cs` → `SootMeter` (`Soot`, `Fraction`, `IsDimming`, `DimmingFraction`, the two multipliers) |
| Charging | `AbilityController.TryActivate` (Ghasaq-keyed abilities only) |
| Fading | `Combatant.Tick` → `SootMeter.Tick`; reset in `Combatant.Revive` |
| Pricing blows | `Combatant.OutgoingDamageMultiplier` / `IncomingDamageMultiplier`, read by `AttackResolver` in both the single-target `Resolve` path and the `ApplyRadialBurst` path (the Ash and Glass bursts included, so a dimmed burst is heavier too) |
| Handing it to the run | `GameRoot.BuildSession`: `player.Soot = new SootMeter()` - the run state dies with the run, like the Sigil's, and is never part of a save |
| Readout | `Hud.DrawSootBar`: a third bar under the vitals, grey while clear, dimming toward red, with the word `عَتْمة` the moment the state begins |

## The feel gate (sign-off)

The Soot passes when **a new player, within one fight**:

- [ ] notices the meter move when they use the Ghasaq,
- [ ] can say which way is safe (let it fade) and which way is strong (burn on),
- [ ] steps into the Dimming at least once and feels both edges - the heavier
      blow and the harder hit taken.

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Combat designer | | | |
