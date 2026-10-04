# الأثر والموقد — The Reliquary and the Hearth (the dismantling loop)

**Status:** draft for the economy-designer signature (plan §13). Numbers marked
*draft* are placeholders to be tuned in the economy slice; the plan moves them to
Remote Config in a later phase. Terms: [Naming.md](Naming.md).

**Implemented in the Core (2026-10-04).** The first half of §3.3 is code: gear
held in the bag breaks down into السُّخام / Soot at the Hearth, and the bank is a
permanent balance that rides every save. `SalvageTests` and `SootBankTests` pin
the yields, the refusals and the save; the smoke test drives the loop through the
menu's own buttons. The numbers remain draft: what is signed off is the
behaviour, not the tuning.

## The contract (plan §3.3, §3.7)

- الأثر / the Reliquary is **gear loot**, not spirit-bound loot: what a creature
  drops is an item anyone can carry, and the loop is the classic one —
  **kill → pick up → compare → dismantle or wear**.
- التفكيك (dismantling) happens at الموقد / the Hearth: a camp region with no
  hostiles standing, exactly where the Sigil is swapped. Making it a place keeps
  the Hearth a hub and keeps a mid-fight accident from eating an upgrade.
- السُّخام / Soot arrives in **two separate stores**. The run meter (plan §3.6,
  `Core/Combat/Soot.cs`) prices a single fight and is never saved. This bank —
  the upgrade currency the plan names in the same section — accumulates between
  runs and is written into every save. They are deliberately not one number:
  a fight's residue can be tuned without moving the character's savings.
- **Nothing here can lose an item.** Every rule is checked before the piece
  moves; a refused dismantle changes nothing at all.

## The rule in the Core

`GameSession.TrySalvage(itemId, out failure, out soot)`:

| Condition | Outcome |
| --- | --- |
| Unknown id | `UnknownItem` |
| Bound (story items, e.g. the Sentinel's Core) | `Bound` |
| Not equipment (materials, consumables) | `NotSalvageable` |
| Not in the bag — including the **equipped** copy | `NotHeld` |
| Hostiles standing | `InCombat` |
| Not at the Hearth's camp | `NotAtHearth` |
| Held, gear, at the Hearth | the piece leaves the bag and `SootBank.Deposit(yield)` reports `None` |

One piece per call, because gear is `MaxStack = 1`. The menu lists each held
piece with the Soot its hammer would pay, so "compare" ends in a visible number,
not a guess.

## The numbers (draft)

| Rarity | Soot per piece | Where it appears today |
| --- | --- | --- |
| Common | 2 | Sigilbearer's Blade (starting kit) |
| Uncommon | 6 | — |
| Rare | 15 | Ashen Plate, Ember Relic |
| Eclipse | 40 | Ghasaq Edge |
| Mythic | 80 | — (the one Mythic, the Sentinel's Core, is bound and refused) |

Chosen shapes, not just numbers:

- **Keyed by rarity, not authored per item.** Every piece of gear the content
  ships is dismantlable the day it is authored, and a new item cannot silently
  yield nothing. Rebalancing a tier moves every item of it at once.
- **The yields climb faster than the tiers do.** The bank exists so a rare drop
  stays worth keeping even when it is not an upgrade — the heart of the genre
  loop.
- **The bank saturates rather than wraps** (`SootBank.Deposit`): an overflow can
  never turn savings negative, and a hand-edited negative save clamps to zero.
- **The equipped copy is out of reach by construction.** Dismantling reads the
  bag, and an equipped item is not in the bag: there is no code path that can
  take the piece off the character to pay for anything.

## Where it lives

| Piece | Where |
| --- | --- |
| Yields and failure reasons | `Core/Items/Salvage.cs` → `SalvageTuning`, `SalvageFailure` |
| The bank | `Core/Items/SootBank.cs` → `Balance`, `Deposit`, `LoadFrom` |
| The rule | `GameSession.TrySalvage` (validated before anything moves) |
| The save | `SaveGame.SootBalance` ← `CreateSave` / `ApplySave`; JSON key `soot` |
| The Hearth page | `scripts/GameMenu.cs` → `MenuPage.Hearth`: the bank note, one row per dismantlable piece with its yield, and the Sigil link |
| Measured loop | `Tests/Godot/GodotSmoke.cs` → `CheckHearthSalvage` (the core rule) and `CheckHearthMenu` (through the menu's own buttons, after travelling to the camp the way the World page does) |

## What is deliberately not built yet

- **سوابق / prefixes** — rare affix rolls on drops. The loot generator is the
  right place for them, and faking them in the tables is the wrong one.
- **الطرق / forging** — the sink that spends the bank. The currency is banked
  now and nothing spends it yet: this slice delivers the dismantling half of the
  loop so the values and the save shape are settled before an item-level system
  depends on them. A forge needs per-instance item state (an upgrade level on
  the copy, not on the shared definition), which is its own slice.
- **Comparison UI** — "compare" is still the player reading two rows in the
  menu; a side-by-side view is a UX slice.

## The feel gate (sign-off)

The loop passes when **a new player**:

- [ ] can say where dismantling happens (the Hearth) and where it does not,
- [ ] dismantles a piece they might have used, and feels the trade,
- [ ] can say what the banked number is for, even before the forge exists.

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Economy designer | | | |
