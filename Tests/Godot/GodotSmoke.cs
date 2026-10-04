using System;
using System.Collections.Generic;
using Godot;
using Ghasaq.Core.Combat;
using Ghasaq.Game;
using Ghasaq.Core.Content;
using Ghasaq.Core.Items;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.Simulation;

namespace Ghasaq.Tests
{
    /// <summary>
    /// A headless end-to-end check that runs inside Godot.
    ///
    /// It exercises the engine-free core through the engine: the deterministic
    /// RNG must keep its exact parity, a session must boot, a fight must actually
    /// resolve, a save must round-trip, the Sigil Price surface must be drawable
    /// - a font with the Arabic glyphs and five authored Price lines - the
    /// Hearth's salvage loop must run from the menu a player actually touches,
    /// a fallen bearer must rise back into the run, and the accessibility
    /// settings must reach every surface they claim to change.
    /// Run with:
    ///
    ///     godot --headless --path . res://Tests/Godot/GodotSmoke.tscn
    ///
    /// Exits 0 when every check passes and 1 otherwise, so CI can classify on it.
    /// </summary>
    public partial class GodotSmoke : Node
    {
        private int _failures;

        private sealed class AggressiveDriver : ICombatantDriver
        {
            public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
            {
                CombatIntent intent = CombatIntent.None();

                Combatant target = world.FindNearestHostile(self, 60f);
                if (target == null)
                {
                    intent.MoveDirection = new Float3(0f, 0f, 1f);
                    intent.SpeedScale = 1f;
                    return intent;
                }

                intent.MoveDirection = (target.Position - self.Position).FlattenedXZ;
                intent.SpeedScale = 1f;
                intent.ActivateAbility = true;
                intent.AbilityIndex = 0;
                return intent;
            }
        }

        public override void _Ready()
        {
            CheckRngParity();
            CheckSessionBootsAndFights();
            CheckSaveRoundTrip();
            CheckSigilSurface();
            CheckBattleFeedback();
            CheckSootSurface();
            CheckHearthSalvage();
            CheckHearthForge();
            CheckPrefixDrops();
            CheckHearthMenu();
            CheckDefeatLoop();
            CheckAccessibility();

            if (_failures == 0)
            {
                GD.Print("godot-smoke: OK (all checks passed)");
                GetTree().Quit(0);
            }
            else
            {
                GD.PrintErr($"godot-smoke: FAIL ({_failures} check(s) failed)");
                GetTree().Quit(1);
            }
        }

        private void Check(bool condition, string label)
        {
            if (condition)
            {
                GD.Print("  ok   - " + label);
                return;
            }

            _failures++;
            GD.PrintErr("  FAIL - " + label);
        }

        // ---------------------------------------------------------------- rng ---

        private void CheckRngParity()
        {
            var rng = new DeterministicRng(42);
            Check(rng.NextUInt() == 492690617u, "DeterministicRng(42) uint #1");
            Check(rng.NextUInt() == 1919685028u, "DeterministicRng(42) uint #2");
            Check(rng.NextUInt() == 3561993920u, "DeterministicRng(42) uint #3");

            var floatRng = new DeterministicRng(42);
            Check(Math.Abs(floatRng.NextFloat() - 0.1147134f) < 1e-5f, "DeterministicRng(42) first float");

            Check(DeterministicRng.StableHash("a") == 0xaf63dc4c8601ec8cUL, "StableHash(\"a\")");
        }

        // ------------------------------------------------------------ session ---

        private GameSession BuildSession()
        {
            var items = GameContent.BuildItems();
            Combatant player = GameContent.CreatePlayer();

            var session = new GameSession(
                player,
                items,
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(20250925),
                WorldBounds.Square(38f));

            GameContent.Populate(session);
            session.EnterRegion(GameContent.RegionWilds);
            session.Quests.TryStart(GameContent.QuestArrival);
            return session;
        }

        private void CheckSessionBootsAndFights()
        {
            GameSession session = BuildSession();
            Check(session.World.Count == 5, "world graph has five regions");

            session.Encounter.AddDriven(
                session.Player,
                GameContent.BuildPlayerAbilities(),
                new AggressiveDriver(),
                isPlayer: true);

            // The Grey Wilds' opening encounter, from the content's spawn plan.
            SpawnRegion(session, GameContent.RegionWilds);

            Check(session.Encounter.HostilesRemaining == 5, "wilds starts with five hostiles");

            float elapsed = 0f;
            while (elapsed < 90f && session.Encounter.HostilesRemaining > 0)
            {
                session.Update(1f / 60f);
                elapsed += 1f / 60f;
            }

            Check(session.Encounter.HostilesRemaining < 5, "a fight actually resolved (hostiles fell)");

            // The driver above is deliberately naive (it charges every hostile at
            // once), so the meaningful invariant is that the encounter reached a
            // conclusion, not that the Sigilbearer won.
            Check(!session.Player.IsAlive || session.Encounter.HostilesRemaining == 0,
                "the opening fight reached a conclusion");
            Check(session.Player.Vitals.Health < session.Player.Vitals.MaxHealth, "the Sigilbearer took damage");
            Check(session.Progression.TotalExperience > 0, "defeats granted experience");

            GD.Print($"  info - outcome: hostiles {session.Encounter.HostilesRemaining}, sigilbearer alive {session.Player.IsAlive}, xp {session.Progression.TotalExperience}");
        }

        private static void SpawnRegion(GameSession session, string regionId)
        {
            (string Archetype, float X, float Z, int Level)[] plan =
            {
                (GameContent.ArchetypeHollowWalker, 8f, 12f, 1),
                (GameContent.ArchetypeHollowWalker, -9f, 14f, 1),
                (GameContent.ArchetypeHollowWalker, 0f, 18f, 2),
                (GameContent.ArchetypeCinderHound, 14f, -6f, 3),
                (GameContent.ArchetypeCinderHound, -14f, -8f, 3)
            };

            for (int i = 0; i < plan.Length; i++)
            {
                EnemyArchetype archetype = GameContent.FindArchetype(plan[i].Archetype);
                if (archetype == null)
                {
                    continue;
                }

                var position = new Float3(plan[i].X, 0f, plan[i].Z);
                Combatant combatant = archetype.Create(regionId + "/" + plan[i].Archetype + "-" + i, position, plan[i].Level);
                session.Encounter.AddEnemy(combatant, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);
            }
        }

