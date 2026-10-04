using System.Collections.Generic;
using Ghasaq.Core.Ai;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Content;
using Ghasaq.Core.Items;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.Simulation;
using Ghasaq.Core.Tests.Support;
using Xunit;

namespace Ghasaq.Core.Tests.Combat
{
    /// <summary>
    /// Tests the five الوَسْم / Sigils and their الثمن / Prices.
    ///
    /// Each Sigil gets one test for the new verb it grants and one for what it
    /// charges, plus the content-level contract: a Sigil without a readable
    /// Price is a content error, not a style choice (plan section 3.2). The
    /// numbers asserted here are the draft values of Documentation/Sigils.md,
    /// pinned the way CombatTuning pins the plan's budgets.
    /// </summary>
    public class SigilTests
    {
        private static readonly Float3 Forward = new Float3(0f, 0f, 1f);

        // ------------------------------- fixtures --------------------------------

        private static Combatant Sigilbearer(
            string sigilId,
            float attackPower = 100f,
            float maxHealth = 100f,
            Float3 position = default(Float3))
        {
            Combatant bearer = CombatantFactory.Create(
                "bearer",
                Faction.Player,
                maxHealth: maxHealth,
                attackPower: attackPower,
                position: position);

            bearer.FaceImmediately(Forward);
            bearer.Sigil = new SigilLoadout(bearer);
            bearer.Sigil.Equip(GameContent.FindSigil(sigilId));
            return bearer;
        }

        private static Combatant Enemy(
            string id,
            float maxHealth = 500f,
            float armor = 0f,
            Float3 position = default(Float3),
            float facingDegrees = 0f)
        {
            Combatant enemy = CombatantFactory.Create(
                id,
                Faction.Hostile,
                maxHealth: maxHealth,
                armor: armor,
                position: position);

            enemy.FaceImmediately(Combatant.DegreesToDirection(facingDegrees));
            return enemy;
        }

        private static AbilityDefinition Strike()
        {
            return CombatantFactory.Strike();
        }

        private static AbilityDefinition DashAbility(float distance = 5.5f)
        {
            return new AbilityDefinition
            {
                Id = "blink",
                DisplayName = "Blink",
                Kind = AbilityKind.Dash,
                StaminaCost = 0f,
                CooldownSeconds = 1f,
                WindupSeconds = 0f,
                RecoverySeconds = 0.1f,
                DashDistance = distance,
                MoveSpeedDuringWindup = 0f
            };
        }

        /// <summary>States awareness directly, so Silence tests need no perception scene.</summary>
        private sealed class FixedAwareness : IAwarenessProbe
        {
            public bool Unaware = true;

            public bool IsUnaware(Combatant target)
            {
                return Unaware;
            }
        }

        private static GameSession NewSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(1234),
                WorldBounds.Square(40f));

