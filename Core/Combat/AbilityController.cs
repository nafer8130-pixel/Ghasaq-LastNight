using System;
using System.Collections.Generic;
using Ghasaq.Core.Numerics;

namespace Ghasaq.Core.Combat
{
    /// <summary>Why an ability activation was refused.</summary>
    public enum AbilityFailure
    {
        None = 0,
        UnknownAbility = 1,
        OnCooldown = 2,
        NotEnoughStamina = 3,
        Busy = 4,
        Stunned = 5,
        Dead = 6,

        /// <summary>Sealed by a Sigil's Price - the Silence Sigil's defender, today.</summary>
        Locked = 7
    }

    /// <summary>Where the caster is in the commit-to-an-ability cycle.</summary>
    public enum CastPhase
    {
        /// <summary>Can start a new ability.</summary>
        Ready = 0,

        /// <summary>Committed, blow has not landed yet.</summary>
        Windup = 1,

        /// <summary>Blow has landed, still locked out.</summary>
        Recovery = 2
    }

    /// <summary>
    /// Owns one combatant's abilities: cooldown timers, the cast state machine,
    /// and the stamina cost.
    ///
    /// Deliberately does NOT apply damage. It decides *when* a blow lands and
    /// reports it; <see cref="AttackResolver"/> decides *what* it hits. That split
    /// keeps timing testable without a world, and target selection testable with
    /// no timers involved.
    ///
    /// Timers advance on a fixed order: cooldowns tick down on the caster's own
    /// cooldown-rate stat, so a hasted combatant recovers faster without any
    /// special-casing at the call site.
    /// </summary>
    public sealed class AbilityController
    {
        private readonly Combatant _self;
        private readonly AbilityDefinition[] _abilities;
        private readonly float[] _cooldownRemaining;

        /// <summary>Index of the longest wind-up. Fixed at construction; content is not mutated at runtime.</summary>
        private readonly int _loudestIndex;

        private int _castingIndex;
        private float _phaseTimer;

        public AbilityController(Combatant self, IReadOnlyList<AbilityDefinition> abilities)
        {
            if (self == null)
            {
                throw new ArgumentNullException(nameof(self));
            }

            _self = self;
            _abilities = new AbilityDefinition[abilities == null ? 0 : abilities.Count];
            for (int i = 0; i < _abilities.Length; i++)
            {
                _abilities[i] = abilities[i] ?? throw new ArgumentException("Ability list contains null.", nameof(abilities));
            }

            _cooldownRemaining = new float[_abilities.Length];
            _castingIndex = -1;
            _phaseTimer = 0f;
            Phase = CastPhase.Ready;

            // "Your loudest ability" is the one with the biggest wind-up. Ties
            // go to the earlier ability, so the choice is stable and readable
            // rather than dependent on iteration order.
            _loudestIndex = -1;
            float longest = -1f;

            for (int i = 0; i < _abilities.Length; i++)
            {
                float windup = _abilities[i].WindupSeconds;

                if (windup > longest)
                {
                    longest = windup;
                    _loudestIndex = i;
                }
            }
        }

        public int Count
        {
            get { return _abilities.Length; }
        }

        public CastPhase Phase { get; private set; }

        /// <summary>Index currently being cast, or -1.</summary>
        public int CastingIndex
        {
            get { return _castingIndex; }
        }

        /// <summary>True while committed to an ability, either winding up or recovering.</summary>
        public bool IsBusy
        {
            get { return Phase != CastPhase.Ready; }
        }

        /// <summary>Seconds left in the current phase. Zero when ready.</summary>
        public float PhaseRemaining
        {
            get { return _phaseTimer; }
        }

        public AbilityDefinition this[int index]
        {
            get { return index >= 0 && index < _abilities.Length ? _abilities[index] : null; }
        }

        public IReadOnlyList<AbilityDefinition> Abilities
        {
            get { return _abilities; }
        }

