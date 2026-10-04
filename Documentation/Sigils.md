# الوَسْم والثمن — Sigils and Prices (the five for the slice)

**Status:** draft for the combat-designer signature (plan §13, days 1–14).
Numbers marked *draft* are placeholders to be tuned in the slice; the plan moves
them to Remote Config in a later phase. Terms: [Naming.md](Naming.md).

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

**In the Core today:** the dash shape exists (`AbilityKind.Dash`,
`DashDistance`); i-frames and the "acquired" flag are new hooks for the slice.

*في سطر: ورقة هروب تكشفك.*

### 2. الرماد — Ash (`ash`)

**The new verb:** enemies you kill **burst** — a small area explosion at the
corpse, *draft* 2.5 m, 60% of your AttackPower.

**The Price:** you take a share of the **overkill** — damage beyond the target's
remaining health comes back at you at *draft* 25%.

**In the Core today:** `Combatant.Died` exists (the hook); the damage pipeline
would report the overkill amount so the burst and the bite can be computed.

*في سطر: تقتل فيتفتّت، والزائد يعضّك.*

### 3. الصمت — Silence (`silence`)

**The new verb:** a **silent execution** from behind — a heavy blow on an unaware
target *draft* ×2.5, instant if it is already below *draft* ~20% health.

**The Price:** your loudest ability is **locked** while the Sigil is equipped —
the one with the biggest wind-up — and the lock is visible on its HUD button.

**In the Core today:** behind-checks and awareness live in the AI and attack
resolution; the lockout is an `AbilityController` rule keyed on an ability tag.

*في سطر: تقتل من الخلف، ويُقفل ضربتك العالية.*

### 4. الجوع — Hunger (`hunger`)

**The new verb:** every landed hit **heals you** for *draft* ~8% of the damage.

**The Price:** after **8 seconds without landing a hit**, the hunger starts — a
Ghasaq damage-over-time on you that only stops when a hit lands.

**In the Core today:** the damage-over-time shape exists (`StatusKind.Bleeding`
carrying a Ghasaq school); the heal-on-hit and the no-hit timer are new.

*في سطر: كل ضربة تطعمك، والجفاف يأكلك.*

### 5. الزجاج — Glass (`glass`)

**The new verb:** a **shield that shatters into blades** — while held it absorbs
a share of incoming damage; on break it bursts outward, *draft* 2.5 m.

**The Price:** after the shatter you are **exposed for 2 s** — damage taken
increased (the `Marked` shape) and the shield is gone.

**In the Core today:** `StatusKind.Warded` (absorb) and `StatusKind.Marked`
(taking more damage) both exist; the outward burst is an ability.

*في سطر: حصانة تنكسر فتصير مكشوفًا.*

## How the five map onto the slice (days 15–45)

| Piece | Existing in Core | New in the slice |
| --- | --- | --- |
| Carrying and swapping | — | one equipped-Sigil slot; swap at the Hearth |
| The verbs | `AbilityDefinition` (Melee / Cleave / Bolt / Burst / Dash / Self) | one ability definition per Sigil |
| The Prices | `StatId` modifiers, `StatusKind`, ability lockout | a Price surface on the HUD plus the specific hooks above |
| Tuning | — | draft numbers here → Remote Config (later phase) |

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
