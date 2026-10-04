using System;
using Ghasaq.Core.Stats;

namespace Ghasaq.Core.Combat
{
    /// <summary>
    /// The runtime half of the Sigil system: one Sigil carried by one combatant,
    /// its Price being paid, and the pieces of world-facing work it leaves for
    /// the encounter to finish.
    ///
    /// Only the player ever carries a Sigil, so every hook in the combat path is
    /// one null check away from neutral. The loadout itself follows the same
    /// rule as the rest of the core: it mutates state and raises no engine
    /// dependency, so a Price can be asserted in a test the same way damage is.
    ///
    /// Two of the five Prices need something the damage pipeline does not have -
    /// a target list. The Lantern's acquisition needs the enemies around the
    /// bearer, and the Glass shatter needs everyone in range, while a combatant
    /// receiving a blow knows neither. Those are recorded here as plain values
    /// and consumed by <see cref="Ghasaq.Core.Simulation.EncounterSimulation"/>
    /// right after blows land; a caller driving combat by hand consumes them
    /// itself. The Ash burst needs the same list, but a strike already holds it,
    /// so that one is resolved inside <see cref="AttackResolver.Resolve"/> rather
    /// than queued. Nothing in this class allocates.
    /// </summary>
    public sealed class SigilLoadout
    {
        private readonly Combatant _owner;

        private SigilDefinition _definition;

        // Glass
        private float _shieldRemaining;
        private float _shieldCapacity;
        private float _reformRemaining;
        private bool _hasShatter;
        private float _shatterDamage;
        private float _shatterRadius;

        // Lantern
        private bool _hasAcquire;
        private float _acquireRadius;
        private float _acquireSeconds;

        // Hunger
        private float _secondsSinceLandedHit;
        private StatusEffect _famine;

        public SigilLoadout(Combatant owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public Combatant Owner
        {
            get { return _owner; }
        }

        /// <summary>The carried definition, or null when nothing is equipped.</summary>
        public SigilDefinition Definition
        {
            get { return _definition; }
        }

        public bool HasSigil
        {
            get { return _definition != null && _definition.Kind != SigilId.None; }
        }

        public SigilId Kind
        {
            get { return _definition == null ? SigilId.None : _definition.Kind; }
        }

        /// <summary>The Silence Price: the loudest ability is locked while equipped.</summary>
        public bool LocksLoudestAbility
        {
            get { return Kind == SigilId.Silence; }
        }

        // --------------------------------- glass ---------------------------------

        /// <summary>Absorption left in the shield. Zero while the shield is broken or absent.</summary>
        public float ShieldRemaining
        {
            get { return _shieldRemaining; }
        }

        /// <summary>
        /// The shield's full strength, snapshotted when it formed. The HUD shows
        /// "remaining of this", so it must be the value the shield actually
        /// formed with rather than one recomputed from a maximum health that may
        /// have grown since.
        /// </summary>
        public float ShieldCapacity
        {
            get { return _shieldCapacity; }
        }

        public bool ShieldActive
        {
            get { return Kind == SigilId.Glass && _shieldRemaining > 0f; }
        }

        /// <summary>Seconds until a broken shield reforms. Zero while held.</summary>
        public float ShieldReformRemaining
        {
            get { return _reformRemaining; }
        }

        // -------------------------------- hunger ---------------------------------

        /// <summary>Seconds since the bearer last landed a hit. The famine clock.</summary>
        public float SecondsSinceLandedHit
        {
            get { return _secondsSinceLandedHit; }
        }

        /// <summary>True while the famine is actually applying to the bearer.</summary>
        public bool IsFamineActive
        {
            get { return _famine != null && _owner.Statuses.Contains(_famine); }
        }

        // ------------------------------- lifecycle --------------------------------

        /// <summary>
        /// Takes up a Sigil, at full strength and with its clock at zero.
        /// Replacing a Sigil clears the previous one's state first, so a stale
        /// famine or a half-broken shield cannot leak across a swap.
        /// </summary>
        public void Equip(SigilDefinition definition)
        {
            if (definition == null || definition.Kind == SigilId.None)
            {
                Unequip();
                return;
            }

            Unequip();

            _definition = definition;
            _secondsSinceLandedHit = 0f;

            if (definition.Kind == SigilId.Glass)
            {
                _shieldCapacity = ComputeShieldCapacity();
                _shieldRemaining = _shieldCapacity;
                _reformRemaining = 0f;
            }
        }

        /// <summary>Puts the Sigil down and erases everything it was doing.</summary>
        public void Unequip()
        {
            EndFamine();

            _definition = null;
            _shieldRemaining = 0f;
            _shieldCapacity = 0f;
            _reformRemaining = 0f;
            _secondsSinceLandedHit = 0f;
            _hasShatter = false;
            _hasAcquire = false;
        }

        /// <summary>Re-takes the same Sigil in a clean state. Used when an encounter resets.</summary>
        public void Reset()
        {
            SigilDefinition definition = _definition;

            Unequip();

            if (definition != null)
            {
                Equip(definition);
            }
        }

        /// <summary>
        /// Advances the time-based Prices. Called once per fixed step on every
        /// combatant that carries a Sigil, so a Price advances with the same
        /// clock as the fight it belongs to.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || !HasSigil || !_owner.IsAlive)
            {
                return;
            }

            switch (Kind)
            {
                case SigilId.Glass:
                    TickGlass(deltaTime);
                    break;

                case SigilId.Hunger:
                    TickHunger(deltaTime);
                    break;
            }
        }