        public float CooldownRemaining(int index)
        {
            return index >= 0 && index < _cooldownRemaining.Length ? _cooldownRemaining[index] : 0f;
        }

        /// <summary>Index of the ability with the longest wind-up, or -1 when there are none.</summary>
        public int LoudestAbilityIndex
        {
            get { return _loudestIndex; }
        }

        /// <summary>
        /// Index currently sealed by the carried Sigil, or -1. The HUD reads
        /// this to mark the button, so the lock can never be invisible: the
        /// plan says a Price the player cannot see is not a Price.
        /// </summary>
        public int LockedAbilityIndex
        {
            get { return IsLocked(_loudestIndex) ? _loudestIndex : -1; }
        }

        /// <summary>True when this index is sealed by the current Sigil's Price.</summary>
        public bool IsLocked(int index)
        {
            return index >= 0
                && index == _loudestIndex
                && _self.Sigil != null
                && _self.Sigil.LocksLoudestAbility;
        }

        /// <summary>Fraction of the cooldown still to run, 1 when just used and 0 when ready.</summary>
        public float CooldownFraction(int index)
        {
            if (index < 0 || index >= _abilities.Length)
            {
                return 0f;
            }

            float total = _abilities[index].EffectiveCooldown;
            if (total <= FMath.Epsilon)
            {
                return 0f;
            }

            return FMath.Clamp01(_cooldownRemaining[index] / total);
        }

        public bool IsReady(int index)
        {
            if (index < 0 || index >= _abilities.Length || IsBusy || _self.IsStunned || !_self.IsAlive)
            {
                return false;
            }

            if (IsLocked(index))
            {
                return false;
            }

            return _cooldownRemaining[index] <= 0f;
        }

        /// <summary>
        /// Validates an activation and reports the precise reason for failure, so
        /// the HUD can tell the player whether they are out of stamina, on
        /// cooldown, or mid-swing rather than silently doing nothing.
        /// </summary>
        public AbilityFailure CanActivate(int index)
        {
            if (index < 0 || index >= _abilities.Length)
            {
                return AbilityFailure.UnknownAbility;
            }

            if (!_self.IsAlive)
            {
                return AbilityFailure.Dead;
            }

            if (IsLocked(index))
            {
                return AbilityFailure.Locked;
            }

            if (_self.IsStunned)
            {
                return AbilityFailure.Stunned;
            }

            if (IsBusy)
            {
                return AbilityFailure.Busy;
            }

            if (_cooldownRemaining[index] > 0f)
            {
                return AbilityFailure.OnCooldown;
            }

            if (_self.Vitals.Stamina < _abilities[index].StaminaCost)
            {
                return AbilityFailure.NotEnoughStamina;
            }

            return AbilityFailure.None;
        }

        /// <summary>
        /// Commits to an ability. Stamina is spent here, not when the blow lands,
        /// so a caster cannot dodge the cost by dying mid-windup.
        /// </summary>
        public bool TryActivate(int index, out AbilityFailure failure)
        {
            failure = CanActivate(index);
            if (failure != AbilityFailure.None)
            {
                return false;
            }

            AbilityDefinition ability = _abilities[index];

            if (ability.StaminaCost > 0f && !_self.Vitals.TrySpendStamina(ability.StaminaCost))
            {
                failure = AbilityFailure.NotEnoughStamina;
                return false;
            }

            _castingIndex = index;
            _phaseTimer = ability.WindupSeconds;
            Phase = CastPhase.Windup;
            _landedThisTick = -1;

            // The Lantern's blink opens its i-frame window at commitment, so
            // the window covers the reposition itself rather than only what
            // follows it. Interrupting the wind-up still spends the window: the
            // Price of carrying the light does not wait for the outcome.
            if (ability.Kind == AbilityKind.Dash)
            {
                _self.Sigil?.NotifyDashStarted();
            }

            // Using the Ghasaq leaves soot, and it is committed here for the
            // same reason: the meter answers the decision to burn, not whether
            // the blow landed.
            if (ability.UsesGhasaqPower)
            {
                _self.Soot?.NotifyGhasaqUsed();
            }

            // An ability with no windup is left in the Windup phase with a zero
            // timer, and lands on the next Tick. It is deliberately NOT landed
            // here: Tick clears the landed flag as its first action, so setting
            // it during activation would be erased before any caller could read
            // it, and an instant ability would silently never deal damage.

            if (ability.EffectiveCooldown > 0f)
            {
                _cooldownRemaining[index] = ability.EffectiveCooldown;
            }

            return true;
        }