            GameContent.Populate(session);
            return session;
        }

        // ---------------------------- the numbers, pinned --------------------------

        [Fact]
        public void TheFivePrices_MatchTheSpec()
        {
            Assert.Equal(5.5f, SigilTuning.LanternBlinkDistance, 3);
            Assert.Equal(0.2f, SigilTuning.LanternInvulnerabilitySeconds, 3);
            Assert.Equal(15f, SigilTuning.LanternPriceAcquireRadius, 3);
            Assert.Equal(2f, SigilTuning.LanternPriceAcquireSeconds, 3);

            Assert.Equal(2.5f, SigilTuning.AshBurstRadius, 3);
            Assert.Equal(0.6f, SigilTuning.AshBurstDamageFraction, 3);
            Assert.Equal(0.25f, SigilTuning.AshOverkillBiteFraction, 3);

            Assert.Equal(2.5f, SigilTuning.SilenceExecutionMultiplier, 3);
            Assert.Equal(0.2f, SigilTuning.SilenceExecutionHealthFraction, 3);

            Assert.Equal(0.08f, SigilTuning.HungerHealFraction, 3);
            Assert.Equal(8f, SigilTuning.HungerFamineSeconds, 3);
            Assert.Equal(1f, SigilTuning.HungerFamineTickSeconds, 3);

            Assert.Equal(0.35f, SigilTuning.GlassShieldHealthFraction, 3);
            Assert.Equal(0.5f, SigilTuning.GlassShieldAbsorbFraction, 3);
            Assert.Equal(2.5f, SigilTuning.GlassShatterRadius, 3);
            Assert.Equal(2f, SigilTuning.GlassExposedSeconds, 3);
            Assert.Equal(0.25f, SigilTuning.GlassExposedDamageTakenBonus, 3);
            Assert.Equal(12f, SigilTuning.GlassReformSeconds, 3);
        }

        [Fact]
        public void TheFiveSigils_AreAuthoredWithAVisiblePrice()
        {
            List<SigilDefinition> sigils = GameContent.BuildSigils();

            Assert.Equal(5, sigils.Count);

            var kinds = new HashSet<SigilId>();

            foreach (SigilDefinition sigil in sigils)
            {
                Assert.False(string.IsNullOrWhiteSpace(sigil.Id));
                Assert.False(string.IsNullOrWhiteSpace(sigil.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(sigil.EnglishName));
                Assert.False(string.IsNullOrWhiteSpace(sigil.VerbLine));
                Assert.False(
                    string.IsNullOrWhiteSpace(sigil.PriceLine),
                    sigil.Id + " carries no Price line, so the HUD could never show its Price.");

                Assert.NotEqual(SigilId.None, sigil.Kind);
                Assert.True(kinds.Add(sigil.Kind), "Duplicate Sigil kind " + sigil.Kind + ".");
            }

            Assert.Equal(5, kinds.Count);
            Assert.NotNull(GameContent.FindSigil(GameContent.SigilLantern));
            Assert.Null(GameContent.FindSigil("no-such-sigil"));
        }

        // --------------------------------- lantern --------------------------------

        [Fact]
        public void LanternBlink_OpensAnIFrameWindowThatRefusesDamage()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilLantern);
            var controller = new AbilityController(bearer, new List<AbilityDefinition> { DashAbility() });

            Assert.True(controller.TryActivate(0, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.None, failure);

            // The window opens at commitment, so it covers the blink itself.
            Assert.True(bearer.Vitals.IsInvulnerable);
            Assert.Equal(0f, bearer.Vitals.ApplyDamage(50f, null));
            Assert.Equal(100f, bearer.Vitals.Health, 3);

            bearer.Vitals.Tick(SigilTuning.LanternInvulnerabilitySeconds + 0.01f);
            Assert.False(bearer.Vitals.IsInvulnerable);
            Assert.Equal(50f, bearer.Vitals.ApplyDamage(50f, null), 3);
        }

        [Fact]
        public void LanternDash_LandsTheBlinkAndLeavesOneAcquisitionRequest()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilLantern);
            var hits = new List<Combatant>();

            int struck = AttackResolver.Resolve(
                bearer,
                DashAbility(),
                new List<Combatant>(),
                new DeterministicRng(1),
                hits);

            Assert.Equal(0, struck);
            Assert.Equal(SigilTuning.LanternBlinkDistance, bearer.Position.Z, 3);

            Assert.True(bearer.Sigil.TryTakeAcquire(out float radius, out float seconds));
            Assert.Equal(SigilTuning.LanternPriceAcquireRadius, radius, 3);
            Assert.Equal(SigilTuning.LanternPriceAcquireSeconds, seconds, 3);

            Assert.False(bearer.Sigil.TryTakeAcquire(out _, out _), "The request must be taken once.");
        }

        private sealed class DashOnceDriver : ICombatantDriver
        {
            public float DelaySeconds;
            public bool Fired;

            public CombatIntent Decide(float deltaTime, Combatant self, EncounterSimulation world)
            {
                CombatIntent intent = CombatIntent.None();

                if (Fired)
                {
                    return intent;
                }

                DelaySeconds -= deltaTime;

                if (DelaySeconds > 0f)
                {
                    return intent;
                }

                Participant participant = world.FindParticipant(self);
                if (participant != null && participant.Abilities.IsReady(0))
                {
                    Fired = true;
                    intent.ActivateAbility = true;
                    intent.AbilityIndex = 0;
                }

                return intent;
            }
        }

        [Fact]
        public void LanternPrice_AcquiresHostilesThroughCover()
        {
            // Cover everywhere and eyes shorter than the gap: the only way any of
            // these enemies can notice the bearer is the Price of the blink.
            var sim = new EncounterSimulation(new DeterministicRng(7), WorldBounds.Square(40f), new BlindSight());

            Combatant bearer = Sigilbearer(GameContent.SigilLantern, maxHealth: 320f, attackPower: 20f);
            var driver = new DashOnceDriver { DelaySeconds = 0.4f };
            sim.AddDriven(bearer, new List<AbilityDefinition> { DashAbility() }, driver, isPlayer: true);

            var settings = new EnemyBrainSettings
            {
                ViewDistance = 8f,
                ViewHalfAngleDegrees = 70f,
                ProximityRadius = 1f,
                LoseSightGrace = 5f,
                ReactionTime = 0.1f,
                IdleDuration = 999f
            };

            Combatant near = Enemy("near", position: new Float3(0f, 0f, 10f));
            Combatant far = Enemy("far", position: new Float3(0f, 0f, 25f));
            Participant nearParticipant = sim.AddEnemy(near, new List<AbilityDefinition> { Strike() }, settings);
            Participant farParticipant = sim.AddEnemy(far, new List<AbilityDefinition> { Strike() }, settings);

            sim.Advance(0.4f);

            Assert.False(nearParticipant.Brain.IsAlerted, "cover alone must not expose the bearer.");
            Assert.False(farParticipant.Brain.IsAlerted);

            sim.Advance(1f);

            Assert.True(driver.Fired, "the blink should have fired.");
            Assert.True(nearParticipant.Brain.IsAlerted, "a blink must give the bearer away through cover.");
            Assert.False(farParticipant.Brain.IsAlerted, "only hostiles inside the radius acquire.");
        }

        // ----------------------------------- ash -----------------------------------

        [Fact]
        public void AshKill_BurstsAroundTheCorpse()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilAsh, attackPower: 100f);
            Combatant victim = Enemy("victim", maxHealth: 30f, position: new Float3(0f, 0f, 2f));
            Combatant neighbour = Enemy("neighbour", maxHealth: 500f, position: new Float3(0f, 0f, 4f));
            Combatant distant = Enemy("distant", maxHealth: 500f, position: new Float3(0f, 0f, 12f));

            var candidates = new List<Combatant> { victim, neighbour, distant };
            var hits = new List<Combatant>();

            int struck = AttackResolver.Resolve(bearer, Strike(), candidates, new DeterministicRng(1), hits);

            // Only the victim is inside the swing; the neighbour is inside the
            // burst, and the far one is inside neither.
            Assert.Equal(1, struck);
            Assert.False(victim.IsAlive);
            Assert.Equal(500f - 60f, neighbour.Vitals.Health, 3);
            Assert.Equal(500f, distant.Vitals.Health, 3);
        }

        [Fact]
        public void AshOverkill_BitesBack()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilAsh, attackPower: 100f, maxHealth: 320f);
            Combatant victim = Enemy("victim", maxHealth: 30f, position: new Float3(0f, 0f, 2f));

            AttackResolver.Strike(bearer, Strike(), victim, new DeterministicRng(1));

            // 100 damage into 30 health: 70 overkill, a quarter of it comes back.
            Assert.False(victim.IsAlive);
            Assert.Equal(320f - 17.5f, bearer.Vitals.Health, 3);
        }

        [Fact]
        public void AshBurst_DoesNotChainIntoAnotherBurst()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilAsh, attackPower: 100f);
            Combatant victim = Enemy("victim", maxHealth: 30f, position: new Float3(0f, 0f, 2f));
            Combatant neighbour = Enemy("neighbour", maxHealth: 30f, position: new Float3(0f, 0f, 4f));
            Combatant witness = Enemy("witness", maxHealth: 500f, position: new Float3(0f, 0f, 6f));

            var candidates = new List<Combatant> { victim, neighbour, witness };
            AttackResolver.Resolve(bearer, Strike(), candidates, new DeterministicRng(1), new List<Combatant>());

            Assert.False(victim.IsAlive);
            Assert.False(neighbour.IsAlive, "the burst should take the neighbour.");

            // Two metres from the neighbour and four from the victim: only a
            // second, chained burst could ever have reached it.
            Assert.Equal(500f, witness.Vitals.Health, 3);
        }

        // --------------------------------- silence ---------------------------------

        [Fact]
        public void SilenceFromBehind_MultipliesTheBlow()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilSilence, attackPower: 100f);

            // Facing away from the bearer, and stated unaware.
            Combatant behind = Enemy("behind", position: new Float3(0f, 0f, 2f));
            var probe = new FixedAwareness { Unaware = true };

            AttackResolver.Resolve(
                bearer,
                Strike(),
                new List<Combatant> { behind },
                new DeterministicRng(1),
                new List<Combatant>(),
                probe);

            Assert.Equal(500f - 250f, behind.Vitals.Health, 3);
        }

        [Fact]
        public void SilenceAgainstAFacingTarget_IsAnOrdinaryBlow()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilSilence, attackPower: 100f);
            Combatant facing = Enemy("facing", position: new Float3(0f, 0f, 2f), facingDegrees: 180f);

            AttackResolver.Strike(bearer, Strike(), facing, new DeterministicRng(1), new FixedAwareness());

            Assert.Equal(400f, facing.Vitals.Health, 3);
        }

        [Fact]
        public void SilenceOnAnAwareTarget_IsAnOrdinaryBlow()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilSilence, attackPower: 100f);
            Combatant behind = Enemy("behind", position: new Float3(0f, 0f, 2f));
            var probe = new FixedAwareness { Unaware = false };

            AttackResolver.Strike(bearer, Strike(), behind, new DeterministicRng(1), probe);

            Assert.Equal(400f, behind.Vitals.Health, 3);
        }

        [Fact]
        public void SilenceExecutesABehindTargetUnderTheHealthThreshold()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilSilence, attackPower: 1f);
            Combatant doomed = Enemy("doomed", maxHealth: 500f, armor: 5000f, position: new Float3(0f, 0f, 2f));

            doomed.Vitals.ApplyDamage(430f, null);
            Assert.True(doomed.Vitals.HealthFraction < SigilTuning.SilenceExecutionHealthFraction);

            AttackResolver.Strike(bearer, Strike(), doomed, new DeterministicRng(1), new FixedAwareness());

            Assert.False(doomed.IsAlive, "the execution bypasses the armour that would stop a normal blow.");
        }

        [Fact]
        public void SilencePrice_LocksTheLoudestAbilityAndNothingElse()
        {
            Combatant bearer = CombatantFactory.Create("bearer", Faction.Player, maxStamina: 100f);
            var abilities = new List<AbilityDefinition>
            {
                CombatantFactory.Strike(windup: 0.1f, staminaCost: 0f),
                CombatantFactory.Strike(windup: 0.6f, staminaCost: 0f),
                CombatantFactory.Strike(windup: 0.3f, staminaCost: 0f)
            };

            var controller = new AbilityController(bearer, abilities);

            Assert.Equal(1, controller.LoudestAbilityIndex);
            Assert.Equal(-1, controller.LockedAbilityIndex);

            bearer.Sigil = new SigilLoadout(bearer);
            bearer.Sigil.Equip(GameContent.FindSigil(GameContent.SigilSilence));

            Assert.True(controller.IsLocked(1));
            Assert.Equal(1, controller.LockedAbilityIndex);
            Assert.False(controller.IsReady(1));
            Assert.False(controller.TryActivate(1, out AbilityFailure failure));
            Assert.Equal(AbilityFailure.Locked, failure);

            // The other two are untouched: the Price is a seal, not a silence of
            // the whole kit.
            Assert.True(controller.TryActivate(0, out _));
            controller.Tick(0.5f);
            Assert.True(controller.TryActivate(2, out _));
            controller.Tick(1f);

            bearer.Sigil.Unequip();
            Assert.False(controller.IsLocked(1));
            Assert.Equal(AbilityFailure.None, controller.CanActivate(1));
        }

        [Fact]
        public void SilenceLocksSunderInTheShippedKit()
        {
            Combatant player = GameContent.CreatePlayer();
            List<AbilityDefinition> abilities = GameContent.BuildPlayerAbilities();

            player.Sigil = new SigilLoadout(player);
            player.Sigil.Equip(GameContent.FindSigil(GameContent.SigilSilence));

            var controller = new AbilityController(player, abilities);
            int locked = controller.LockedAbilityIndex;

            Assert.True(locked >= 0, "the Price must have a visible target in the shipped kit.");
            Assert.Equal("sunder", abilities[locked].Id);
            Assert.Equal(AbilityFailure.Locked, controller.CanActivate(locked));
        }

        // ---------------------------------- hunger ---------------------------------

        [Fact]
        public void HungerHeal_OnEveryLandedHit()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilHunger, attackPower: 100f, maxHealth: 100f);
            bearer.Vitals.ApplyDamage(40f, null);

            Combatant target = Enemy("target", position: new Float3(0f, 0f, 2f));
            AttackResolver.Strike(bearer, Strike(), target, new DeterministicRng(1));

            // 100 damage landed, 8% of it fed back.
            Assert.Equal(68f, bearer.Vitals.Health, 3);
        }

        [Fact]
        public void HungerFamine_StartsAfterEightSecondsWithoutAHit()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilHunger, maxHealth: 100f);

            bearer.Sigil.Tick(SigilTuning.HungerFamineSeconds - 0.1f);
            Assert.False(bearer.Sigil.IsFamineActive);

            bearer.Sigil.Tick(0.2f);
            Assert.True(bearer.Sigil.IsFamineActive);
            Assert.True(
                bearer.Statuses.Has(StatusKind.Starving),
                "the famine is its own affliction, not a wound from something else.");

            // The famine ticks Ghasaq damage every second until a hit lands.
            bearer.Tick(SigilTuning.HungerFamineTickSeconds);
            Assert.Equal(98f, bearer.Vitals.Health, 3);
        }

        [Fact]
        public void HungerFamine_StopsTheMomentAHitLands()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilHunger, attackPower: 100f, maxHealth: 100f);

            bearer.Sigil.Tick(SigilTuning.HungerFamineSeconds + 0.1f);
            Assert.True(bearer.Sigil.IsFamineActive);

            Combatant target = Enemy("target", position: new Float3(0f, 0f, 2f));
            AttackResolver.Strike(bearer, Strike(), target, new DeterministicRng(1));

            Assert.False(bearer.Sigil.IsFamineActive);
            Assert.Equal(0f, bearer.Sigil.SecondsSinceLandedHit, 3);

            // With the famine gone, time alone no longer hurts.
            float health = bearer.Vitals.Health;
            bearer.Tick(2f);
            Assert.Equal(health, bearer.Vitals.Health, 3);
        }

        // ---------------------------------- glass ----------------------------------

        [Fact]
        public void GlassShield_AbsorbsItsShareOfABlow()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilGlass, maxHealth: 100f);

            // 35% of 100 health: the shield holds 35.
            Assert.Equal(35f, bearer.Sigil.ShieldRemaining, 3);
            Assert.Equal(35f, bearer.Sigil.ShieldCapacity, 3);

            bearer.ReceiveDamage(new DamageResult(40f, 40f, 40f, false, 0f), null);

            // Half of the 40 goes into the shield.
            Assert.Equal(80f, bearer.Vitals.Health, 3);
            Assert.Equal(15f, bearer.Sigil.ShieldRemaining, 3);
        }

        [Fact]
        public void GlassShield_BreaksIntoExposureAndAShatterRequest()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilGlass, maxHealth: 100f, attackPower: 100f);

            bearer.ReceiveDamage(new DamageResult(100f, 100f, 100f, false, 0f), null);

            // 50 would be absorbed but the shield only holds 35, so 35 is
            // absorbed and the remaining 65 lands.
            Assert.Equal(35f, bearer.Vitals.Health, 3);
            Assert.False(bearer.Sigil.ShieldActive);
            Assert.Equal(SigilTuning.GlassReformSeconds, bearer.Sigil.ShieldReformRemaining, 3);

            // Exposure is the Marked shape: stronger incoming damage, for its two seconds.
            Assert.True(bearer.Statuses.Has(StatusKind.Marked));
            Assert.Equal(SigilTuning.GlassExposedDamageTakenBonus, bearer.Statuses.Magnitude(StatusKind.Marked), 3);
            Assert.True(bearer.Statuses.DamageTakenMultiplier > 1f);

            // The blades are a request until the world resolves them.
            Assert.True(bearer.Sigil.TryTakeShatter(out float damage, out float radius));
            Assert.Equal(75f, damage, 3);
            Assert.Equal(SigilTuning.GlassShatterRadius, radius, 3);
        }

        [Fact]
        public void GlassShatter_BurstsThroughTheEncounter()
        {
            var sim = new EncounterSimulation(new DeterministicRng(11), WorldBounds.Square(40f));

            Combatant bearer = Sigilbearer(GameContent.SigilGlass, maxHealth: 100f, attackPower: 100f);
            sim.AddDriven(bearer, new List<AbilityDefinition> { Strike() }, new PassiveDriver(), isPlayer: true);

            Combatant biter = Enemy("biter", maxHealth: 500f, position: new Float3(0f, 0f, 2f));
            sim.AddEnemy(biter, new List<AbilityDefinition> { Strike() }, new EnemyBrainSettings());

            // Break the shield directly: the point here is that the encounter
            // finishes the request, not how the blow arrived.
            bearer.ReceiveDamage(new DamageResult(100f, 100f, 100f, false, 0f), biter);
            Assert.False(bearer.Sigil.ShieldActive);

            sim.Step();

            // 75% of 100 attack power, through the biter's bare armour.
            Assert.Equal(425f, biter.Vitals.Health, 3);
        }

        [Fact]
        public void GlassShield_ReformsAfterItsCooldown()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilGlass, maxHealth: 100f);

            bearer.ReceiveDamage(new DamageResult(100f, 100f, 100f, false, 0f), null);
            Assert.False(bearer.Sigil.ShieldActive);

            bearer.Sigil.Tick(SigilTuning.GlassReformSeconds - 0.1f);
            Assert.False(bearer.Sigil.ShieldActive);

            bearer.Sigil.Tick(0.2f);
            Assert.True(bearer.Sigil.ShieldActive);
            Assert.Equal(35f, bearer.Sigil.ShieldRemaining, 3);
        }

        [Fact]
        public void Revival_ReArmsThePrice()
        {
            Combatant bearer = Sigilbearer(GameContent.SigilGlass, maxHealth: 100f, attackPower: 100f);

            bearer.ReceiveDamage(new DamageResult(100f, 100f, 100f, false, 0f), null);
            Assert.False(bearer.Sigil.ShieldActive);
            Assert.True(bearer.Statuses.Has(StatusKind.Marked));

            bearer.Revive(Float3.Zero, 0f);

            // The boss-retry path: a revival is a full re-arm, so a retry does
            // not start with a broken shield or a pending burst.
            Assert.True(bearer.Sigil.ShieldActive);
            Assert.Equal(35f, bearer.Sigil.ShieldRemaining, 3);
            Assert.False(bearer.Statuses.Has(StatusKind.Marked));
            Assert.False(bearer.Sigil.TryTakeShatter(out _, out _), "a revival must not leave a shatter pending.");
        }

        // --------------------------------- carrying --------------------------------

        [Fact]
        public void TryEquipSigil_RefusesMidFightAndAllowsWhenClear()
        {
            GameSession session = NewSession();

            Assert.True(session.TryEquipSigil(GameContent.SigilLantern, out SigilEquipFailure failure));
            Assert.Equal(SigilEquipFailure.None, failure);
            Assert.Equal(SigilId.Lantern, session.Player.Sigil.Kind);
            Assert.Equal(GameContent.SigilLantern, session.EquippedSigil.Id);

            Assert.False(session.TryEquipSigil("no-such-sigil", out failure));
            Assert.Equal(SigilEquipFailure.UnknownSigil, failure);

            // A hostile on its feet means the Hearth is out of reach.
            EnemyArchetype archetype = GameContent.BuildHollowWalker();
            Combatant walker = archetype.Create("walker", new Float3(0f, 0f, 8f));
            session.Encounter.AddEnemy(walker, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);

            Assert.False(session.TryEquipSigil(GameContent.SigilAsh, out failure));
            Assert.Equal(SigilEquipFailure.InCombat, failure);
            Assert.Equal(SigilId.Lantern, session.Player.Sigil.Kind);
        }

        [Fact]
        public void TryEquipSigil_RefusesOutsideACamp()
        {
            GameSession session = NewSession();

            // The Hearth stands in the camp, so the wilds cannot take a Sigil up.
            session.EnterRegion(GameContent.RegionWilds);
            Assert.False(session.TryEquipSigil(GameContent.SigilAsh, out SigilEquipFailure failure));
            Assert.Equal(SigilEquipFailure.NotAtHearth, failure);
            Assert.Null(session.Player.Sigil);

            // Back at the Hearth's camp, out of combat, the swap goes through.
            session.EnterRegion(GameContent.RegionCamp);
            Assert.True(session.TryEquipSigil(GameContent.SigilAsh, out failure));
            Assert.Equal(SigilEquipFailure.None, failure);
            Assert.Equal(SigilId.Ash, session.Player.Sigil.Kind);
        }

        [Fact]
        public void TheCarriedSigil_RoundTripsThroughASave()
        {
            GameSession session = NewSession();
            Assert.True(session.TryEquipSigil(GameContent.SigilHunger, out _));

            string json = SaveSerializer.Serialize(session.CreateSave());
            Assert.True(SaveSerializer.TryDeserialize(json, out SaveGame save, out string error), error);

            GameSession restored = NewSession();
            restored.ApplySave(save);

            Assert.NotNull(restored.Player.Sigil);
            Assert.Equal(SigilId.Hunger, restored.Player.Sigil.Kind);
        }

        [Fact]
        public void ASaveNamingAnUnknownSigil_LoadsUnequipped()
        {
            GameSession session = NewSession();
            Assert.True(session.TryEquipSigil(GameContent.SigilGlass, out _));

            SaveGame save = session.CreateSave();
            save.EquippedSigilId = "no-such-sigil";

            session.ApplySave(save);

            Assert.True(session.Player.Sigil == null || !session.Player.Sigil.HasSigil);
        }

        [Fact]
        public void TheEncounter_AnswersAwarenessFromTheBrain()
        {
            var sim = new EncounterSimulation(new DeterministicRng(3), WorldBounds.Square(30f), new BlindSight());

            Combatant player = CombatantFactory.Create("player", Faction.Player, position: Float3.Zero);
            sim.AddDriven(player, new List<AbilityDefinition> { Strike() }, new PassiveDriver(), isPlayer: true);

            var settings = new EnemyBrainSettings
            {
                ViewDistance = 8f,
                ProximityRadius = 1f,
                IdleDuration = 999f
            };

            Combatant walker = Enemy("walker", position: new Float3(0f, 0f, 20f));
            sim.AddEnemy(walker, new List<AbilityDefinition> { Strike() }, settings);

            Assert.True(sim.IsUnaware(walker), "out of sight and out of mind.");

            // Being hit always alerts, regardless of facing or cover.
            walker.ReceiveDamage(new DamageResult(1f, 1f, 1f, false, 0f), player);
            sim.Step();

            Assert.False(sim.IsUnaware(walker));
        }

        [Fact]
        public void TheFamine_AdvancesOnTheEncountersClock()
        {
            var sim = new EncounterSimulation(new DeterministicRng(5), WorldBounds.Square(30f));

            Combatant bearer = Sigilbearer(GameContent.SigilHunger, maxHealth: 100f);
            sim.AddDriven(bearer, new List<AbilityDefinition> { Strike() }, new PassiveDriver(), isPlayer: true);

            // One full tick past the start of the famine, because the bite
            // itself lands on the status clock, not the moment it begins.
            sim.Advance(SigilTuning.HungerFamineSeconds + SigilTuning.HungerFamineTickSeconds + 0.1f);

            Assert.True(bearer.Sigil.IsFamineActive);
            Assert.True(bearer.Vitals.Health < bearer.Vitals.MaxHealth, "the famine must have bitten.");
        }

        [Fact]
        public void TheShippedDash_IsTheLanternsBlink()
        {
            // The Sigil's spec number and the kit's own move must not drift
            // apart: a blink whose window covered a different distance would be
            // a different Sigil.
            bool found = false;

            foreach (AbilityDefinition ability in GameContent.BuildPlayerAbilities())
            {
                if (ability.Kind != AbilityKind.Dash)
                {
                    continue;
                }

                found = true;
                Assert.Equal(SigilTuning.LanternBlinkDistance, ability.DashDistance, 3);
            }

            Assert.True(found, "the kit needs a dash for the Lantern to be a blink.");
        }
    }
}