        // -------------------------------------------------------------- saves ---

        private void CheckSaveRoundTrip()
        {
            GameSession session = BuildSession();
            session.Player.SetPosition(new Float3(1f, 0f, 2f));
            session.GrantItem(GameContent.ItemAsh, 7);
            session.Progression.AddExperience(500);

            string json = SaveSerializer.Serialize(session.CreateSave());
            Check(!string.IsNullOrEmpty(json), "save serialises to JSON");

            Check(SaveSerializer.TryDeserialize(json, out SaveGame loaded, out string error),
                "save deserialises" + (error == null ? "" : " (" + error + ")"));

            GameSession restored = BuildSession();
            restored.ApplySave(loaded);

            Check(restored.Progression.TotalExperience == session.Progression.TotalExperience, "experience survives a save round-trip");
            Check(restored.Inventory.Count(GameContent.ItemAsh) == 7, "inventory survives a save round-trip");
            Check(restored.Player.Position.X == session.Player.Position.X, "position survives a save round-trip");
        }

        // ---------------------------------------------------------- the price ---

        /// <summary>
        /// The Price surface (plan section 3.2): every Price must be readable on
        /// the HUD at all times. Two things have to hold for that, and neither is
        /// visible to the engine-free tests: the UI font must actually carry the
        /// Arabic glyphs - Godot's built-in font has none - and the five Sigils
        /// must have their lines authored. The swap rule is checked here too,
        /// because it is what puts the lines on screen in the first place.
        /// </summary>
        private void CheckSigilSurface()
        {
            Font font = ThemeDB.FallbackFont;
            Check(font != null && font.HasChar(0x0645) && font.HasChar(0x0041),
                "the UI font covers Arabic and Latin (the Price lines can draw)");

            List<SigilDefinition> sigils = GameContent.BuildSigils();
            bool linesPresent = sigils.Count == 5;

            for (int i = 0; i < sigils.Count; i++)
            {
                if (string.IsNullOrEmpty(sigils[i].VerbLine) || string.IsNullOrEmpty(sigils[i].PriceLine))
                {
                    linesPresent = false;
                }
            }

            Check(linesPresent, "all five Sigils carry a verb line and a Price line for the HUD");

            // The card does not wrap: a line that does not fit runs off it. Measured
            // with the font and size the HUD actually draws with.
            bool linesFit = true;

            for (int i = 0; i < sigils.Count; i++)
            {
                if (font.GetStringSize("الفعل: " + sigils[i].VerbLine, HorizontalAlignment.Left, -1, Hud.SigilLineSize).X > Hud.SigilCardWidth ||
                    font.GetStringSize("الثمن: " + sigils[i].PriceLine, HorizontalAlignment.Left, -1, Hud.SigilLineSize).X > Hud.SigilCardWidth)
                {
                    linesFit = false;
                }
            }

            Check(linesFit, "every Sigil line fits the HUD's card width at its drawing size");

            GameSession session = BuildSession();

            // The session boots in the Grey Wilds, away from the Hearth.
            Check(!session.TryEquipSigil(GameContent.SigilLantern, out SigilEquipFailure away)
                && away == SigilEquipFailure.NotAtHearth,
                "the swap is refused away from the Hearth's camp");

            session.EnterRegion(GameContent.RegionCamp);

            Check(session.TryEquipSigil(GameContent.SigilLantern, out SigilEquipFailure atHearth)
                && atHearth == SigilEquipFailure.None,
                "a run takes up its starting Sigil in the camp");

            SigilDefinition carried = session.EquippedSigil;
            Check(carried != null && !string.IsNullOrEmpty(carried.PriceLine),
                "the carried Sigil exposes its Price line to the HUD");

            session.Encounter.AddDriven(
                session.Player,
                GameContent.BuildPlayerAbilities(),
                new AggressiveDriver(),
                isPlayer: true);

            Check(session.TryEquipSigil(GameContent.SigilSilence, out _), "the Silence Sigil can be taken up");

            Participant participant = session.Encounter.Find(session.Player.Id);
            Check(participant != null && participant.Abilities.LockedAbilityIndex >= 0,
                "Silence marks the loudest ability on its HUD button");
        }

        // ------------------------------------------------------ the juice pass ---

        /// <summary>
        /// The hit feedback's numbers: where a blow falls inside the plan's
        /// hit-stop band and how a wind-up ramps. Both are pure arithmetic in
        /// <see cref="BattleFeedback"/>; asserting them here keeps the view
        /// honest about the budget the plan signed while its drawing stays a
        /// device question.
        /// </summary>
        private void CheckBattleFeedback()
        {
            float min = CombatTuning.HitStopMinMilliseconds / 1000f;
            float max = CombatTuning.HitStopMaxMilliseconds / 1000f;

            float light = BattleFeedback.HitStopSeconds(1f, 100f, false);
            float heavy = BattleFeedback.HitStopSeconds(20f, 100f, false);
            Check(light >= min && heavy <= max && heavy > light,
                "hit-stop stays inside the plan's 40-80 ms band and grows with the blow");

            float critical = BattleFeedback.HitStopSeconds(1f, 100f, true);
            Check(critical > light && critical <= max, "a critical blow takes a heavier stop");

            float startOfWindup = BattleFeedback.TelegraphStrength(CastPhase.Windup, 0.5f, 0.5f);
            float midWindup = BattleFeedback.TelegraphStrength(CastPhase.Windup, 0.5f, 0.25f);
            float endOfWindup = BattleFeedback.TelegraphStrength(CastPhase.Windup, 0.5f, 0f);
            Check(startOfWindup == 0f && midWindup > 0f && midWindup < 1f && endOfWindup == 1f
                && BattleFeedback.TelegraphStrength(CastPhase.Ready, 0.5f, 0.5f) == 0f,
                "the telegraph ramps from 0 to 1 across the wind-up and is silent when ready");
        }