        private int _landedThisTick = -1;

        /// <summary>
        /// Advances cooldowns and the cast state machine.
        /// Returns the index of the ability whose blow landed this tick, or -1.
        ///
        /// The return value is the signal to resolve a hit, and it fires exactly
        /// once per activation, so a caller cannot accidentally apply damage
        /// twice for one swing.
        /// </summary>
        public int Tick(float deltaTime)
        {
            _landedThisTick = -1;

            if (deltaTime > 0f)
            {
                // Cooldown recovery scales with the Haste stat, and is further
                // reduced by Chilled. A staggered caster still recovers.
                float rate = _self.Stats.Get(Stats.StatId.CooldownRate);
                if (rate < 0f)
                {
                    rate = 0f;
                }

                rate *= _self.Statuses.CooldownRateMultiplier;

                for (int i = 0; i < _cooldownRemaining.Length; i++)
                {
                    if (_cooldownRemaining[i] > 0f)
                    {
                        _cooldownRemaining[i] -= deltaTime * rate;
                        if (_cooldownRemaining[i] < 0f)
                        {
                            _cooldownRemaining[i] = 0f;
                        }
                    }
                }

                AdvancePhase(deltaTime);
            }

            return _landedThisTick;
        }

        private void AdvancePhase(float deltaTime)
        {
            if (Phase == CastPhase.Ready)
            {
                return;
            }

            _phaseTimer -= deltaTime;
            if (_phaseTimer > 0f)
            {
                return;
            }

            AbilityDefinition ability = _abilities[_castingIndex];

            if (Phase == CastPhase.Windup)
            {
                // Carry the overshoot into recovery so a long frame does not
                // shorten the caster's commitment.
                float overshoot = -_phaseTimer;
                _landedThisTick = _castingIndex;
                Phase = CastPhase.Recovery;
                _phaseTimer = ability.RecoverySeconds - overshoot;

                if (_phaseTimer <= 0f)
                {
                    EndCast();
                }

                return;
            }

            EndCast();
        }

        private void EndCast()
        {
            _castingIndex = -1;
            _phaseTimer = 0f;
            Phase = CastPhase.Ready;
        }

        /// <summary>
        /// Abandons the current cast without landing it. Used when the caster is
        /// staggered mid-windup, which is what makes interrupt abilities work.
        /// Returns true if a cast was actually interrupted.
        /// </summary>
        public bool Interrupt()
        {
            if (Phase != CastPhase.Windup)
            {
                return false;
            }

            // An interrupted ability does not refund its stamina or cooldown.
            // That cost is what makes interrupting worthwhile.
            EndCast();
            return true;
        }

        /// <summary>Clears all cooldowns. Used on respawn and encounter reset.</summary>
        public void ResetCooldowns()
        {
            for (int i = 0; i < _cooldownRemaining.Length; i++)
            {
                _cooldownRemaining[i] = 0f;
            }

            EndCast();
        }

        /// <summary>
        /// Fraction of movement speed available while casting. Zero when ready,
        /// otherwise the current ability's windup allowance.
        /// </summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                if (Phase != CastPhase.Windup || _castingIndex < 0)
                {
                    return 1f;
                }

                return FMath.Clamp01(_abilities[_castingIndex].MoveSpeedDuringWindup);
            }
        }
    }
}
