using System;
using System.Collections.Generic;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Items;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Quests;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.World;

namespace Ghasaq.Core.Simulation
{
    /// <summary>
    /// One playthrough, in progress.
    ///
    /// This is the layer that makes the individual systems one game. It owns the
    /// player, the RPG systems, the world and the running encounter, and it is the
    /// only place that knows how defeating an enemy turns into experience, loot
    /// and quest progress.
    ///
    /// Keeping that wiring here - rather than spread across combat, loot and quest
    /// code - is what allows the whole chain to be asserted in one test: kill an
    /// enemy, and check that the journal moved, the experience went up and the
    /// drops landed in the bag.
    ///
    /// Encounters themselves are deliberately not persisted. A save records the
    /// player's progress and location; the enemies for a region are respawned when
    /// it is entered. That keeps saves small and matches how the genre behaves.
    /// </summary>
    public sealed class GameSession
    {
        private readonly Dictionary<string, LootTable> _lootTables;
        private readonly Dictionary<string, SigilDefinition> _sigils;
        private readonly List<SigilDefinition> _sigilOrder;
        private readonly List<PrefixDefinition> _prefixOrder;
        private readonly List<ItemStack> _lootBuffer;

        /// <summary>
        /// Guards against a re-entrant call into <see cref="AdvanceQuests"/> from
        /// inside it. Claiming a reward can add items, which can complete a collect
        /// objective, which must then also be turned in - the outer loop drains that
        /// rather than the inner call recursing into it.
        /// </summary>
        private bool _advancingQuests;

        public GameSession(
            Combatant player,
            ItemDatabase items,
            ExperienceCurve curve,
            StatGrowth[] growth,
            DeterministicRng rng,
            WorldBounds bounds = default(WorldBounds),
            IOcclusionProvider occlusion = null)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Items = items ?? throw new ArgumentNullException(nameof(items));

            Inventory = new Inventory(Items);
            SootBank = new SootBank();
            Forge = new ItemForge();
            Equipment = new EquipmentLoadout(Player, Items, Forge);
            Quests = new QuestLog();
            Chapters = new ChapterTracker(Quests);
            World = new WorldGraph();
            Progression = new ProgressionSystem(Player, curve ?? new ExperienceCurve(), growth);

            _lootTables = new Dictionary<string, LootTable>(StringComparer.Ordinal);
            _sigils = new Dictionary<string, SigilDefinition>(StringComparer.Ordinal);
            _sigilOrder = new List<SigilDefinition>(8);
            _prefixOrder = new List<PrefixDefinition>(8);
            _lootBuffer = new List<ItemStack>(8);

            Encounter = new EncounterSimulation(rng, bounds, occlusion);
            Encounter.Died += OnEncounterDeath;
        }

        public Combatant Player { get; private set; }

        public ItemDatabase Items { get; private set; }

        public Inventory Inventory { get; private set; }

        public EquipmentLoadout Equipment { get; private set; }

        /// <summary>
        /// The permanent السُّخام / Soot balance, fed by dismantling gear at the
        /// Hearth (plan section 3.3). Separate from the in-run meter on the
        /// player, which prices a single fight and is deliberately never saved.
        /// </summary>
        public SootBank SootBank { get; private set; }

        /// <summary>
        /// Forged levels, one per piece the player owns (plan section 3.3).
        /// The loadout reads these when it applies a worn piece's modifiers.
        /// </summary>
        public ItemForge Forge { get; private set; }

        public QuestLog Quests { get; private set; }

        public ChapterTracker Chapters { get; private set; }

        public WorldGraph World { get; private set; }

        public ProgressionSystem Progression { get; private set; }

        public EncounterSimulation Encounter { get; private set; }

        /// <summary>
        /// The single random source for the session, which is the encounter's own
        /// generator.
        ///
        /// Loot and combat deliberately share one stream. Keeping two would mean
        /// the saved state described only one of them, so loading a save would let
        /// combat rolls replay values that loot had already consumed, and the two
        /// would silently diverge from the run they were meant to reproduce.
        /// </summary>
        public DeterministicRng Rng
        {
            get { return Encounter.Rng; }
        }

        // ------------------------------- save metadata -----------------------------

        public string SlotId { get; set; } = "slot-1";

        public string ProfileName { get; set; } = "Wanderer";

        public string DifficultyId { get; set; } = "wanderer";

        private string _regionId = "";

