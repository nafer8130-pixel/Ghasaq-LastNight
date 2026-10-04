# الوَسْم والثمن — Sigils and Prices (the five for the slice)

**Status:** draft for the combat-designer signature (plan §13, days 1–14).
Numbers marked *draft* are placeholders to be tuned in the slice; the plan moves
them to Remote Config in a later phase. Terms: [Naming.md](Naming.md).

**Implemented in the Core (2026-10-04).** Every rule described below is now
code: the definitions and lines live in `GameContent.BuildSigils()`, the numbers
in `SigilTuning`, the runtime state in `SigilLoadout`, and the hooks in
`AttackResolver`, `Combatant`, `AbilityController`, `EnemyBrain` and
`EncounterSimulation`. The numbers remain draft: what is signed off is the
behaviour, not the tuning. `SigilTests` pins each verb and each Price, and
content validation refuses a Sigil that has no Price line to show.

## The contract (plan §3.1–3.2)

- The player carries **one** الوَسْم / Sigil per run, swapped at the الموقد /
  Hearth, out of combat.
- A Sigil **changes one combat action radically**. It is a combat style, not a
  character pull: no gacha, no pulls, no levels to buy.
- Every Sigil has a **الثمن / Price**: a visible mechanical cost, **shown on the
  HUD at all times**. No hidden costs; a text-only curse does not count.
- If a new player cannot say what their Sigil does and what it costs within one
  fight, the Sigil is mis-designed — that is a design bug, not a player error.

## The five

### 1. المشكاة — Lantern (`lantern`)

**The new verb:** a short blink-step — instant reposition, *draft* 5.5 m, with a
short i-frame window *draft* ~0.2 s.

**The Price:** your light marks you. After each blink, every hostile within
*draft* ~15 m acquires you for *draft* ~2 s, even around cover.

**In the Core now:** the kit's dash *is* the blink. Committing to a
`AbilityKind.Dash` opens the i-frame window (`AbilityController` calls
`SigilLoadout.NotifyDashStarted`, backed by the generic
`Vitals.GrantInvulnerability`), and landing it charges the Price: the request is
queued and the encounter hands every hostile within the radius a forced
acquisition (`EnemyBrain.ForceAcquire`), which holds through cover for the whole
window. The i-frame window refuses blows and lingering damage alike; a blink
that dodges does not spend the Glass shield.

*في سطر: ورقة هروب تكشفك.*

### 2. الرماد — Ash (`ash`)

**The new verb:** enemies you kill **burst** — a small area explosion at the
corpse, *draft* 2.5 m, 60% of your AttackPower.

**The Price:** you take a share of the **overkill** — damage beyond the target's
remaining health comes back at you at *draft* 25%.

**In the Core now:** `Combatant.ReceiveDamage` reports the overkill, measured
after other defences have taken their share. The strike charges the bite
immediately (`SigilLoadout.NotifyKill`), and the burst is resolved in
`AttackResolver.Resolve` - the one place that holds the target list - as a plain
radial hit that cannot crit, cannot apply on-hit effects, and deliberately
cannot burst again, so one swing can never become a chain reaction.

*في سطر: تقتل فيتفتّت، والزائد يعضّك.*

### 3. الصمت — Silence (`silence`)

**The new verb:** a **silent execution** from behind — a heavy blow on an unaware
target *draft* ×2.5, instant if it is already below *draft* ~20% health.

**The Price:** your loudest ability is **locked** while the Sigil is equipped —
the one with the biggest wind-up — and the lock is visible on its HUD button.

**In the Core now:** "from behind" is `AttackResolver.IsBehind` (the target's
rear half-plane), and "unaware" comes from the brain through the
`IAwarenessProbe` the encounter implements - only an enemy that has not noticed
the fight can be executed. Below the threshold the blow bypasses armour,
resistance and crit; above it, it merely lands multiplied. The Price is a lock
on the longest wind-up: `AbilityController` computes `LoudestAbilityIndex`,
refuses it with `AbilityFailure.Locked`, and exposes `LockedAbilityIndex` so the
HUD button can be marked. In the shipped kit the sealed ability is `sunder`.

*في سطر: تقتل من الخلف، ويُقفل ضربتك العالية.*

### 4. الجوع — Hunger (`hunger`)

**The new verb:** every landed hit **heals you** for *draft* ~8% of the damage.

**The Price:** after **8 seconds without landing a hit**, the hunger starts — a
Ghasaq damage-over-time on you that only stops when a hit lands.

**In the Core now:** any landed hit of the bearer's feeds (`NotifyHitLanded`,
8% of the damage actually applied). Eight seconds without one starts the
famine, a `StatusKind.Starving` damage-over-time carrying the Ghasaq school at
*draft* 2% of maximum health per second. It gets its own status kind rather than
borrowing Bleeding: a Price is not a wound, and merging the two would let an
enemy's bleed end the famine or hide it on the HUD. The famine ends the moment a
hit lands, through an explicit `StatusEffectSystem.Remove` of the instance the
loadout owns - duration never decides when a Price stops.

*في سطر: كل ضربة تطعمك، والجفاف يأكلك.*

### 5. الزجاج — Glass (`glass`)

**The new verb:** a **shield that shatters into blades** — while held it absorbs
a share of incoming damage; on break it bursts outward, *draft* 2.5 m.

**The Price:** after the shatter you are **exposed for 2 s** — damage taken
increased (the `Marked` shape) and the shield is gone.

**In the Core now:** the shield forms when the Sigil is taken up, holds *draft*
35% of the bearer's maximum health, and takes *draft* half of each incoming blow
until it runs out. Breaking it applies the exposure (the `Marked` shape, +25%
damage taken for 2 s), queues the shatter request, and starts the *draft* 12 s
reform - the verb must come back inside a fight. The shatter itself is finished
by the encounter: a 2.5 m radial burst at *draft* 75% of AttackPower, through
the hostiles' own armour. The shield guards against blows, not against damage
that arrives from inside, such as the Hunger famine.

*في سطر: حصانة تنكسر فتصير مكشوفًا.*

## How the five map onto the slice (days 15–45)

| Piece | Existing in Core | New in the slice |
| --- | --- | --- |
| Carrying and swapping | — | one equipped-Sigil slot (`GameSession.TryEquipSigil`, refused mid-fight); saved and restored by id; swap at the Hearth |
| The verbs | `AbilityDefinition` (Melee / Cleave / Bolt / Burst / Dash / Self) | each Sigil is a rule on the shared kit, not a new button: the blink is the dash, the execution is a strike, the shield is how damage arrives. Per-Sigil ability definitions remain for the HUD pass |
| The Prices | `StatId` modifiers, `StatusKind`, ability lockout | implemented as the specific hooks above, each pinned by a test; the HUD surface (a Price line on screen at all times) is the remaining slice work |
| Tuning | — | draft numbers in `SigilTuning` → Remote Config (later phase) |

Every Price must remain readable in the HUD at all times (plan §3.2): the slice
is not complete if a Price is only discoverable by reading this document.

## The feel gate (sign-off)

Each Sigil passes when **a new player, within one fight**:

- [ ] can say what their Sigil's new verb does,
- [ ] can say what it costs them,
- [ ] loses to, or wins by, the Price at least once meaningfully in the first ten
      minutes.

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Combat designer | | | |