        // ------------------------------------------------------ the hearth ---

        /// <summary>
        /// The Hearth's dismantling loop (plan sections 3.3 and 3.7): gear the
        /// player holds breaks down into the permanent Soot balance - only at
        /// the camp, never the equipped copy, never what is not gear - and the
        /// balance rides a save.
        /// </summary>
        private void CheckHearthSalvage()
        {
            GameSession session = BuildSession();
            session.GrantItem(GameContent.ItemEmberRelic, 1);

            // The session boots in the Grey Wilds, away from the Hearth's camp.
            Check(!session.TrySalvage(GameContent.ItemEmberRelic, out SalvageFailure away, out _)
                && away == SalvageFailure.NotAtHearth,
                "dismantling is refused away from the Hearth's camp");

            Check(session.SootBank.Balance == 0 && session.Inventory.Count(GameContent.ItemEmberRelic) == 1,
                "a refused dismantle loses nothing");

            session.EnterRegion(GameContent.RegionCamp);

            Check(session.TrySalvage(GameContent.ItemEmberRelic, out SalvageFailure failure, out int soot)
                && failure == SalvageFailure.None
                && soot == SalvageTuning.SootFor(ItemRarity.Rare)
                && soot == 15,
                "a rare Relic breaks down into its yield of Soot in the camp");

            Check(session.SootBank.Balance == 15 && session.Inventory.Count(GameContent.ItemEmberRelic) == 0,
                "the piece leaves the bag and the soot lands in the bank");

            session.GrantItem(GameContent.ItemSentinelsCore, 1);
            Check(!session.TrySalvage(GameContent.ItemSentinelsCore, out SalvageFailure bound, out _)
                && bound == SalvageFailure.Bound,
                "a story-bound piece cannot be dismantled");

            session.GrantItem(GameContent.ItemAsh, 3);
            Check(!session.TrySalvage(GameContent.ItemAsh, out SalvageFailure material, out _)
                && material == SalvageFailure.NotSalvageable,
                "materials are not gear and cannot be dismantled");

            string json = SaveSerializer.Serialize(session.CreateSave());
            Check(SaveSerializer.TryDeserialize(json, out SaveGame loaded, out string error),
                "a save carrying soot deserialises" + (error == null ? "" : " (" + error + ")"));

            GameSession restored = BuildSession();
            restored.ApplySave(loaded);

            Check(restored.SootBank.Balance == 15,
                "the Soot balance survives a save round-trip");
        }

        /// <summary>
        /// The Hearth's forging half (plan section 3.3): the hammer spends the
        /// bank on levels, refuses what the bank cannot pay, and the levels ride
        /// a save.
        /// </summary>
        private void CheckHearthForge()
        {
            GameSession session = BuildSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);

            // The session boots in the Grey Wilds, away from the Hearth's camp.
            Check(!session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure away, out _)
                && away == ForgeFailure.NotAtHearth,
                "forging is refused away from the Hearth's camp");

            session.EnterRegion(GameContent.RegionCamp);
            session.SootBank.Deposit(15);

