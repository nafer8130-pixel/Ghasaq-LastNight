# الأثر والموقد — The Reliquary and the Hearth (dismantle or forge)

**Status:** draft for the economy-designer signature (plan §13). Numbers marked
*draft* are placeholders to be tuned in the economy slice; the plan moves them to
Remote Config in a later phase. Terms: [Naming.md](Naming.md).

**Implemented in the Core (2026-10-04).** Both halves of §3.3 are code: gear
held in the bag breaks down into السُّخام / Soot at the Hearth, the forge spends
that Soot on levels, and the bank and the ledger ride every save. `SalvageTests`,
`ForgeTests` and `SootBankTests` pin the yields, the costs, the refusals and the
saves; the smoke test drives both hammers through the menu's own buttons. The
numbers remain draft: what is signed off is the behaviour, not the tuning.

## The contract (plan §3.3, §3.7)

- الأثر / the Reliquary is **gear loot**, not spirit-bound loot: what a creature
  drops is an item anyone can carry, and the loop is the classic one —
  **kill → pick up → compare → dismantle, forge or wear**.
- Both hammers stand at الموقد / the Hearth: a camp region with no hostiles
  standing, exactly where the Sigil is swapped. Making them a place keeps the
  Hearth a hub and keeps a mid-fight accident from eating an upgrade.
- السُّخام / Soot arrives in **two separate stores**. The run meter (plan §3.6,
  `Core/Combat/Soot.cs`) prices a single fight and is never saved. This bank —
  the upgrade currency the plan names in the same section — accumulates between
  runs, is spent by the forge, and is written into every save. They are
  deliberately not one number: a fight's residue can be tuned without moving the
  character's savings.
- **Nothing here can lose an item.** Every rule is checked before anything
  moves; a refused dismantle or forging changes nothing at all, and the bank is
  charged all or nothing, so a purchase can never take the Soot and leave the
  level un-bought.

## The rules in the Core

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

`GameSession.TryForge(itemId, out failure, out cost)`:

| Condition | Outcome |
| --- | --- |
| Unknown id | `UnknownItem` |
| Bound | `Bound` |
| Not equipment | `NotForgeable` |
| Neither carried nor worn | `NotOwned` |
| Hostiles standing | `InCombat` |
| Not at the Hearth's camp | `NotAtHearth` |
| Already at `MaxLevel` | `MaxLevel` |
| The bank cannot pay the next level | `InsufficientSoot` (the price is reported, so the refusal can name it) |
| Owned, gear, at the Hearth, affordable | `SootBank.TrySpend(cost)`, the level rises, a **worn** piece's stats are re-applied on the spot |

One piece per call, because gear is `MaxStack = 1`. The menu lists each owned
piece with the level it stands at, the bonus the next one adds and its price —
and each dismantlable piece with the Soot its hammer would pay — so "compare"
ends in visible numbers, not a guess.

## The numbers (draft)

Dismantling, by rarity:

| Rarity | Soot per piece | Where it appears today |
| --- | --- | --- |
| Common | 2 | Sigilbearer's Blade (starting kit) |
| Uncommon | 6 | — |
| Rare | 15 | Ashen Plate, Ember Relic |
| Eclipse | 40 | Ghasaq Edge |
| Mythic | 80 | — (the one Mythic, the Sentinel's Core, is bound and refused) |

Forging, by level (cost to reach the next) and by kind (the flat bonus each
level adds):

| Level | 0→1 | 1→2 | 2→3 | 3→4 | 4→5 |
| --- | --- | --- | --- | --- | --- |
| Cost in Soot | 12 | 24 | 40 | 60 | 90 |

| Kind | Bonus per level |
| --- | --- |
| Weapon | +6 AttackPower |
| Armor | +8 Armor |
| Relic | +5 GhasaqPower |

Chosen shapes, not just numbers:

- **Yields keyed by rarity, never authored per item.** Every piece of gear the
  content ships is dismantlable the day it is authored, and a new item cannot
  silently yield nothing.
- **Forge levels are additive by kind, not percentages of the piece's own
  lines.** The Ashen Plate's move penalty is part of its identity, and scaling
  the whole array would quietly deepen a Trade-off as a reward. The bonus lands
  on one stat chosen by kind, so "what does a level do" never needs a table.
- **Prices climb, yields do not.** Twelve Soot buys the first level off a single
  Common piece's brothers; the fifth costs more than two Eclipse dismantles.
  The bank is meant to be a decision, not a formality.
- **The bank saturates rather than wraps** (`SootBank.Deposit`): an overflow can
  never turn savings negative, and a hand-edited negative save clamps to zero.
  Levels clamp into `0..5` the same way.
- **The equipped copy is out of reach of the dismantler by construction.**
  Dismantling reads the bag, and an equipped item is not in the bag.
- **A destroyed piece forgets its levels** (`TrySalvage` clears the entry):
  nothing refunds the investment and a fresh drop of the same id starts at level
  zero, so the forge is a commitment, not a rent.
- **Levels are keyed by item id, which under this build IS the copy.** The
  inventory holds at most one copy of any id (one slot per type, gear at
  `MaxStack 1`), so a second copy of the same id can never exist. If the bag
  ever learns to hold two, `ItemForge` is the class that has to learn to tell
  them apart.

## Where it lives

| Piece | Where |
| --- | --- |
| Salvage yields and failure reasons | `Core/Items/Salvage.cs` → `SalvageTuning`, `SalvageFailure` |
| Forge costs, bonuses and failure reasons | `Core/Items/Forge.cs` → `ForgeTuning`, `ForgeFailure` |
| The ledger of levels | `Core/Items/Forge.cs` → `ItemForge` (`LevelOf`, `SetLevel`, `ToStacks`, `LoadFrom`) |
| The bank | `Core/Items/SootBank.cs` → `Balance`, `Deposit`, `TrySpend`, `LoadFrom` |
| The rules | `GameSession.TrySalvage`, `GameSession.TryForge` (validated before anything moves) |
| The worn bonus | `EquipmentLoadout` reads the ledger in `ApplyModifiers` (same token as the piece's lines, so one unequip removes both) and `RefreshModifiers` re-applies it when a worn piece is forged |
| The save | `SaveGame.SootBalance` (`soot`) and `SaveGame.Forge` (`forge`); the ledger loads **before** the loadout, so worn bonuses come back |
| The Hearth page | `scripts/GameMenu.cs` → `MenuPage.Hearth`: the bank note, forging rows (worn first) and dismantling rows, then the Sigil link |
| Measured loop | `Tests/Godot/GodotSmoke.cs` → `CheckHearthSalvage`, `CheckHearthForge` (the core rules) and `CheckHearthMenu` (both hammers through the menu's own buttons, after travelling to the camp the way the World page does) |

## What is deliberately not built yet

- **سوابق / prefixes** — rare affix rolls on drops. The loot generator is the
  right place for them, and faking them in the tables is the wrong one.
- **Comparison UI** — "compare" is still the player reading rows in the menu; a
  side-by-side view is a UX slice.

## The feel gate (sign-off)

The loop passes when **a new player**:

- [ ] can say where the hammers stand (the Hearth) and where they do not,
- [ ] dismantles a piece they might have used, and feels the trade,
- [ ] forges a piece they mean to keep, and can say what the level bought,
- [ ] sees the spent Soot and the raised level as one decision, not two screens.

| Role | Name | Date | Signature |
| --- | --- | --- | --- |
| Economy designer | | | |