        private void TickGlass(float deltaTime)
        {
            if (_shieldRemaining > 0f)
            {
                return;
            }

            _reformRemaining -= deltaTime;

            if (_reformRemaining <= 0f)
            {
                _reformRemaining = 0f;
                _shieldCapacity = ComputeShieldCapacity();
                _shieldRemaining = _shieldCapacity;
            }
        }

        private void TickHunger(float deltaTime)
        {
            _secondsSinceLandedHit += deltaTime;

            if (_secondsSinceLandedHit < SigilTuning.HungerFamineSeconds)
            {
                return;
            }

            // The famine only stops when a hit lands, so while it runs it is
            // kept alive by re-applying whenever the last application is gone.
            // Re-forming it this way - rather than one endless status - means a
            // cleanse or a reset genuinely ends it.
            if (_famine == null || !_owner.Statuses.Contains(_famine))
            {
                float perSecond = _owner.Vitals.MaxHealth * SigilTuning.HungerFamineHealthFractionPerSecond;

                _famine = _owner.Statuses.Apply(StatusEffect.Dot(
                    StatusKind.Starving,
                    DamageType.Ghasaq,
                    perSecond * SigilTuning.HungerFamineTickSeconds,
                    SigilTuning.HungerFamineStatusSeconds,
                    SigilTuning.HungerFamineTickSeconds,
                    _owner,
                    maxStacks: 1));
            }
        }

        // --------------------------------- hooks -----------------------------------

        /// <summary>
        /// The Lantern's new verb opens: the bearer is intangible for the blink's
        /// window. Called when the dash is committed to, so the window covers
        /// the reposition itself rather than only what follows it.
        /// </summary>
        public void NotifyDashStarted()
        {
            if (Kind != SigilId.Lantern || !_owner.IsAlive)
            {
                return;
            }

            _owner.Vitals.GrantInvulnerability(SigilTuning.LanternInvulnerabilitySeconds);
        }

        /// <summary>
        /// The blink arrived, so the Lantern's Price comes due: every hostile
        /// inside the radius acquires the bearer. The request is left for the
        /// encounter, which is the layer that can see the other combatants.
        /// </summary>
        public void NotifyDashLanded()
        {
            if (Kind != SigilId.Lantern || !_owner.IsAlive)
            {
                return;
            }

            _hasAcquire = true;
            _acquireRadius = SigilTuning.LanternPriceAcquireRadius;
            _acquireSeconds = SigilTuning.LanternPriceAcquireSeconds;
        }