        /// <summary>
        /// Region the player is currently in.
        ///
        /// Setting it also records the region as discovered. Being somewhere you have
        /// never been is not a state that should be able to exist, and the starting
        /// region is set directly by the content rather than by entering it - so
        /// without this the camp was never marked visited and the player could never
        /// fast-travel back to the place they began.
        /// </summary>
        public string RegionId
        {
            get { return _regionId; }
            set
            {
                _regionId = value;

                if (!string.IsNullOrEmpty(value) && !DiscoveredRegions.Contains(value))
                {
                    DiscoveredRegions.Add(value);
                }
            }
        }

        public List<string> DiscoveredRegions { get; private set; } = new List<string>();

        public float PlaytimeSeconds { get; set; }

        // ---------------------------------- events --------------------------------

        /// <summary>Raised for each item stack that actually entered the bag.</summary>
        public event Action<ItemStack> LootGranted;

        /// <summary>Raised for every hostile defeated, before rewards are applied.</summary>
        public event Action<Combatant> EnemyDefeated;

        /// <summary>Raised with the number of levels gained from experience.</summary>
        public event Action<int> LevelledUp;

        /// <summary>Raised for each quest whose reward has just been claimed.</summary>
        public event Action<QuestState> QuestTurnedIn;

        /// <summary>
        /// Whether finished quests are turned in automatically, granting their
        /// rewards and offering whatever they unlock.
        ///
        /// This defaults to on because a quest only becomes startable once its
        /// prerequisite has been TURNED IN, and this build has no quest-giver to do
        /// the turning in. Without it the player finishes the opening quest, receives
        /// nothing, and no further quest ever becomes available - the whole story is
        /// unreachable. Turn it off once a quest-giver hands out the next task.
        /// </summary>
        public bool AutoAdvanceQuests { get; set; } = true;

        // -------------------------------- configuration ---------------------------

        public void RegisterLootTable(LootTable table)
        {
            if (table == null || string.IsNullOrEmpty(table.Id))
            {
                return;
            }

            _lootTables[table.Id] = table;
        }

        /// <summary>
        /// Makes a Sigil available to this run. Like loot tables, the session is
        /// handed content by whoever built it rather than reaching for the
        /// content module itself: the simulation layer does not know what game
        /// it is running.
        /// </summary>
        public void RegisterSigil(SigilDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                return;
            }

            // Re-registering replaces the definition in place and leaves the
            // order alone, so a Hearth listing stays stable across a rebuild.
            if (!_sigils.ContainsKey(definition.Id))
            {
                _sigilOrder.Add(definition);
            }