            Check(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out int cost)
                && failure == ForgeFailure.None
                && cost == 12
                && session.Forge.LevelOf(GameContent.ItemSigilbearersBlade) == 1
                && session.SootBank.Balance == 3,
                "the hammer takes 12 Soot and sets the piece to level 1");

            Check(!session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure poor, out int next)
                && poor == ForgeFailure.InsufficientSoot
                && next == 24
                && session.Forge.LevelOf(GameContent.ItemSigilbearersBlade) == 1
                && session.SootBank.Balance == 3,
                "the next level costs 24 and is refused with 3 in the bank");

            string json = SaveSerializer.Serialize(session.CreateSave());
            Check(SaveSerializer.TryDeserialize(json, out SaveGame loaded, out string error),
                "a save carrying forge levels deserialises" + (error == null ? "" : " (" + error + ")"));

            GameSession restored = BuildSession();
            restored.ApplySave(loaded);

            Check(restored.Forge.LevelOf(GameContent.ItemSigilbearersBlade) == 1
                && restored.SootBank.Balance == 3,
                "the forge level and the bank survive a save round-trip");
        }

        // --------------------------------------------------------- prefixes ---

        /// <summary>
        /// The rare prefixes (plan section 3.3) through the engine: a gear drop
        /// can come out prefixed, the same seed replays the same drops, and the
        /// prefixed piece dismantles for its bumped tier.
        /// </summary>
        private void CheckPrefixDrops()
        {
            GameSession session = BuildSession();
            session.RegisterLootTable(new LootTable
            {
                Id = "smoke-gear",
                Guaranteed = new[] { new LootEntry(GameContent.ItemSigilbearersBlade, 1f, 1, 1) },
                MinRolls = 0,
                MaxRolls = 0
            });

            EnemyArchetype archetype = GameContent.BuildHollowWalker();
            Combatant victim = archetype.Create("victim", new Float3(0f, 0f, 6f));
            victim.LootTableId = "smoke-gear";

            var first = new List<string>();
            session.LootGranted += stack => first.Add(stack.ItemId);

            for (int i = 0; i < 40; i++)
            {
                session.GrantLoot(victim);
            }

            string prefixedId = null;
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i].Contains("+"))
                {
                    prefixedId = first[i];
                    break;
                }
            }

            Check(prefixedId != null, "a gear drop can come out carrying a rare prefix");

            if (prefixedId == null)
            {
                return;
            }

            // Replay: an identical run rolls an identical sequence.
            GameSession twin = BuildSession();
            twin.RegisterLootTable(new LootTable
            {
                Id = "smoke-gear",
                Guaranteed = new[] { new LootEntry(GameContent.ItemSigilbearersBlade, 1f, 1, 1) },
                MinRolls = 0,
                MaxRolls = 0
            });

            Combatant twinVictim = archetype.Create("victim", new Float3(0f, 0f, 6f));
            twinVictim.LootTableId = "smoke-gear";

            var second = new List<string>();
            twin.LootGranted += stack => second.Add(stack.ItemId);

            for (int i = 0; i < 40; i++)
            {
                twin.GrantLoot(twinVictim);
            }

            bool same = first.Count == second.Count;
            for (int i = 0; same && i < first.Count; i++)
            {
                same = first[i] == second[i];
            }

            Check(same, "the same seed grants the same prefixed drops");

            // The bumped tier: dismantling reads the variant's higher rarity.
            string baseId = prefixedId.Substring(prefixedId.IndexOf('+') + 1);
            ItemDefinition baseItem = session.Items.Get(baseId);
            int bumpedSoot = SalvageTuning.SootFor(PrefixTuning.Bumped(baseItem.Rarity));

            session.EnterRegion(GameContent.RegionCamp);

            Check(session.TrySalvage(prefixedId, out _, out int soot)
                && soot == bumpedSoot
                && soot > SalvageTuning.SootFor(baseItem.Rarity),
                "the prefixed piece dismantles for its bumped tier");
        }

        // ------------------------------------------------------ the hearth (menu) ---

        /// <summary>
        /// The Hearth page through the interface a player touches: the main
        /// scene is instantiated, the menu opened, and the Hearth visited by
        /// pressing its button. This is where the row labels, the bank note and
        /// the salvage action are proven to exist outside the core.
        /// </summary>
        private void CheckHearthMenu()
        {
            var scene = GD.Load<PackedScene>("res://scenes/Main.tscn");
            Check(scene != null, "the main scene loads for the menu check");

            if (scene == null)
            {
                return;
            }

            Node main = scene.Instantiate();
            AddChild(main);

            var root = main as GameRoot;
            var menu = main.GetNodeOrNull<GameMenu>("UI/GameMenu");

            Check(root != null && root.Session != null && menu != null,
                "the main scene arrives wired: session and menu");

            if (root == null || root.Session == null || menu == null)
            {
                main.QueueFree();
                return;
            }

            // The run starts in the Wilds with its hostiles standing; the hammer
            // waits in the camp. Travel goes through the game's own flow (the same
            // call the World page makes), which clears the old region's encounter
            // and spawns the camp's - none.
            Check(root.TravelTo(GameContent.RegionCamp, out string travelError),
                "the run can travel back to the Hearth's camp"
                + (string.IsNullOrEmpty(travelError) ? "" : " (" + travelError + ")"));
            root.Session.GrantItem(GameContent.ItemEmberRelic, 1);
            root.Session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            root.Session.GrantItem("emberforged+ember-relic", 1);

            menu.SetOpen(true);

            VBoxContainer rows = menu.GetNode<VBoxContainer>("Panel/VBox/Rows");

            string missing = MissingGlyphs(rows);
            Check(missing.Length == 0,
                "every glyph of the main page's rows draws (the Hearth row included)"
                + (missing.Length == 0 ? "" : " (missing: " + missing + ")"));

            Button hearthRow = FindRow(rows, "الموقد — HEARTH");
            Check(hearthRow != null && hearthRow.Text.Contains("سُخام: 0"),
                "the main page shows the Hearth with the banked Soot");

            if (hearthRow != null)
            {
                hearthRow.EmitSignal(BaseButton.SignalName.Pressed);

                missing = MissingGlyphs(rows);
                Check(missing.Length == 0,
                    "every glyph of the Hearth page's rows draws"
                    + (missing.Length == 0 ? "" : " (missing: " + missing + ")"));

                Button salvageRow = FindRow(rows, "فكّ", "Ember Relic");
                Check(salvageRow != null && salvageRow.Text.Contains("15 سُخام"),
                    "the Hearth page offers the Relic for 15 Soot");

                if (salvageRow != null)
                {
                    salvageRow.EmitSignal(BaseButton.SignalName.Pressed);

                    Check(root.Session.SootBank.Balance == 15,
                        "pressing the row dismantles the piece and banks the Soot");
                    Check(FindRow(rows, "سُخام مدَّخر: 15") != null,
                        "the Hearth page redraws with the new balance");
                }

                Button forgeRow = FindRow(rows, "طَرْق", "Sigilbearer's Blade");
                Check(forgeRow != null && forgeRow.Text.Contains("12 سُخام"),
                    "the Hearth page offers the Blade's first level for 12 Soot");

                if (forgeRow != null)
                {
                    forgeRow.EmitSignal(BaseButton.SignalName.Pressed);

                    Check(root.Session.Forge.LevelOf(GameContent.ItemSigilbearersBlade) == 1
                        && root.Session.SootBank.Balance == 3,
                        "pressing the row spends the Soot and raises the level");
                    Check(FindRow(rows, "سُخام مدَّخر: 3") != null,
                        "the Hearth page redraws with the spent balance");
                }

                Button prefixedRow = FindRow(rows, "فكّ", "Emberforged");
                Check(prefixedRow != null && prefixedRow.Text.Contains("40 سُخام"),
                    "a prefixed piece's row carries its bumped dismantling tier");
            }

            menu.SetOpen(false);
            main.QueueFree();
        }

        // ---------------------------------------------------------- the defeat ---

        /// <summary>
        /// The fall-and-rise loop through the surface a player touches: the
        /// bearer falls in the Wilds, the defeat screen opens and cannot be
        /// dismissed, and its one row returns a living bearer to the Hearth's
        /// camp with the Soot meter washed off.
        /// </summary>
        private void CheckDefeatLoop()
        {
            var scene = GD.Load<PackedScene>("res://scenes/Main.tscn");
            Check(scene != null, "the main scene loads for the defeat check");

            if (scene == null)
            {
                return;
            }

            Node main = scene.Instantiate();
            AddChild(main);

            var root = main as GameRoot;
            var menu = main.GetNodeOrNull<GameMenu>("UI/GameMenu");

            Check(root != null && root.Session != null && menu != null,
                "the main scene arrives wired for the defeat check");

            if (root == null || root.Session == null || menu == null)
            {
                main.QueueFree();
                return;
            }

            GameSession session = root.Session;

            // The run starts in the Wilds with the bearer attached to the
            // fight; a lethal blow and the tick that sees the corpse are all it
            // takes to fall.
            session.Player.Soot.NotifyGhasaqUsed();
            session.Player.Soot.NotifyGhasaqUsed();
            Check(session.Player.Soot.Soot > 0f,
                "the run carries a hot Soot meter before the fall");

            session.Player.Vitals.ApplyDamage(session.Player.Vitals.MaxHealth * 2f, null);
            session.Player.Tick(1f / 60f);

            Check(session.IsPlayerFallen && !session.Player.IsAlive,
                "a lethal blow fells the Sigilbearer");
            Check(menu.IsOpen, "the defeat screen opens on the fall");

            VBoxContainer rows = menu.GetNode<VBoxContainer>("Panel/VBox/Rows");

            string missing = MissingGlyphs(rows);
            Check(missing.Length == 0,
                "every glyph of the defeat screen draws"
                + (missing.Length == 0 ? "" : " (missing: " + missing + ")"));

            Button returnRow = FindRow(rows, "RETURN TO THE HEARTH");
            Check(returnRow != null, "the defeat screen offers the one way on");

            // A phone can tap the HUD's menu button, but the defeat screen is
            // not a pause screen: until the rise has run, it stays.
            menu.SetOpen(false);
            Check(menu.IsOpen, "the defeat screen cannot be dismissed before the rise");

            if (returnRow != null)
            {
                returnRow.EmitSignal(BaseButton.SignalName.Pressed);

                bool fullHealth = Math.Abs(session.Player.Vitals.Health - session.Player.Vitals.MaxHealth) < 0.001f;

                Check(!session.IsPlayerFallen && session.Player.IsAlive,
                    "pressing the row raises the bearer");
                Check(fullHealth, "the risen bearer stands at full health");
                Check(session.Player.Soot.Soot == 0f,
                    "the rise washes the Soot meter off");
                Check(session.RegionId == GameContent.RegionCamp,
                    "the risen bearer wakes at the Hearth's camp");
                Check(Math.Abs(session.Player.Position.Z + (root.ArenaHalfExtent - 8f)) < 0.001f,
                    "the risen bearer stands at the camp's arrival spot");
                Check(!menu.IsOpen, "the defeat screen closes when the run resumes");
                Check(session.Encounter.HostilesRemaining == 0,
                    "the camp is empty, so the bearer is not raised into a fight");

                // And the run goes on: the risen bearer can walk back out.
                bool walkedOut = root.TravelTo(GameContent.RegionWilds, out string travelError);
                Check(walkedOut && session.Encounter.HostilesRemaining == 5,
                    "the risen bearer can walk back out to the Wilds"
                    + (string.IsNullOrEmpty(travelError) ? "" : " (" + travelError + ")"));
            }

            main.QueueFree();
        }

        // --------------------------------------------------- accessibility ---

        /// <summary>
        /// The accessibility settings (plan section 6, phase B) through the
        /// surface a player touches: the four settings exist on their page,
        /// each press reaches the surface it claims to change, and every
        /// choice is in the settings file before the next frame. The checks
        /// also hold the two drawn surfaces to their geometry at every text
        /// size step, because a setting that breaks its own layout is worse
        /// than none.
        /// </summary>
        private void CheckAccessibility()
        {
            var defaults = new AccessibilitySettings();
            Check(!defaults.ReduceShake && defaults.DimmingDistortion && !defaults.ColorblindSafe
                && defaults.FontScalePercent == 100,
                "the accessibility defaults keep the shipped presentation (shake full, distortion on, cues off, 100%)");
            Check(defaults.ShakeScale == 1f, "the default shake scale passes impulses through");

            defaults.ReduceShake = true;
            Check(defaults.ShakeScale == AccessibilitySettings.ReducedShakeScale,
                "the reduced shake scale keeps a fifth of the impulse");

            defaults.CycleFontScale();
            Check(defaults.FontScalePercent == 125, "the text size steps to 125%");
            defaults.CycleFontScale();
            Check(defaults.FontScalePercent == 150, "the text size steps to 150%");
            defaults.CycleFontScale();
            Check(defaults.FontScalePercent == 100, "the text size wraps back to 100%");

            defaults.FontScaleIndex = 99;
            defaults.Normalize();
            Check(defaults.FontScaleIndex == AccessibilitySettings.FontScaleSteps.Length - 1,
                "an out-of-range stored text size is clamped to the largest step");

            defaults.FontScaleIndex = -5;
            defaults.Normalize();
            Check(defaults.FontScaleIndex == 0, "a negative stored text size clamps back to 100%");

            // The palette: what the colour-blind setting actually changes.
            Check(AccessibilityPalette.DamageText(12.6f, false, false) == "13"
                && AccessibilityPalette.DamageText(12.6f, true, false) == "13",
                "the shipped palette marks a critical with size and colour only");
            Check(AccessibilityPalette.DamageText(12.6f, true, true) == "13!",
                "the colour-blind palette adds a shape to the critical number");
            Check(!AccessibilityPalette.HealthFill(false).IsEqualApprox(AccessibilityPalette.HealthFill(true))
                && !AccessibilityPalette.StaminaFill(false).IsEqualApprox(AccessibilityPalette.StaminaFill(true))
                && !AccessibilityPalette.Telegraph(false).IsEqualApprox(AccessibilityPalette.Telegraph(true)),
                "the colour-blind palette swaps every state colour it owns");

            // The Dimming's distortion and its off switch.
            Check(Hud.DimmingVignetteAlpha(false, true, 1f, 1f) == 0f,
                "the distortion switch removes the vignette entirely");
            Check(Hud.DimmingVignetteAlpha(true, false, 1f, 1f) == 0f,
                "nothing is drawn below the Dimming's threshold");

            float shallow = Hud.DimmingVignetteAlpha(true, true, 0f, 1f);
            float deep = Hud.DimmingVignetteAlpha(true, true, 1f, 1f);
            Check(shallow > 0f && deep > shallow, "the vignette deepens as the meter fills");
            Check(Hud.DimmingVignetteAlpha(true, true, 1f, 0f) < deep, "the vignette pulses with the frame");

            // The camera's own scale.
            var probeRig = new CameraRig { ShakeScale = AccessibilitySettings.ReducedShakeScale };
            probeRig.Shake(1f);
            Check(Math.Abs(probeRig.PendingShake - AccessibilitySettings.ReducedShakeScale) < 1e-4f,
                "the camera applies the reduced shake scale to an impulse");
            probeRig.ShakeScale = 1f;
            probeRig.Shake(0.5f);
            Check(Math.Abs(probeRig.PendingShake - 0.5f) < 1e-4f,
                "full shake passes an impulse through unchanged");
            probeRig.Free();

            // The file: a missing one loads defaults, a written one carries every option.
            const string probePath = "user://smoke-accessibility.cfg";
            DeleteIfPresent(probePath);

            AccessibilitySettings missing = SettingsStore.Load(probePath);
            Check(!missing.ReduceShake && missing.DimmingDistortion && missing.FontScalePercent == 100,
                "a missing settings file loads the defaults");

            var written = new AccessibilitySettings
            {
                ReduceShake = true,
                DimmingDistortion = false,
                ColorblindSafe = true,
                FontScaleIndex = 2
            };

            Check(SettingsStore.Save(written, probePath), "the settings file writes");

            AccessibilitySettings loaded = SettingsStore.Load(probePath);
            Check(loaded.ReduceShake && !loaded.DimmingDistortion && loaded.ColorblindSafe
                && loaded.FontScalePercent == 150,
                "every option survives a settings round-trip");
            DeleteIfPresent(probePath);

            // Every Sigil line still fits the card at every text-size step: the
            // card and the text scale together, so the fit must hold at all of
            // them, not just at 100%.
            Font font = ThemeDB.FallbackFont;
            List<SigilDefinition> sigils = GameContent.BuildSigils();
            bool fits = font != null;
            float[] steps = AccessibilitySettings.FontScaleSteps;

            for (int step = 0; step < steps.Length && fits; step++)
            {
                int size = Hud.ScaledSize(Hud.SigilLineSize, steps[step]);
                float cardWidth = Hud.SigilCardWidth * steps[step];

                for (int i = 0; i < sigils.Count && fits; i++)
                {
                    fits = font.GetStringSize("الفعل: " + sigils[i].VerbLine, HorizontalAlignment.Left, -1, size).X <= cardWidth
                        && font.GetStringSize("الثمن: " + sigils[i].PriceLine, HorizontalAlignment.Left, -1, size).X <= cardWidth;
                }
            }

            Check(fits, "every Sigil line still fits the card at every text-size step");

            if (font != null)
            {
                string sootLabel = "السُّخام: 100 / 100    عَتْمة";
                float scaledLabelWidth = font.GetStringSize(sootLabel, HorizontalAlignment.Left, -1,
                    Hud.ScaledSize(Hud.BarLabelSize, 1.5f)).X;

                Check(scaledLabelWidth <= Hud.BarWidth * 0.8f * 1.5f,
                    "the Soot bar's longest label still fits the bar at 150%");
            }

            // The page through the menu a player touches. The file is cleared
            // first so the scene under test starts from the shipped defaults
            // even on a machine that ran this smoke test before.
            DeleteIfPresent(SettingsStore.DefaultPath);

            var scene = GD.Load<PackedScene>("res://scenes/Main.tscn");
            Check(scene != null, "the main scene loads for the accessibility check");

            if (scene == null)
            {
                return;
            }

            Node main = scene.Instantiate();
            AddChild(main);

            var root = main as GameRoot;
            var menu = main.GetNodeOrNull<GameMenu>("UI/GameMenu");
            var hud = main.GetNodeOrNull<Hud>("UI/Hud");
            var rig = main.GetNodeOrNull<CameraRig>("CameraRig");

            Check(root != null && root.Session != null && menu != null && hud != null && rig != null,
                "the main scene arrives wired for the accessibility check");

            if (root == null || menu == null || hud == null || rig == null)
            {
                main.QueueFree();
                return;
            }

            Check(!root.Settings.ReduceShake && root.Settings.DimmingDistortion
                && root.Settings.FontScalePercent == 100 && !root.Settings.ColorblindSafe,
                "a fresh launch starts on the shipped accessibility defaults");
            Check(rig.ShakeScale == 1f, "the camera starts with full shake");

            // Fill the bag so the main page runs at its row-pool cap: the
            // accessibility row must still be there, because the players who
            // need it are not required to sort their inventory first.
            string[] fullBag =
            {
                GameContent.ItemSigilbearersBlade,
                GameContent.ItemGhasaqEdge,
                GameContent.ItemAshenPlate,
                GameContent.ItemEmberRelic,
                GameContent.ItemSentinelsCore,
                "emberforged+ember-relic"
            };

            for (int i = 0; i < fullBag.Length; i++)
            {
                root.Session.GrantItem(fullBag[i], 1);
            }

            menu.SetOpen(true);
            VBoxContainer rows = menu.GetNode<VBoxContainer>("Panel/VBox/Rows");

            int visibleRows = 0;

            for (int i = 0; i < rows.GetChildCount(); i++)
            {
                if (rows.GetChild(i) is Button visible && visible.Visible)
                {
                    visibleRows++;
                }
            }

            Check(visibleRows == rows.GetChildCount(), "a full bag fills the row pool to its cap");

            Button pageRow = FindRow(rows, "إعدادات الوصولية — ACCESSIBILITY");
            Check(pageRow != null, "the main page opens the accessibility page (with the bag full and the pool at its cap)");

            if (pageRow == null)
            {
                menu.SetOpen(false);
                main.QueueFree();
                return;
            }

            pageRow.EmitSignal(BaseButton.SignalName.Pressed);

            string missingGlyphs = MissingGlyphs(rows);
            Check(missingGlyphs.Length == 0,
                "every glyph of the accessibility page draws"
                + (missingGlyphs.Length == 0 ? "" : " (missing: " + missingGlyphs + ")"));

            Button shakeRow = FindRow(rows, "REDUCE CAMERA SHAKE");
            Check(shakeRow != null && shakeRow.Text.Contains("OFF"), "the shake row starts at OFF");

            if (shakeRow != null)
            {
                shakeRow.EmitSignal(BaseButton.SignalName.Pressed);
                Check(root.Settings.ReduceShake && rig.ShakeScale == AccessibilitySettings.ReducedShakeScale,
                    "the shake toggle reaches the camera");
                Check(FindRow(rows, "REDUCE CAMERA SHAKE")?.Text.Contains("ON") == true,
                    "the shake row redraws as ON");
            }

            Button distortionRow = FindRow(rows, "DIMMING DISTORTION");
            Check(distortionRow != null && distortionRow.Text.Contains("ON"), "the distortion row starts at ON");

            if (distortionRow != null)
            {
                distortionRow.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!root.Settings.DimmingDistortion && !hud.DimmingDistortion,
                    "the distortion toggle reaches the HUD");
            }

            Button sizeRow = FindRow(rows, "TEXT SIZE");
            Check(sizeRow != null && sizeRow.Text.Contains("100%"), "the text-size row starts at 100%");

            if (sizeRow != null)
            {
                sizeRow.EmitSignal(BaseButton.SignalName.Pressed);
                Check(Math.Abs(hud.FontScale - 1.25f) < 1e-4f, "a press moves the HUD text to 125%");


                Button scaledRow = FindRow(rows, "TEXT SIZE");
                Check(scaledRow != null && scaledRow.Text.Contains("125%"),
                    "the text-size row shows the step it moved to");
                Check(scaledRow != null
                    && scaledRow.GetThemeFontSize("font_size") == Hud.ScaledSize(AccessibilitySettings.BaseMenuFontSize, 1.25f),
                    "the menu's own rows grow with the step");
            }

            Button colorblindRow = FindRow(rows, "COLOUR-BLIND CUES");
            Check(colorblindRow != null && colorblindRow.Text.Contains("OFF"),
                "the colour-blind row starts at OFF");

            if (colorblindRow != null)
            {
                colorblindRow.EmitSignal(BaseButton.SignalName.Pressed);
                Check(root.Settings.ColorblindSafe && hud.ColorblindSafe && CombatantView.ColorblindSafe,
                    "the colour-blind toggle reaches the HUD and the telegraphs");
            }

            // The menu itself at the largest text step: a size setting that
            // pushes a row's words out of the panel would be a feature that
            // hides the very text it was meant to enlarge. Panels are authored
            // 1240 units wide in scenes/GameMenu.tscn; the rows are measured
            // through the live page strings, not a copy of them.
            Button growRow = FindRow(rows, "TEXT SIZE");

            if (growRow != null)
            {
                growRow.EmitSignal(BaseButton.SignalName.Pressed);
                Check(Math.Abs(hud.FontScale - 1.5f) < 1e-4f, "a second press moves the HUD text to 150%");
            }

            const float MenuPanelWidth = 1240f;
            float longest = 0f;
            string longestText = "";
            bool sizeRowFits = true;

            sizeRowFits = MeasureMenuRows(rows, font, ref longest, ref longestText);

            Button backRow = FindRow(rows, "BACK");
            if (backRow != null)
            {
                backRow.EmitSignal(BaseButton.SignalName.Pressed);
                sizeRowFits = MeasureMenuRows(rows, font, ref longest, ref longestText) && sizeRowFits;

                Button sigilRow = FindRow(rows, "SIGIL");
                if (sigilRow != null)
                {
                    sigilRow.EmitSignal(BaseButton.SignalName.Pressed);
                    sizeRowFits = MeasureMenuRows(rows, font, ref longest, ref longestText) && sizeRowFits;

                    backRow = FindRow(rows, "BACK");
                    if (backRow != null)
                    {
                        backRow.EmitSignal(BaseButton.SignalName.Pressed);

                        Button hearthRow = FindRow(rows, "HEARTH");
                        if (hearthRow != null)
                        {
                            hearthRow.EmitSignal(BaseButton.SignalName.Pressed);
                            sizeRowFits = MeasureMenuRows(rows, font, ref longest, ref longestText) && sizeRowFits;

                            backRow = FindRow(rows, "BACK");
                            backRow?.EmitSignal(BaseButton.SignalName.Pressed);
                        }
                    }
                }
            }

            Check(sizeRowFits && longest <= MenuPanelWidth,
                "no menu row runs past its panel at 150%");
            GD.Print($"  info - longest menu line at 150%: {longest:0.#} units against {MenuPanelWidth:0.#}" +
                (longestText.Length > 0 ? " (\"" + longestText + "\")" : ""));

            Check(FileAccess.FileExists(SettingsStore.DefaultPath), "the toggles land in the settings file");

            AccessibilitySettings reloaded = SettingsStore.Load();
            Check(reloaded.ReduceShake && !reloaded.DimmingDistortion && reloaded.ColorblindSafe
                && reloaded.FontScalePercent == 150,
                "the saved file carries exactly the chosen steps");

            // A relaunch is the point of saving: a second main scene must boot
            // on the file the first one wrote, with every surface already
            // changed before the player opens anything.
            Node secondMain = scene.Instantiate();
            AddChild(secondMain);

            var secondRoot = secondMain as GameRoot;
            var secondHud = secondMain.GetNodeOrNull<Hud>("UI/Hud");
            var secondRig = secondMain.GetNodeOrNull<CameraRig>("CameraRig");

            Check(secondRoot != null && secondHud != null && secondRig != null,
                "a relaunch arrives wired for the accessibility check");

            if (secondRoot != null && secondHud != null && secondRig != null)
            {
                Check(secondRoot.Settings.ReduceShake && !secondRoot.Settings.DimmingDistortion
                    && secondRoot.Settings.ColorblindSafe && secondRoot.Settings.FontScalePercent == 150,
                    "a relaunch reads the saved steps before it builds the run");
                Check(secondRig.ShakeScale == AccessibilitySettings.ReducedShakeScale
                    && !secondHud.DimmingDistortion && secondHud.ColorblindSafe
                    && Math.Abs(secondHud.FontScale - 1.5f) < 1e-4f && CombatantView.ColorblindSafe,
                    "a relaunch applies the saved steps to the camera, HUD and telegraphs");
            }

            secondMain.QueueFree();

            // Put the device back the way it was and leave no file behind: the
            // smoke test may run again in the same user directory.
            root.Settings.ReduceShake = false;
            root.Settings.DimmingDistortion = true;
            root.Settings.ColorblindSafe = false;
            root.Settings.FontScaleIndex = 0;
            root.CommitSettings();

            Check(rig.ShakeScale == 1f && hud.DimmingDistortion && Math.Abs(hud.FontScale - 1f) < 1e-4f
                && !CombatantView.ColorblindSafe,
                "restoring the defaults reaches every surface again");
            DeleteIfPresent(SettingsStore.DefaultPath);

            menu.SetOpen(false);
            main.QueueFree();
        }

        /// <summary>
        /// Measures every visible row of the current menu page at the size it
        /// is actually drawn with, keeping the longest line seen. Returns false
        /// if any line has no glyphs in the font, so one walk proves both fit
        /// and readability.
        /// </summary>
        private static bool MeasureMenuRows(VBoxContainer rows, Font font, ref float longest, ref string longestText)
        {
            if (font == null)
            {
                return false;
            }

            bool glyphsPresent = true;

            for (int i = 0; i < rows.GetChildCount(); i++)
            {
                if (!(rows.GetChild(i) is Button row) || !row.Visible)
                {
                    continue;
                }

                int size = row.GetThemeFontSize("font_size");
                string[] lines = row.Text.Split('\n');

                for (int line = 0; line < lines.Length; line++)
                {
                    for (int c = 0; c < lines[line].Length; c++)
                    {
                        if (!font.HasChar(lines[line][c]))
                        {
                            GD.PrintErr("  info - missing glyph U+" + ((int)lines[line][c]).ToString("X4") + " in \"" + lines[line] + "\"");
                        }

                        glyphsPresent = glyphsPresent && font.HasChar(lines[line][c]);
                    }

                    float width = font.GetStringSize(lines[line], HorizontalAlignment.Left, -1, size).X;

                    if (width > longest)
                    {
                        longest = width;
                        longestText = lines[line];
                    }
                }
            }

            return glyphsPresent;
        }

        /// <summary>Removes a user-directory file if it is there; keeps the accessibility checks hermetic.</summary>
        private static void DeleteIfPresent(string path)
        {
            if (FileAccess.FileExists(path))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            }
        }

        /// <summary>
        /// The visible row whose label contains a fragment (and, when given, a
        /// second one too), or null. The second fragment disambiguates pages
        /// that name the same piece twice, such as the Hearth's forge and
        /// salvage rows.
        /// </summary>
        private static Button FindRow(VBoxContainer rows, string fragment, string alsoContains = null)
        {
            for (int i = 0; i < rows.GetChildCount(); i++)
            {
                if (rows.GetChild(i) is Button button
                    && button.Visible
                    && button.Text.Contains(fragment)
                    && (alsoContains == null || button.Text.Contains(alsoContains)))
                {
                    return button;
                }
            }

            return null;
        }

        /// <summary>
        /// Every character of every visible row, checked against the font the
        /// menu draws with. An Arabic label whose glyphs the font lacks is
        /// nothing at all on screen, and setting the text never fails - so the
        /// built strings are measured, not a copy of them.
        /// </summary>
        private static string MissingGlyphs(VBoxContainer rows)
        {
            Font font = ThemeDB.FallbackFont;
            string missing = "";

            for (int i = 0; i < rows.GetChildCount(); i++)
            {
                if (!(rows.GetChild(i) is Button row) || !row.Visible)
                {
                    continue;
                }

                for (int c = 0; c < row.Text.Length; c++)
                {
                    if (!font.HasChar(row.Text[c]))
                    {
                        missing += row.Text[c];
                    }
                }
            }

            return missing;
        }

        // -------------------------------------------------------------- soot ---

        /// <summary>
        /// The Soot surface (plan section 3.6): the meter's draft behaviour,
        /// the kit it follows, and the one engine-side fact that can silently
        /// break the readout - the UI font must carry every glyph the bar
        /// draws, because Godot's built-in font carries no Arabic at all.
        /// </summary>
        private void CheckSootSurface()
        {
            // The meter only moves in play if something in the kit burns.
            bool ghasaqInKit = GameContent.BuildPlayerAbilities().Exists(ability => ability.UsesGhasaqPower);
            Check(ghasaqInKit, "the player kit carries a Ghasaq-keyed ability for the Soot meter to follow");

            var meter = new SootMeter();
            for (int i = 0; i < 3; i++)
            {
                meter.NotifyGhasaqUsed();
            }

            Check(meter.IsDimming && meter.DimmingFraction == 0f,
                "the third burn lands exactly on the Dimming's threshold, still neutral");

            for (int i = 0; i < 2; i++)
            {
                meter.NotifyGhasaqUsed();
            }

            Check(meter.Fraction == 1f && meter.DamageDealtMultiplier > 1.2f && meter.DamageTakenMultiplier > 1.3f,
                "a full meter sharpens both edges of every blow");

            meter.Tick(30f);

            Check(!meter.IsDimming && meter.Soot == SootTuning.Max - (SootTuning.DecayPerSecond * 30f),
                "half a minute of rest carries the meter back below the line");

            // The bar's label, at full and dimmed, measured with the font and
            // size the HUD draws with. It must fit the bar, and every glyph of
            // it must exist in the font - an Arabic label drawn with a Latin
            // font is nothing at all.
            Font font = ThemeDB.FallbackFont;
            string label = "السُّخام: 100 / 100    عَتْمة";

            bool glyphs = font != null;
            for (int i = 0; glyphs && i < label.Length; i++)
            {
                glyphs = font.HasChar(label[i]);
            }

            Check(glyphs, "the UI font covers every glyph of the Soot bar's label");

            float labelWidth = font.GetStringSize(label, HorizontalAlignment.Left, -1, Hud.BarLabelSize).X;
            Check(labelWidth <= Hud.BarWidth * 0.8f,
                "the Soot bar's longest label fits the bar it is drawn on");

            GD.Print($"  info - soot label width {labelWidth:0.#} px against {Hud.BarWidth * 0.8f:0.#} px");
        }
    }
}