        /// <summary>
        /// A damaging blow of the bearer's actually landed. The Hunger Sigil
        /// feeds on it and its famine ends; the other Sigils are unmoved.
        /// </summary>
        public void NotifyHitLanded(float appliedDamage)
        {
            if (!HasSigil || appliedDamage <= 0f)
            {
                return;
            }

            if (Kind != SigilId.Hunger || !_owner.IsAlive)
            {
                return;
            }

            _owner.Vitals.Heal(appliedDamage * SigilTuning.HungerHealFraction);
            _secondsSinceLandedHit = 0f;
            EndFamine();
        }

        /// <summary>
        /// The bearer's blow killed. The Ash Price takes its share of the
        /// overkill straight into the bearer; the burst that goes with it is
        /// resolved by the attacker's strike, which is where the target list
        /// lives.
        /// </summary>
        public void NotifyKill(Combatant victim, float overkill)
        {
            if (!HasSigil || overkill <= 0f || Kind != SigilId.Ash || !_owner.IsAlive)
            {
                return;
            }

            // Deliberately unmitigated: this is a Price, not an attack. Armour
            // does not bargain with it.
            _owner.Vitals.ApplyDamage(overkill * SigilTuning.AshOverkillBiteFraction, victim);
        }

        /// <summary>
        /// Takes a share of an incoming blow into the shield. Returns false when
        /// nothing was absorbed; otherwise <paramref name="absorbedResult"/>
        /// carries the reduced blow.
        /// </summary>
        public bool TryAbsorb(in DamageResult incoming, out DamageResult absorbedResult)
        {
            absorbedResult = incoming;

            if (!ShieldActive || incoming.Applied <= 0f)
            {
                return false;
            }

            float absorbed = incoming.Applied * SigilTuning.GlassShieldAbsorbFraction;
            if (absorbed > _shieldRemaining)
            {
                absorbed = _shieldRemaining;
            }

            if (absorbed <= 0f)
            {
                return false;
            }

            _shieldRemaining -= absorbed;

            absorbedResult = new DamageResult(
                incoming.Raw,
                incoming.Mitigated,
                incoming.Applied - absorbed,
                incoming.Critical,
                incoming.ReductionFraction);

            if (_shieldRemaining <= 0f)
            {
                BreakShield();
            }

            return true;
        }

        /// <summary>The shatter request left by a broken shield, if one is waiting.</summary>
        public bool TryTakeShatter(out float damage, out float radius)
        {
            damage = _shatterDamage;
            radius = _shatterRadius;

            if (!_hasShatter)
            {
                return false;
            }

            _hasShatter = false;
            _shatterDamage = 0f;
            _shatterRadius = 0f;
            return true;
        }

        /// <summary>The acquisition request left by a blink, if one is waiting.</summary>
        public bool TryTakeAcquire(out float radius, out float seconds)
        {
            radius = _acquireRadius;
            seconds = _acquireSeconds;

            if (!_hasAcquire)
            {
                return false;
            }

            _hasAcquire = false;
            _acquireRadius = 0f;
            _acquireSeconds = 0f;
            return true;
        }

        // -------------------------------- internals --------------------------------

        private float ComputeShieldCapacity()
        {
            return _owner.Vitals.MaxHealth * SigilTuning.GlassShieldHealthFraction;
        }

        /// <summary>
        /// The shield is gone: the bearer is exposed, and the blades go outward.
        /// The burst itself needs the world's combatants, so it is recorded as a
        /// request for the encounter to finish.
        /// </summary>
        private void BreakShield()
        {
            _shieldRemaining = 0f;
            _reformRemaining = SigilTuning.GlassReformSeconds;

            _hasShatter = true;
            _shatterRadius = SigilTuning.GlassShatterRadius;
            _shatterDamage = _owner.Stats.Get(StatId.AttackPower) * SigilTuning.GlassShatterDamageFraction;

            if (_owner.IsAlive)
            {
                // The exposure is the Marked shape: for its duration the bearer
                // takes more from everything, not just from what broke the shield.
                _owner.Statuses.Apply(StatusEffect.Modifier(
                    StatusKind.Marked,
                    SigilTuning.GlassExposedDamageTakenBonus,
                    SigilTuning.GlassExposedSeconds,
                    _owner));
            }
        }

        private void EndFamine()
        {
            if (_famine == null)
            {
                return;
            }

            _owner.Statuses.Remove(_famine);
            _famine = null;
        }
    }
}
