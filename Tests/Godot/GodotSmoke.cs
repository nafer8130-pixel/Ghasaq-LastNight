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
    /// - a font with the Arabic glyphs and five authored Price lines - and the
    /// Hearth's salvage loop must run from the menu a player actually touches.
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
            CheckHearthMenu();

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

                Button salvageRow = FindRow(rows, "Ember Relic");
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
            }

            menu.SetOpen(false);
            main.QueueFree();
        }

        /// <summary>The visible row whose label contains a fragment, or null.</summary>
        private static Button FindRow(VBoxContainer rows, string fragment)
        {
            for (int i = 0; i < rows.GetChildCount(); i++)
            {
                if (rows.GetChild(i) is Button button && button.Visible && button.Text.Contains(fragment))
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