            _sigils[definition.Id] = definition;
        }

        /// <summary>
        /// Makes a rare prefix available to this run's loot rolls (plan section
        /// 3.3). Like Sigils and loot tables, the session is handed content by
        /// whoever built it.
        /// </summary>
        public void RegisterPrefix(PrefixDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                return;
            }

            // Re-registering replaces in place and leaves the order alone, so
            // the weighted stream stays stable across a content rebuild.
            for (int i = 0; i < _prefixOrder.Count; i++)
            {
                if (string.Equals(_prefixOrder[i].Id, definition.Id, StringComparison.Ordinal))
                {
                    _prefixOrder[i] = definition;
                    return;
                }
            }

            _prefixOrder.Add(definition);
        }

        /// <summary>The الوَسْم / Sigil this run carries, or null while it carries none.</summary>
        public SigilDefinition EquippedSigil
        {
            get { return Player.Sigil == null ? null : Player.Sigil.Definition; }
        }

        /// <summary>
        /// Every Sigil this run knows about, in the order it was registered.
        /// The Hearth lists these; the order is what makes that list stable
        /// between openings.
        /// </summary>
        public IReadOnlyList<SigilDefinition> Sigils
        {
            get { return _sigilOrder; }
        }

        /// <summary>
        /// Takes up a Sigil, applying the one hard swap rule of the slice: a
        /// Sigil is chosen at the Hearth, never mid-fight (plan section 3.1).
        /// Equipping is the only way a loadout appears on the player, so an
        /// enemy can never be carrying one by accident.
        /// </summary>
        public bool TryEquipSigil(string sigilId, out SigilEquipFailure failure)
        {
            failure = SigilEquipFailure.UnknownSigil;

            if (string.IsNullOrEmpty(sigilId) || !_sigils.TryGetValue(sigilId, out SigilDefinition definition))
            {
                return false;
            }

            if (Encounter.HostilesRemaining > 0)
            {
                failure = SigilEquipFailure.InCombat;
                return false;
            }

            // The Hearth stands in a camp. The rule is asked of the region's
            // kind rather than of a region id, so the simulation layer keeps
            // knowing how the world behaves, not what this game's map is called.
            RegionDefinition here = World.Get(RegionId);
            if (here == null || here.Kind != RegionKind.Camp)
            {
                failure = SigilEquipFailure.NotAtHearth;
                return false;
            }

            if (Player.Sigil == null)
            {
                Player.Sigil = new SigilLoadout(Player);
            }

            Player.Sigil.Equip(definition);
            failure = SigilEquipFailure.None;
            return true;
        }

        /// <summary>
        /// Applies a sigil id from a save. A save naming a Sigil this build does
        /// not know is left unequipped rather than failing the load: the rest of
        /// the character is still perfectly playable.
        /// </summary>
        private void RestoreSigil(string sigilId)
        {
            if (!string.IsNullOrEmpty(sigilId)
                && _sigils.TryGetValue(sigilId, out SigilDefinition definition))
            {
                if (Player.Sigil == null)
                {
                    Player.Sigil = new SigilLoadout(Player);
                }

                Player.Sigil.Equip(definition);
                return;
            }

            if (Player.Sigil != null)
            {
                Player.Sigil.Unequip();
            }
        }

        /// <summary>Replaces the encounter, for moving to a new region. Subscriptions are rewired.</summary>
        public void SetEncounter(EncounterSimulation encounter)
        {
            if (encounter == null)
            {
                return;
            }

            Encounter.Died -= OnEncounterDeath;
            Encounter = encounter;
            Encounter.Died += OnEncounterDeath;
        }

        /// <summary>Advances the running encounter.</summary>
        public int Update(float deltaTime)
        {
            int steps = Encounter.Update(deltaTime);

            if (steps > 0)
            {
                PlaytimeSeconds += deltaTime;
            }

            // A safety net for any path that moved a quest along without going
            // through one of the explicit call sites. Cheap: a handful of quests.
            AdvanceQuests();

            return steps;
        }

        // ------------------------------ quest lifecycle ---------------------------

        /// <summary>
        /// Claims the reward for every finished quest, then starts whatever that
        /// makes available. Returns how many quests changed state.
        ///
        /// Two things happen here and both matter. A completed quest is turned in,
        /// which is the only moment its experience, attribute points and items are
        /// actually granted. And turning one in is what makes its dependants
        /// startable, so the loop repeats: finish a quest, claim it, and the next one
        /// in the chain becomes available.
        ///
        /// Without this the game had exactly one playable quest. Every quest after
        /// the first requires its predecessor to be *turned in*, nothing performed
        /// that step, and so the story could not progress past the opening scene.
        /// </summary>
        public int AdvanceQuests()
        {
            if (!AutoAdvanceQuests || _advancingQuests)
            {
                return 0;
            }

            _advancingQuests = true;

            try
            {
                int advances = 0;

                // Bounded by the quest count: each pass turns one quest in or starts
                // one, and no quest can do either twice.
                int limit = (Quests.All.Count * 2) + 2;

                for (int guard = 0; guard < limit; guard++)
                {
                    QuestState completed = FindCompletedAwaitingTurnIn();

                    if (completed != null)
                    {
                        string questId = completed.Definition.Id;

                        if (Quests.TryTurnIn(questId, out QuestReward reward))
                        {
                            advances++;

                            // Announced before the reward, so the journal message
                            // reads before the loot that came with it.
                            QuestTurnedIn?.Invoke(completed);

                            GrantReward(reward);
                            continue;
                        }
                    }

                    QuestState startable = FindNextStartable();

                    if (startable != null && Quests.TryStart(startable.Definition.Id))
                    {
                        advances++;
                        continue;
                    }

                    break;
                }

                if (advances > 0)
                {
                    Chapters.Refresh();
                }

                return advances;
            }
            finally
            {
                _advancingQuests = false;
            }
        }

        private QuestState FindCompletedAwaitingTurnIn()
        {
            IReadOnlyList<QuestState> all = Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Status == QuestStatus.Completed)
                {
                    return all[i];
                }
            }

            return null;
        }

        private QuestState FindNextStartable()
        {
            IReadOnlyList<QuestState> all = Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];

                if (state.Status == QuestStatus.Locked && Quests.IsStartable(state.Definition.Id))
                {
                    return state;
                }
            }

            return null;
        }

        // ------------------------------ reward plumbing ---------------------------

        /// <summary>
        /// Applies everything that follows from defeating a combatant: loot, then
        /// experience, then quest progress, then chapter re-evaluation.
        ///
        /// Loot is granted before experience so that a level-up gained from the kill
        /// cannot change the loot roll for that same kill.
        /// </summary>
        public void ReportDefeat(Combatant victim)
        {
            if (victim == null || victim.Faction != Faction.Hostile)
            {
                return;
            }

            EnemyDefeated?.Invoke(victim);

            GrantLoot(victim);

            int levels = Progression.AddExperience(victim.ExperienceReward);
            if (levels > 0)
            {
                LevelledUp?.Invoke(levels);
            }

            if (!string.IsNullOrEmpty(victim.ArchetypeId))
            {
                Quests.Report(QuestEvent.Kill(victim.ArchetypeId));

                if (victim.IsBoss)
                {
                    Quests.Report(QuestEvent.DefeatBoss(victim.ArchetypeId));
                }
            }

            Chapters.Refresh();
            AdvanceQuests();
        }

        /// <summary>Rolls a victim's loot table and puts what it yields into the bag.</summary>
        public int GrantLoot(Combatant victim)
        {
            if (victim == null || string.IsNullOrEmpty(victim.LootTableId))
            {
                return 0;
            }

            if (!_lootTables.TryGetValue(victim.LootTableId, out LootTable table))
            {
                return 0;
            }

            table.Roll(Rng, Progression.Level, 0f, _lootBuffer);

            int granted = 0;
            for (int i = 0; i < _lootBuffer.Count; i++)
            {
                // A rolled prefix swaps the stack for its variant before the bag
                // sees it, so everything downstream - bag, loadout, forge, save -
                // meets one ordinary id.
                string itemId = RollPrefix(_lootBuffer[i].ItemId);
                granted += GrantItem(itemId, _lootBuffer[i].Quantity);
            }

            return granted;
        }

        /// <summary>
        /// Rolls the rare prefix onto one looted stack (plan section 3.3).
        ///
        /// Drops only: quest rewards and starting kits keep their plain ids,
        /// so a story handout cannot surprise anyone with an affix. Only
        /// non-bound gear is eligible. The roll consumes the shared stream the
        /// same way every time, so a save replays to the same drops.
        /// </summary>
        private string RollPrefix(string baseItemId)
        {
            if (_prefixOrder.Count == 0)
            {
                return baseItemId;
            }

            if (!Items.TryGet(baseItemId, out ItemDefinition definition)
                || definition == null
                || !definition.IsEquippable
                || definition.IsBound)
            {
                return baseItemId;
            }

            if (!Rng.Chance(PrefixTuning.Chance))
            {
                return baseItemId;
            }

            PrefixDefinition prefix = PickPrefix();
            if (prefix == null)
            {
                return baseItemId;
            }

            // If the content did not build this pair, the drop stays plain
            // rather than becoming an id the bag would silently skip.
            string variantId = prefix.VariantId(baseItemId);
            return Items.Contains(variantId) ? variantId : baseItemId;
        }

        private PrefixDefinition PickPrefix()
        {
            float total = 0f;

            for (int i = 0; i < _prefixOrder.Count; i++)
            {
                float weight = _prefixOrder[i].Weight;
                if (weight > 0f)
                {
                    total += weight;
                }
            }

            if (total <= 0f)
            {
                return null;
            }

            float roll = Rng.Range(0f, total);
            float cumulative = 0f;

            for (int i = 0; i < _prefixOrder.Count; i++)
            {
                float weight = _prefixOrder[i].Weight;
                if (weight <= 0f)
                {
                    continue;
                }

                cumulative += weight;
                if (roll < cumulative)
                {
                    return _prefixOrder[i];
                }
            }

            // Floating point can leave the last weight just short of the roll.
            for (int i = _prefixOrder.Count - 1; i >= 0; i--)
            {
                if (_prefixOrder[i].Weight > 0f)
                {
                    return _prefixOrder[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Adds items to the bag and syncs any collection objectives.
        ///
        /// Items that will not fit are dropped rather than queued: the return value
        /// reports how many were accepted, so the Unity layer can leave a world
        /// pickup on the ground for the player to come back for.
        /// </summary>
        public int GrantItem(string itemId, int quantity)
        {
            int accepted = Inventory.Add(itemId, quantity);

            if (accepted <= 0)
            {
                return 0;
            }

            LootGranted?.Invoke(new ItemStack(itemId, accepted));
            SyncCollectionObjectives();

            // Collect objectives are satisfied by holding an item, so picking the
            // last one up can finish the quest then and there.
            AdvanceQuests();

            return accepted;
        }

        /// <summary>Applies a quest reward: experience, attribute points and items.</summary>
        public void GrantReward(in QuestReward reward)
        {
            int levels = Progression.AddExperience(reward.Experience);
            if (levels > 0)
            {
                LevelledUp?.Invoke(levels);
            }

            if (reward.AttributePoints > 0)
            {
                Progression.GrantAttributePoints(reward.AttributePoints);
            }

            ItemStack[] items = reward.Items;
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                GrantItem(items[i].ItemId, items[i].Quantity);
            }
        }

        /// <summary>
        /// Pulls every active collection objective's progress from the inventory.
        ///
        /// Collection objectives track what the player currently holds rather than
        /// a tally of pickups, so they must be re-synced whenever the bag changes.
        /// Reporting pickups cumulatively would let a player pick an item up and
        /// drop it repeatedly to finish the quest.
        /// </summary>
        public void SyncCollectionObjectives()
        {
            IReadOnlyList<QuestState> all = Quests.All;

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                ObjectiveDefinition[] objectives = state.Definition.Objectives;

                for (int j = 0; j < objectives.Length; j++)
                {
                    ObjectiveDefinition objective = objectives[j];
                    if (objective == null || objective.Kind != ObjectiveKind.Collect)
                    {
                        continue;
                    }

                    Quests.SetProgress(state.Definition.Id, objective.Id, Inventory.Count(objective.TargetId));
                }
            }
        }

        // -------------------------------- equipment -------------------------------

        /// <summary>
        /// Moves an item out of the bag and onto the character, putting whatever it
        /// replaces back into the bag. Returns false and leaves everything untouched
        /// when the item cannot be equipped.
        ///
        /// The swap is one transaction rather than an equip followed by a separate
        /// attempt to stow the old item. Equipping first would destroy the replaced
        /// item every time the bag is full - which is precisely the situation a player
        /// is in when they finally find an upgrade.
        ///
        /// This lives here rather than in the UI because it is the rule that decides
        /// whether an item can be lost, and rules belong where they can be tested.
        /// </summary>
        public bool TryEquipFromInventory(string itemId, out EquipFailure failure)
        {
            failure = EquipFailure.UnknownItem;

            if (string.IsNullOrEmpty(itemId) || !Items.TryGet(itemId, out ItemDefinition definition))
            {
                return false;
            }

            if (!definition.Slot.HasValue)
            {
                failure = EquipFailure.NotEquippable;
                return false;
            }

            EquipSlot slot = definition.Slot.Value;

            if (!Inventory.Has(itemId))
            {
                // Not held, so there is nothing to move. Reported as unknown rather
                // than as a slot problem, because the slot is fine.
                return false;
            }

            // Validated before anything moves, so a rejected equip leaves the bag and
            // the loadout exactly as they were.
            failure = Equipment.CanEquip(slot, definition);

            if (failure != EquipFailure.None)
            {
                return false;
            }

            if (Inventory.Remove(itemId, 1) <= 0)
            {
                return false;
            }

            if (!Equipment.TryEquip(slot, definition, out ItemDefinition replaced, out failure))
            {
                // Put it back rather than letting a failed equip swallow it.
                Inventory.Add(itemId, 1);
                return false;
            }

            if (replaced != null)
            {
                // Removing the new item freed the slot it occupied, so whatever it
                // replaced always has somewhere to go. When the two are the same item
                // id this is simply a second copy going back in the bag.
                Inventory.Add(replaced.Id, 1);
                SyncCollectionObjectives();
            }

            return true;
        }

        /// <summary>
        /// Returns the item in a slot to the bag. Fails rather than destroying it when
        /// there is nowhere to put it.
        /// </summary>
        public bool TryUnequipToInventory(EquipSlot slot)
        {
            if (Equipment.IsEmpty(slot))
            {
                return false;
            }

            if (!Equipment.TryUnequip(slot, out ItemDefinition removed) || removed == null)
            {
                return false;
            }

            if (Inventory.Add(removed.Id, 1) <= 0)
            {
                // Nowhere to put it, so it goes straight back on. Silently dropping it
                // would be a real loss of a real item.
                Equipment.TryEquip(slot, removed, out _, out _);
                return false;
            }

            SyncCollectionObjectives();
            return true;
        }

        /// <summary>
        /// Breaks one piece of carried gear down at the Hearth's hammer and banks
        /// its soot (plan sections 3.3 and 3.7).
        ///
        /// The piece must be gear the player holds: an equipped copy is not in the
        /// bag and never comes off to pay for this. The act belongs to the Hearth -
        /// a camp region, no hostiles standing - the same place and the same calm
        /// the Sigil swap requires. Every rule is checked before anything moves, so
        /// a refused dismantle loses nothing, and the item leaving the bag and the
        /// soot landing in the bank happen in one call, so neither can happen
        /// without the other.
        /// </summary>
        public bool TrySalvage(string itemId, out SalvageFailure failure, out int soot)
        {
            failure = SalvageFailure.UnknownItem;
            soot = 0;

            if (string.IsNullOrEmpty(itemId)
                || !Items.TryGet(itemId, out ItemDefinition definition)
                || definition == null)
            {
                return false;
            }

            if (definition.IsBound)
            {
                failure = SalvageFailure.Bound;
                return false;
            }

            if (!definition.IsEquippable)
            {
                failure = SalvageFailure.NotSalvageable;
                return false;
            }

            if (!Inventory.Has(itemId))
            {
                failure = SalvageFailure.NotHeld;
                return false;
            }

            if (Encounter.HostilesRemaining > 0)
            {
                failure = SalvageFailure.InCombat;
                return false;
            }

            // Same reading of "the Hearth" as the Sigil swap: the region's kind,
            // not a region id, so the simulation layer never learns this game's
            // map by name.
            RegionDefinition here = World.Get(RegionId);
            if (here == null || here.Kind != RegionKind.Camp)
            {
                failure = SalvageFailure.NotAtHearth;
                return false;
            }

            if (Inventory.Remove(itemId, 1) <= 0)
            {
                failure = SalvageFailure.NotHeld;
                return false;
            }

            soot = SalvageTuning.SootFor(definition.Rarity);
            SootBank.Deposit(soot);

            // The piece is destroyed, and its forged levels die with it: a fresh
            // drop of the same id must never inherit an old investment. Nothing
            // refunds the levels - the plan's forge is a commitment, not a rent.
            Forge.SetLevel(itemId, 0);

            // The bag changed, so a collection objective may have moved - possibly
            // backwards, if the dismantled piece was one the player had to hold.
            SyncCollectionObjectives();
            AdvanceQuests();

            failure = SalvageFailure.None;
            return true;
        }

        /// <summary>
        /// Hammers one piece the player owns at the Hearth: the bank pays the
        /// next level's price and the piece gains a level (plan section 3.3,
        /// "طرق في الموقد").
        ///
        /// The piece may be carried or worn - a worn piece's stats are re-applied
        /// on the spot, so the number on the HUD moves the moment the hammer
        /// falls. Like dismantling, this belongs to the Hearth: a camp region
        /// with no hostiles standing. The bank is charged all or nothing, so a
        /// refusal can never take the Soot and leave the level un-bought.
        /// </summary>
        public bool TryForge(string itemId, out ForgeFailure failure, out int cost)
        {
            failure = ForgeFailure.UnknownItem;
            cost = 0;

            if (string.IsNullOrEmpty(itemId)
                || !Items.TryGet(itemId, out ItemDefinition definition)
                || definition == null)
            {
                return false;
            }

            if (definition.IsBound)
            {
                failure = ForgeFailure.Bound;
                return false;
            }

            if (!definition.IsEquippable)
            {
                failure = ForgeFailure.NotForgeable;
                return false;
            }

            if (!Owns(itemId))
            {
                failure = ForgeFailure.NotOwned;
                return false;
            }

            if (Encounter.HostilesRemaining > 0)
            {
                failure = ForgeFailure.InCombat;
                return false;
            }

            RegionDefinition here = World.Get(RegionId);
            if (here == null || here.Kind != RegionKind.Camp)
            {
                failure = ForgeFailure.NotAtHearth;
                return false;
            }

            int level = Forge.LevelOf(itemId);

            if (level >= ForgeTuning.MaxLevel)
            {
                failure = ForgeFailure.MaxLevel;
                return false;
            }

            // Reported even when the bank cannot pay it, so the refusal can
            // name the price instead of just "no".
            cost = ForgeTuning.CostForLevel(level);

            if (!SootBank.TrySpend(cost))
            {
                failure = ForgeFailure.InsufficientSoot;
                return false;
            }

            Forge.SetLevel(itemId, level + 1);

            // A worn piece must show its new level without a swap.
            for (int i = 0; i < EquipSlots.Count; i++)
            {
                EquipSlot slot = (EquipSlot)i;

                if (string.Equals(Equipment.GetEquipped(slot), itemId, StringComparison.Ordinal))
                {
                    Equipment.RefreshModifiers(slot);
                }
            }

            failure = ForgeFailure.None;
            return true;
        }

        /// <summary>Whether the player carries or wears this piece.</summary>
        private bool Owns(string itemId)
        {
            if (Inventory.Has(itemId))
            {
                return true;
            }

            for (int i = 0; i < EquipSlots.Count; i++)
            {
                if (string.Equals(Equipment.GetEquipped((EquipSlot)i), itemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // -------------------------------- consumables -----------------------------

        /// <summary>
        /// Uses one of a consumable stack, applying its effects to the player and
        /// taking it out of the bag.
        ///
        /// Effects clamp rather than refuse: a draught drunk at full health is spent
        /// and heals nothing. Deciding whether a use is "worth it" is the player's
        /// call to make, and a rule that guesses at it would also have to arbitrate
        /// partial cases - full health but no stamina, and so on.
        ///
        /// Like equipment, this lives here rather than in the UI because it is the
        /// rule that decides when an item is gone.
        /// </summary>
        public bool TryUseConsumable(string itemId, out ConsumableFailure failure)
        {
            failure = ConsumableFailure.UnknownItem;

            if (string.IsNullOrEmpty(itemId) || !Items.TryGet(itemId, out ItemDefinition definition) || definition == null)
            {
                return false;
            }

            if (!definition.IsConsumable)
            {
                failure = ConsumableFailure.NotConsumable;
                return false;
            }

            if (Inventory.Remove(itemId, 1) <= 0)
            {
                failure = ConsumableFailure.NotHeld;
                return false;
            }

            ItemEffect[] effects = definition.Effects;

            if (effects != null)
            {
                for (int i = 0; i < effects.Length; i++)
                {
                    ApplyItemEffect(effects[i], definition);
                }
            }

            // The bag changed, so collection objectives may have moved - possibly
            // backwards, if the consumed item was one the player had to hold.
            SyncCollectionObjectives();
            AdvanceQuests();

            failure = ConsumableFailure.None;
            return true;
        }

        private void ApplyItemEffect(ItemEffect effect, object source)
        {
            switch (effect.Kind)
            {
                case EffectKind.RestoreHealth:
                    Player.Vitals.Heal(effect.Amount);
                    break;

                case EffectKind.RestoreStamina:
                    Player.Vitals.RestoreStamina(effect.Amount);
                    break;

                case EffectKind.ApplyStatus:
                    Player.Statuses.Apply(StatusEffect.Modifier(
                        effect.Status,
                        effect.Amount,
                        effect.Duration,
                        source));
                    break;

                case EffectKind.GrantExperience:
                    int levels = Progression.AddExperience((int)effect.Amount);
                    if (levels > 0)
                    {
                        LevelledUp?.Invoke(levels);
                    }
                    break;
            }
        }

        // -------------------------------- collection ------------------------------

        /// <summary>True when the encounter has no living hostiles left.</summary>
        public bool EncounterCleared
        {
            get { return Encounter.HostilesRemaining <= 0; }
        }

        /// <summary>
        /// Whether the player may move to a region from where they currently are.
        ///
        /// <see cref="WorldGraph.CanEnter"/> answers a narrower question - is the
        /// chapter gate open - and deliberately knows nothing about where the player
        /// is standing. A caller that only asked that would let the player step from
        /// the camp straight into the final sanctum, and the whole region graph would
        /// be decorative.
        ///
        /// So the rule is: the chapter gate must be open, AND the destination must be
        /// either next door or somewhere already visited. Next door keeps the world
        /// connected; already-visited is fast travel, which is what makes returning to
        /// the camp and re-running an area reasonable rather than a walk.
        /// </summary>
        public bool CanTravelTo(string regionId, out AccessFailure failure)
        {
            failure = World.CanEnter(regionId, Chapters);

            if (failure != AccessFailure.None)
            {
                return false;
            }

            if (string.IsNullOrEmpty(regionId) ||
                string.Equals(regionId, RegionId, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<string> neighbours = World.Neighbours(RegionId);

            for (int i = 0; i < neighbours.Count; i++)
            {
                if (string.Equals(neighbours[i], regionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (DiscoveredRegions.Contains(regionId))
            {
                return true;
            }

            failure = AccessFailure.NotConnected;
            return false;
        }

        /// <summary>Moves the player to a region, applying the travel rule. Reports why not on failure.</summary>
        public bool TryTravelTo(string regionId, out AccessFailure failure)
        {
            if (!CanTravelTo(regionId, out failure))
            {
                return false;
            }

            return EnterRegion(regionId);
        }

        /// <summary>Records that the player has reached a region, and reports it to quests.</summary>
        public bool EnterRegion(string regionId)
        {
            AccessFailure access = World.CanEnter(regionId, Chapters);
            if (access != AccessFailure.None)
            {
                return false;
            }

            // The setter records the discovery, so being here and having been here
            // cannot disagree.
            RegionId = regionId;

            Quests.Report(QuestEvent.Reach(regionId));
            Chapters.Refresh();
            AdvanceQuests();
            return true;
        }

        // --------------------------------- saving ---------------------------------

        public SaveGame CreateSave()
        {
            var save = new SaveGame
            {
                SlotId = SlotId,
                ProfileName = ProfileName,
                DifficultyId = DifficultyId,
                Version = SaveGame.CurrentVersion,
                PlaytimeSeconds = PlaytimeSeconds,
                RegionId = RegionId,
                Position = Player.Position,
                FacingDegrees = Player.FacingDegrees,
                TotalExperience = Progression.TotalExperience,
                UnspentAttributePoints = Progression.UnspentAttributePoints,
                StatBoosts = Progression.CopyStatBoosts(),
                Inventory = Inventory.ToStacks(),
                Equipment = Equipment.ToStacks(),
                Quests = CollectQuestSnapshots(),
                DiscoveredRegions = new List<string>(DiscoveredRegions),
                EquippedSigilId = Player.Sigil != null && Player.Sigil.Definition != null
                    ? Player.Sigil.Definition.Id
                    : "",
                SootBalance = SootBank.Balance,
                Forge = Forge.ToStacks(),
                RngState = Rng.State,
                RngIncrement = Rng.Increment
            };

            return save;
        }

        /// <summary>
        /// Restores a session from a save.
        ///
        /// Intended to be called on a freshly constructed session before play
        /// begins. Progression growth is applied as a difference, so calling this
        /// after the character has already levelled does not double-count anything,
        /// but the player's vitals are still reset rather than carrying damage from
        /// whatever was happening when the save was written.
        /// </summary>
        public void ApplySave(SaveGame save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            SlotId = save.SlotId;
            ProfileName = save.ProfileName;
            DifficultyId = save.DifficultyId;
            PlaytimeSeconds = save.PlaytimeSeconds;

            Player.SetPosition(save.Position);
            Player.SetFacing(save.FacingDegrees);

            Progression.LoadFrom(save.TotalExperience, save.UnspentAttributePoints, save.StatBoosts);
            Inventory.LoadFrom(save.Inventory, out _);
            SootBank.LoadFrom(save.SootBalance);

            // Before the loadout: the equipment applies each worn piece's
            // modifiers, and a forged piece's bonus is part of that contribution.
            Forge.LoadFrom(save.Forge);
            Equipment.LoadFrom(save.Equipment);
            ApplyQuestSnapshots(save.Quests);

            // The carried Sigil is content the session was handed, so it is
            // restored by id after the content is registered.
            RestoreSigil(save.EquippedSigilId);

            DiscoveredRegions = save.DiscoveredRegions == null
                ? new List<string>()
                : new List<string>(save.DiscoveredRegions);

            // Set after the list is replaced, not before: assigning the region marks it
            // discovered, and doing that first would have it wiped by the line above.
            RegionId = save.RegionId;

            // The generator is restored exactly, so subsequent loot and combat
            // rolls continue the sequence rather than restarting it. Restoring it
            // on the encounter is enough, because that IS the session's generator.
            Encounter.RestoreRng(save.RngState, save.RngIncrement);

            Player.Vitals.ResetToFull();
            Player.Statuses.Clear();

            SyncCollectionObjectives();
            Chapters.Refresh();

            // A save taken between completing a quest and claiming it would
            // otherwise load into a state where the reward is permanently stranded.
            AdvanceQuests();
        }

        private List<QuestSnapshot> CollectQuestSnapshots()
        {
            IReadOnlyList<QuestState> all = Quests.All;
            var snapshots = new List<QuestSnapshot>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                QuestState state = all[i];
                if (state.Status == QuestStatus.Locked)
                {
                    continue;
                }

                snapshots.Add(new QuestSnapshot(
                    state.Definition.Id,
                    state.Status,
                    state.ToProgressArray()));
            }

            return snapshots;
        }

        private void ApplyQuestSnapshots(List<QuestSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                QuestSnapshot snapshot = snapshots[i];
                QuestState state = Quests.State(snapshot.QuestId);

                // A quest whose content has since been removed is skipped rather
                // than failing the whole load.
                if (state == null)
                {
                    continue;
                }

                state.LoadProgress(snapshot.Progress);
                state.Status = snapshot.Status;
            }
        }

        private void OnEncounterDeath(Combatant victim, Participant participant)
        {
            ReportDefeat(victim);
        }
    }
}
