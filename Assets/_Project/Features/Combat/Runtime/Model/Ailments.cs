using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>Cross-element status ailment reactions. REQ: docs/design/status-ailments.md#3.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum AilmentReactionKind
    {
        None = 0,
        SteamExtinguish = 1,
        ToxicIgnition = 2,
        HighVoltageConduction = 3,
        FlashFreeze = 4,
        Shatter = 5,
        MeltBurst = 6,
        HeavySlam = 7,
        AirflowSpread = 8,
        LightShadowClash = 9,
    }

    /// <summary>Constants for the ten elemental status ailments. REQ: docs/design/status-ailments.md.</summary>
    /// <remarks>Pre: none. Post: immutable constants. Side effects: none.</remarks>
    public static class AilmentTuning
    {
        /// <summary>Number of ailments (one per Element).</summary>
        /// <remarks>Pre: none. Post: 10. Side effects: none.</remarks>
        public const int Count = 10;

        /// <summary>Base gauge threshold to trigger an ailment.</summary>
        /// <remarks>Pre: none. Post: 100. Side effects: none.</remarks>
        public const int BaseThreshold = 100;

        /// <summary>Threshold multiplier for BOSS units.</summary>
        /// <remarks>Pre: none. Post: 2. Side effects: none.</remarks>
        public const int BossThresholdMultiplier = 2;

        /// <summary>Diminishing-returns threshold increase per trigger, in permille (+35%).</summary>
        /// <remarks>Pre: none. Post: 350. Side effects: none.</remarks>
        public const int DrStepPermille = 350;

        /// <summary>Diminishing-returns threshold cap, in permille (2.4x).</summary>
        /// <remarks>Pre: none. Post: 2400. Side effects: none.</remarks>
        public const int DrCapPermille = 2400;

        /// <summary>Delay after last buildup before gauge begins decaying, in ms.</summary>
        /// <remarks>Pre: none. Post: 3000. Side effects: none.</remarks>
        public const int DecayDelayMs = 3000;

        /// <summary>Gauge decay per 1000 ms step once <see cref="DecayDelayMs"/> has elapsed.</summary>
        /// <remarks>Pre: none. Post: 15. Side effects: none.</remarks>
        public const int DecayPerSec = 15;

        /// <summary>Fixed integer step interval for DoT and decay ticks, in ms.</summary>
        /// <remarks>Pre: none. Post: 1000. Side effects: none.</remarks>
        public const int TickStepMs = 1000;

        /// <summary>Shock pulse interval, in ms.</summary>
        /// <remarks>Pre: none. Post: 1500. Side effects: none.</remarks>
        public const int ShockPulseMs = 1500;

        /// <summary>Minimum windup duration (ms) that a Shock pulse interrupts.</summary>
        /// <remarks>Pre: none. Post: 600. Side effects: none.</remarks>
        public const int ShockInterruptWindupMs = 600;

        /// <summary>Burn duration removed per dodge roll, in ms.</summary>
        /// <remarks>Pre: none. Post: 2000. Side effects: none.</remarks>
        public const int BurnRollExtinguishMs = 2000;

        /// <summary>Distance moved (m) under Sunder to trigger a laceration hit.</summary>
        /// <remarks>Pre: none. Post: 2f. Side effects: none.</remarks>
        public const float SunderMoveTriggerM = 2f;

        /// <summary>Spread / default Shock chain radius, in m.</summary>
        /// <remarks>Pre: none. Post: 4f. Side effects: none.</remarks>
        public const float DefaultAilmentRadiusM = 4f;

        /// <summary>Expanded Shock chain radius when Soaked or Thunderstorm is active, in m.</summary>
        /// <remarks>Pre: none. Post: 7f. Side effects: none.</remarks>
        public const float ExpandedShockChainRadiusM = 7f;

        /// <summary>Steam Extinguish zone radius (m) and duration (ms).</summary>
        /// <remarks>Pre: none. Post: 3f / 3000. Side effects: none.</remarks>
        public const float SteamRadiusM = 3f;

        /// <summary>Steam Extinguish zone duration, in ms.</summary>
        /// <remarks>Pre: none. Post: 3000. Side effects: none.</remarks>
        public const int SteamDurationMs = 3000;

        /// <summary>Base durations (ms) for each ailment.</summary>
        /// <remarks>Pre: ailment defined. Post: > 0. Side effects: none.</remarks>
        public static int BaseDurationMs(Ailment ailment)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            switch (ailment)
            {
                case Ailment.Sunder: return 6000;
                case Ailment.Toxin: return 7000;
                case Ailment.Soaked: return 8000;
                case Ailment.Burn: return 6000;
                case Ailment.Weighted: return 6000;
                case Ailment.Frostbite: return 5000;
                case Ailment.Turbulent: return 5000;
                case Ailment.Shock: return 6000;
                case Ailment.Dazzle: return 5000;
                case Ailment.Eclipse: return 6000;
                default: return 5000;
            }
        }

        /// <summary>Maps each ailment to its corresponding element.</summary>
        /// <remarks>Pre: ailment defined. Post: matching Element. Side effects: none.</remarks>
        public static Element ElementOf(Ailment ailment)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            return (Element)(int)ailment;
        }
    }

    /// <summary>Result of applying buildup or an elemental hit to <see cref="AilmentBook"/>.</summary>
    /// <remarks>Pre: obtained from AilmentBook. Post: immutable. Side effects: none.</remarks>
    public readonly struct AilmentActionResult
    {
        internal AilmentActionResult(bool triggered, Ailment triggeredAilment, AilmentReactionKind reaction,
            int bonusDamage, int staggerDamage, bool spreadTriggered, Element bonusElement, Polarity bonusPolarity)
        {
            Triggered = triggered;
            TriggeredAilment = triggeredAilment;
            Reaction = reaction;
            BonusDamage = bonusDamage;
            StaggerDamage = staggerDamage;
            SpreadTriggered = spreadTriggered;
            BonusElement = bonusElement;
            BonusPolarity = bonusPolarity;
        }

        /// <summary>Whether an ailment crossed its threshold (or entered Frostbite phase 1).</summary>
        public bool Triggered { get; }

        /// <summary>The ailment that triggered, if <see cref="Triggered"/>.</summary>
        public Ailment TriggeredAilment { get; }

        /// <summary>Any cross-element reaction that fired.</summary>
        public AilmentReactionKind Reaction { get; }

        /// <summary>Immediate bonus damage from the reaction (e.g. Toxic Ignition, Shatter, Melt, Heavy Slam, Clash).</summary>
        public int BonusDamage { get; }

        /// <summary>Immediate stagger gauge damage from the reaction (Shatter / Heavy Slam).</summary>
        public int StaggerDamage { get; }

        /// <summary>True when Turbulent triggered Airflow Spread.</summary>
        public bool SpreadTriggered { get; }

        /// <summary>Element of <see cref="BonusDamage"/>.</summary>
        public Element BonusElement { get; }

        /// <summary>Polarity of <see cref="BonusDamage"/>.</summary>
        public Polarity BonusPolarity { get; }
    }

    /// <summary>Result of one fixed-step tick of <see cref="AilmentBook.Step"/>.</summary>
    /// <remarks>Pre: obtained from AilmentBook.Step. Post: immutable. Side effects: none.</remarks>
    public readonly struct AilmentStepResult
    {
        internal AilmentStepResult(int toxinDamage, int burnDamage, int shockDamage, bool shockPulse, bool shockChainExpanded)
        {
            ToxinDamage = toxinDamage;
            BurnDamage = burnDamage;
            ShockDamage = shockDamage;
            ShockPulse = shockPulse;
            ShockChainExpanded = shockChainExpanded;
        }

        /// <summary>Toxin DoT damage this tick.</summary>
        public int ToxinDamage { get; }

        /// <summary>Burn DoT damage this tick.</summary>
        public int BurnDamage { get; }

        /// <summary>Shock pulse damage this tick.</summary>
        public int ShockDamage { get; }

        /// <summary>True when a Shock 1500 ms pulse occurred this tick.</summary>
        public bool ShockPulse { get; }

        /// <summary>True when Soaked was active during the Shock pulse (7 m chain radius).</summary>
        public bool ShockChainExpanded { get; }

        /// <summary>Total DoT damage this tick.</summary>
        public int TotalDamage => ToxinDamage + BurnDamage + ShockDamage;
    }

    /// <summary>
    /// Per-unit buildup gauges, diminishing returns, active ailment windows, and integer-stepped DoT timers.
    /// REQ: docs/design/status-ailments.md.
    /// </summary>
    /// <remarks>
    /// Pre: constructed per combatant; all timestamps in CombatTime and monotonic.
    /// Post: deterministic, zero-allocation after construction, consumes no RNG.
    /// Side effects: mutates internal gauge/window arrays.
    /// </remarks>
    public sealed class AilmentBook
    {
        private readonly int[] _gauge = new int[AilmentTuning.Count];
        private readonly long[] _lastHitMs = new long[AilmentTuning.Count];
        private readonly int[] _triggerCount = new int[AilmentTuning.Count];
        private readonly long[] _activeUntilMs = new long[AilmentTuning.Count];

        private bool _isBoss;
        private bool _isFrozen;
        private long _lastDecayStepMs;
        private long _nextToxinTickMs;
        private long _nextBurnTickMs;
        private long _nextShockTickMs;
        private long _watermarkMs;
        private float _sunderMoveAccumM;

        // Frozen caster stats for DoTs (Burn, Shock, Sunder).
        private int _burnMastery = 100;
        private int _burnAttack = 100;
        private int _burnLevelPpm = LevelCurve.PpmOne;
        private int _shockMastery = 100;
        private int _shockAttack = 100;
        private int _shockLevelPpm = LevelCurve.PpmOne;
        private int _sunderMastery = 100;
        private int _sunderAttack = 100;
        private int _sunderLevelPpm = LevelCurve.PpmOne;

        /// <summary>Creates an ailment book for a normal or BOSS unit.</summary>
        /// <remarks>Pre: none. Post: all gauges 0, no active ailments. Side effects: allocates fixed arrays.</remarks>
        public AilmentBook(bool isBoss = false)
        {
            _isBoss = isBoss;
        }

        /// <summary>Configures whether this unit uses BOSS ailment protections (2x threshold, no full Freeze stun, Toxin cap).</summary>
        /// <remarks>Pre: none. Post: IsBoss == isBoss. Side effects: none.</remarks>
        public bool IsBoss
        {
            get => _isBoss;
            internal set => _isBoss = value;
        }

        /// <summary>True while <paramref name="ailment"/> is active at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: ailment defined; nowMs &gt;= 0. Post: none. Side effects: none.</remarks>
        public bool IsActive(Ailment ailment, long nowMs)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            Contract.Requires(nowMs >= 0, "nowMs must be >= 0");
            return nowMs < _activeUntilMs[(int)ailment];
        }

        /// <summary>True while the unit is in full-stun Freeze (non-BOSS only).</summary>
        /// <remarks>Pre: nowMs &gt;= 0. Post: false for BOSS units. Side effects: none.</remarks>
        public bool IsFrozen(long nowMs)
        {
            Contract.Requires(nowMs >= 0, "nowMs must be >= 0");
            return !_isBoss && _isFrozen && nowMs < _activeUntilMs[(int)Ailment.Frostbite];
        }

        /// <summary>Current buildup gauge for <paramref name="ailment"/>.</summary>
        /// <remarks>Pre: ailment defined. Post: &gt;= 0. Side effects: none.</remarks>
        public int Gauge(Ailment ailment)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            return _gauge[(int)ailment];
        }

        /// <summary>Current trigger threshold for <paramref name="ailment"/>, including BOSS and DR scaling.</summary>
        /// <remarks>Pre: ailment defined. Post: &gt;= 100. Side effects: none.</remarks>
        public int Threshold(Ailment ailment)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            int drPermille = Math.Min(AilmentTuning.DrCapPermille, 1000 + _triggerCount[(int)ailment] * AilmentTuning.DrStepPermille);
            int baseT = _isBoss ? AilmentTuning.BaseThreshold * AilmentTuning.BossThresholdMultiplier : AilmentTuning.BaseThreshold;
            return (baseT * drPermille + 500) / 1000;
        }

        /// <summary>Clears all gauges, active ailments, and DR counters (called when combat ends).</summary>
        /// <remarks>Pre: none. Post: all gauges, triggers, and windows 0. Side effects: resets arrays.</remarks>
        public void Clear()
        {
            Array.Clear(_gauge, 0, _gauge.Length);
            Array.Clear(_lastHitMs, 0, _lastHitMs.Length);
            Array.Clear(_triggerCount, 0, _triggerCount.Length);
            Array.Clear(_activeUntilMs, 0, _activeUntilMs.Length);
            _isFrozen = false;
            _lastDecayStepMs = 0;
            _nextToxinTickMs = 0;
            _nextBurnTickMs = 0;
            _nextShockTickMs = 0;
            _watermarkMs = 0;
            _sunderMoveAccumM = 0f;
        }

        /// <summary>Applies buildup for <paramref name="ailment"/> and resolves threshold triggers and reactions.</summary>
        /// <remarks>
        /// Pre: ailment defined; baseBuildup &gt;= 1; nowMs &gt;= watermark.
        /// Post: gauge updated; if threshold reached, ailment activated and DR incremented; reactions resolved.
        /// Side effects: mutates gauge, active window, DR counter, and frozen DoT stats.
        /// </remarks>
        public AilmentActionResult ApplyBuildup(
            Ailment ailment,
            int baseBuildup,
            int targetElementResist,
            int targetAilmentResistDelta,
            bool isBlocked,
            bool isPerfectBlockOrInvulnerable,
            int weatherBuildupPermille,
            int weatherDurationPermille,
            int attackerMastery,
            int attackerAttack,
            int attackerLevelPpm,
            int targetMaxHp,
            long nowMs)
        {
            Contract.Requires(ailment >= Ailment.Sunder && ailment <= Ailment.Eclipse, "undefined Ailment");
            Contract.Requires(baseBuildup >= 1, "baseBuildup must be >= 1");
            Contract.Requires(nowMs >= _watermarkMs, "nowMs must not move backwards");
            _watermarkMs = nowMs;

            if (isPerfectBlockOrInvulnerable)
            {
                return default;
            }

            int idx = (int)ailment;
            int combinedResist = Math.Clamp(targetElementResist + targetAilmentResistDelta,
                -DamageModel.ResistClamp, DamageModel.ResistClamp);
            // 2^(-(R - 100) / 50) using DamageModel's deterministic fixed-point table.
            long buildup = DamageModel.Compute(baseBuildup, DamageModel.NeutralStat, combinedResist);
            buildup = (buildup * Math.Max(100, weatherBuildupPermille) + 500) / 1000;

            // Soaked catalysts: +50% buildup for Shock and Frostbite.
            if (IsActive(Ailment.Soaked, nowMs) && (ailment == Ailment.Shock || ailment == Ailment.Frostbite))
            {
                buildup = (buildup * 1500 + 500) / 1000;
            }

            if (isBlocked)
            {
                buildup /= 2;
            }

            if (buildup <= 0)
            {
                return default;
            }

            _gauge[idx] += (int)buildup;
            _lastHitMs[idx] = nowMs;

            int threshold = Threshold(ailment);
            bool triggered = false;
            bool spreadTriggered = false;
            AilmentReactionKind reaction = AilmentReactionKind.None;
            int bonusDamage = 0;
            int staggerDamage = 0;
            Element bonusElement = AilmentTuning.ElementOf(ailment);
            Polarity bonusPolarity = Polarity.Yin;

            // Frostbite phase 1 at >= 50% gauge (when not already frozen).
            if (ailment == Ailment.Frostbite && _gauge[idx] * 2 >= threshold && _gauge[idx] < threshold && !IsFrozen(nowMs))
            {
                _isFrozen = false;
                _activeUntilMs[idx] = Math.Max(_activeUntilMs[idx], nowMs + AilmentTuning.BaseDurationMs(Ailment.Frostbite));
                triggered = true;
            }

            if (_gauge[idx] >= threshold)
            {
                _gauge[idx] = 0;
                _triggerCount[idx]++;
                triggered = true;

                int durationMs = AilmentTuning.BaseDurationMs(ailment);
                if (ailment == Ailment.Frostbite)
                {
                    if (_isBoss)
                    {
                        _isFrozen = false;
                        durationMs = 3000;
                    }
                    else
                    {
                        _isFrozen = true;
                        durationMs = 1800;
                    }
                }

                durationMs = (int)(((long)durationMs * Math.Max(100, weatherDurationPermille) + 500) / 1000);
                _activeUntilMs[idx] = nowMs + durationMs;

                if (ailment == Ailment.Toxin)
                {
                    _nextToxinTickMs = nowMs + AilmentTuning.TickStepMs;
                }
                else if (ailment == Ailment.Burn)
                {
                    _burnMastery = attackerMastery;
                    _burnAttack = attackerAttack;
                    _burnLevelPpm = attackerLevelPpm;
                    _nextBurnTickMs = nowMs + AilmentTuning.TickStepMs;
                }
                else if (ailment == Ailment.Shock)
                {
                    _shockMastery = attackerMastery;
                    _shockAttack = attackerAttack;
                    _shockLevelPpm = attackerLevelPpm;
                    _nextShockTickMs = nowMs + AilmentTuning.ShockPulseMs;
                }
                else if (ailment == Ailment.Sunder)
                {
                    _sunderMastery = attackerMastery;
                    _sunderAttack = attackerAttack;
                    _sunderLevelPpm = attackerLevelPpm;
                    _sunderMoveAccumM = 0f;
                }
                else if (ailment == Ailment.Turbulent)
                {
                    spreadTriggered = true;
                    reaction = AilmentReactionKind.AirflowSpread;
                }

                // Cross-element reactions on trigger.
                if (ailment == Ailment.Soaked && IsActive(Ailment.Burn, nowMs))
                {
                    _activeUntilMs[(int)Ailment.Burn] = 0;
                    reaction = AilmentReactionKind.SteamExtinguish;
                }
                else if (ailment == Ailment.Burn && IsActive(Ailment.Soaked, nowMs))
                {
                    _activeUntilMs[(int)Ailment.Burn] = 0;
                    reaction = AilmentReactionKind.SteamExtinguish;
                }
                else if (ailment == Ailment.Burn && IsActive(Ailment.Toxin, nowMs))
                {
                    long remainingMs = Math.Max(0, _activeUntilMs[(int)Ailment.Toxin] - nowMs);
                    int remainingTicks = (int)((remainingMs + 999) / 1000);
                    int perTick = ToxinTickDamage(targetMaxHp, targetElementResist);
                    bonusDamage = (perTick * remainingTicks * 13 + 5) / 10;
                    bonusElement = Element.Fire;
                    bonusPolarity = Polarity.Yang;
                    _activeUntilMs[(int)Ailment.Toxin] = 0;
                    _activeUntilMs[(int)Ailment.Burn] = nowMs + 2000;
                    reaction = AilmentReactionKind.ToxicIgnition;
                }
                else if ((ailment == Ailment.Dazzle && IsActive(Ailment.Eclipse, nowMs))
                      || (ailment == Ailment.Eclipse && IsActive(Ailment.Dazzle, nowMs)))
                {
                    _activeUntilMs[(int)Ailment.Dazzle] = 0;
                    _activeUntilMs[(int)Ailment.Eclipse] = 0;
                    int effectiveHp = _isBoss ? Math.Max(1, targetMaxHp / AilmentTuning.BossThresholdMultiplier) : targetMaxHp;
                    bonusDamage = Math.Max(1, effectiveHp * 8 / 100);
                    bonusElement = ailment == Ailment.Dazzle ? Element.Light : Element.Shadow;
                    bonusPolarity = Polarity.Yang;
                    reaction = AilmentReactionKind.LightShadowClash;
                }
            }

            return new AilmentActionResult(triggered, ailment, reaction, bonusDamage, staggerDamage, spreadTriggered,
                bonusElement, bonusPolarity);
        }

        /// <summary>Resolves hit-triggered cross-element reactions for any elemental damage hit.</summary>
        /// <remarks>
        /// Pre: element defined; nowMs &gt;= watermark.
        /// Post: clears consumed ailments and returns reaction damage/stagger if a reaction fired.
        /// Side effects: mutates active windows on reaction.
        /// </remarks>
        public AilmentActionResult OnElementalHit(Element element, int targetResist, int targetMaxHp,
            int attackerMastery, int attackerAttack, int attackerLevelPpm, long nowMs)
        {
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            Contract.Requires(nowMs >= _watermarkMs, "nowMs must not move backwards");
            _watermarkMs = nowMs;

            // Freeze + Earth/Metal -> Shatter.
            if (IsFrozen(nowMs) && (element == Element.Earth || element == Element.Metal))
            {
                _activeUntilMs[(int)Ailment.Frostbite] = 0;
                _isFrozen = false;
                int dmg = Math.Max(1, DamageModel.Compute(65, attackerMastery, targetResist, attackerAttack, 100, 0, attackerLevelPpm));
                int stagger = element == Element.Earth ? Math.Max(1, targetMaxHp * 15 / 100) : Math.Max(1, targetMaxHp * 8 / 100);
                return new AilmentActionResult(false, default, AilmentReactionKind.Shatter, dmg, stagger, false,
                    element, element == Element.Metal ? Polarity.Yang : Polarity.Yin);
            }

            // Freeze + Fire -> Melt Burst + 3s Soaked.
            if (IsFrozen(nowMs) && element == Element.Fire)
            {
                _activeUntilMs[(int)Ailment.Frostbite] = 0;
                _isFrozen = false;
                _activeUntilMs[(int)Ailment.Soaked] = Math.Max(_activeUntilMs[(int)Ailment.Soaked], nowMs + 3000);
                int dmg = Math.Max(1, DamageModel.Compute(55, attackerMastery, targetResist, attackerAttack, 100, 0, attackerLevelPpm));
                return new AilmentActionResult(false, default, AilmentReactionKind.MeltBurst, dmg, 0, false,
                    Element.Fire, Polarity.Yang);
            }

            // Turbulent + Earth -> Heavy Slam.
            if (IsActive(Ailment.Turbulent, nowMs) && element == Element.Earth)
            {
                _activeUntilMs[(int)Ailment.Turbulent] = 0;
                int dmg = Math.Max(1, DamageModel.Compute(50, attackerMastery, targetResist, attackerAttack, 100, 0, attackerLevelPpm));
                int stagger = Math.Max(1, targetMaxHp * 12 / 100);
                return new AilmentActionResult(false, default, AilmentReactionKind.HeavySlam, dmg, stagger, false,
                    Element.Earth, Polarity.Yin);
            }

            // Burn + Water -> Steam Extinguish.
            if (IsActive(Ailment.Burn, nowMs) && element == Element.Water)
            {
                _activeUntilMs[(int)Ailment.Burn] = 0;
                return new AilmentActionResult(false, default, AilmentReactionKind.SteamExtinguish, 0, 0, false,
                    Element.Water, Polarity.Yin);
            }

            // Toxin + Fire -> Toxic Ignition.
            if (IsActive(Ailment.Toxin, nowMs) && element == Element.Fire)
            {
                long remainingMs = Math.Max(0, _activeUntilMs[(int)Ailment.Toxin] - nowMs);
                int remainingTicks = Math.Max(1, (int)((remainingMs + 999) / 1000));
                int perTick = ToxinTickDamage(targetMaxHp, targetResist);
                int dmg = (perTick * remainingTicks * 13 + 5) / 10;
                _activeUntilMs[(int)Ailment.Toxin] = 0;
                _activeUntilMs[(int)Ailment.Burn] = Math.Max(_activeUntilMs[(int)Ailment.Burn], nowMs + 2000);
                return new AilmentActionResult(false, default, AilmentReactionKind.ToxicIgnition, dmg, 0, false,
                    Element.Fire, Polarity.Yang);
            }

            return default;
        }

        /// <summary>Handles dodge-roll counterplay: shortens Burn by 2s and triggers Sunder laceration.</summary>
        /// <remarks>Pre: nowMs &gt;= watermark. Post: returns true if Sunder laceration damage should be applied. Side effects: mutates Burn window.</remarks>
        public bool OnRoll(long nowMs)
        {
            Contract.Requires(nowMs >= _watermarkMs, "nowMs must not move backwards");
            _watermarkMs = nowMs;
            if (IsActive(Ailment.Burn, nowMs))
            {
                _activeUntilMs[(int)Ailment.Burn] = Math.Max(0, _activeUntilMs[(int)Ailment.Burn] - AilmentTuning.BurnRollExtinguishMs);
            }

            return IsActive(Ailment.Sunder, nowMs);
        }

        /// <summary>Accumulates movement distance under Sunder; returns true each time 2 m is crossed.</summary>
        /// <remarks>Pre: distanceM &gt;= 0; nowMs &gt;= watermark. Post: none. Side effects: mutates _sunderMoveAccumM.</remarks>
        public bool OnMoveDistance(float distanceM, long nowMs)
        {
            Contract.Requires(distanceM >= 0f, "distanceM must be >= 0");
            Contract.Requires(nowMs >= _watermarkMs, "nowMs must not move backwards");
            _watermarkMs = nowMs;
            if (!IsActive(Ailment.Sunder, nowMs))
            {
                _sunderMoveAccumM = 0f;
                return false;
            }

            _sunderMoveAccumM += distanceM;
            if (_sunderMoveAccumM >= AilmentTuning.SunderMoveTriggerM)
            {
                _sunderMoveAccumM -= AilmentTuning.SunderMoveTriggerM;
                return true;
            }

            return false;
        }

        /// <summary>Computes Sunder laceration damage using the frozen attacker stats and target's Metal resist / Yang defence.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int ComputeSunderDamage(int targetMetalResist, int targetYangDef)
            => Math.Max(1, DamageModel.Compute(14, _sunderMastery, targetMetalResist, _sunderAttack, targetYangDef, 0, _sunderLevelPpm));

        /// <summary>Advances fixed-step decay, Toxin DoT, Burn DoT, and Shock pulses up to <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: nowMs &gt;= watermark.
        /// Post: deterministic integer-stepped DoTs and decay resolved without RNG.
        /// Side effects: mutates gauges and DoT tick cursors.
        /// </remarks>
        public AilmentStepResult Step(long nowMs, int targetMaxHp, int woodResist, int fireResist, int thunderResist,
            int yangDef, int yinDef)
        {
            Contract.Requires(nowMs >= _watermarkMs, "nowMs must not move backwards");
            _watermarkMs = nowMs;

            // Gauge decay in 1000 ms integer steps.
            if (_lastDecayStepMs == 0)
            {
                _lastDecayStepMs = nowMs;
            }

            while (nowMs - _lastDecayStepMs >= AilmentTuning.TickStepMs)
            {
                _lastDecayStepMs += AilmentTuning.TickStepMs;
                for (int i = 0; i < AilmentTuning.Count; i++)
                {
                    if (_gauge[i] > 0 && _lastDecayStepMs - _lastHitMs[i] >= AilmentTuning.DecayDelayMs)
                    {
                        _gauge[i] = Math.Max(0, _gauge[i] - AilmentTuning.DecayPerSec);
                    }
                }
            }

            int toxinDmg = 0;
            if (IsActive(Ailment.Toxin, nowMs) && _nextToxinTickMs > 0)
            {
                while (_nextToxinTickMs <= nowMs && _nextToxinTickMs <= _activeUntilMs[(int)Ailment.Toxin])
                {
                    toxinDmg += ToxinTickDamage(targetMaxHp, woodResist);
                    _nextToxinTickMs += AilmentTuning.TickStepMs;
                }
            }

            int burnDmg = 0;
            if (IsActive(Ailment.Burn, nowMs) && _nextBurnTickMs > 0)
            {
                while (_nextBurnTickMs <= nowMs && _nextBurnTickMs <= _activeUntilMs[(int)Ailment.Burn])
                {
                    burnDmg += Math.Max(1, DamageModel.Compute(18, _burnMastery, fireResist, _burnAttack, yangDef, 0, _burnLevelPpm));
                    _nextBurnTickMs += AilmentTuning.TickStepMs;
                }
            }

            int shockDmg = 0;
            bool shockPulse = false;
            bool shockChainExpanded = false;
            if (IsActive(Ailment.Shock, nowMs) && _nextShockTickMs > 0)
            {
                while (_nextShockTickMs <= nowMs && _nextShockTickMs <= _activeUntilMs[(int)Ailment.Shock])
                {
                    shockPulse = true;
                    shockChainExpanded = IsActive(Ailment.Soaked, _nextShockTickMs);
                    shockDmg += Math.Max(1, DamageModel.Compute(14, _shockMastery, thunderResist, _shockAttack, yinDef, 0, _shockLevelPpm));
                    _nextShockTickMs += AilmentTuning.ShockPulseMs;
                }
            }

            return new AilmentStepResult(toxinDmg, burnDmg, shockDmg, shockPulse, shockChainExpanded);
        }

        /// <summary>Adds active ailment stat modifiers into <paramref name="totals"/>.</summary>
        /// <remarks>Pre: totals != null; nowMs &gt;= 0. Post: totals updated. Side effects: mutates totals.</remarks>
        public void CollectModifiers(ModifierTotals totals, long nowMs)
        {
            Contract.Requires(totals != null, "totals must not be null");
            Contract.Requires(nowMs >= 0, "nowMs must be >= 0");

            if (IsActive(Ailment.Sunder, nowMs))
            {
                totals.AddBlockReductionDelta(-25);
            }

            if (IsActive(Ailment.Soaked, nowMs))
            {
                totals.AddMoveSpeedPercent(-15);
            }

            if (IsActive(Ailment.Weighted, nowMs))
            {
                totals.AddMoveSpeedPercent(-20);
            }

            if (IsActive(Ailment.Frostbite, nowMs))
            {
                if (_isBoss && _triggerCount[(int)Ailment.Frostbite] > 0)
                {
                    totals.AddMoveSpeedPercent(-40);
                    totals.AddCdAnyPercent(40);
                }
                else if (!_isFrozen)
                {
                    totals.AddMoveSpeedPercent(-20);
                    totals.AddCdAnyPercent(20);
                }
            }

            if (IsActive(Ailment.Dazzle, nowMs))
            {
                totals.AddAccuracyDelta(-25);
            }

            if (IsActive(Ailment.Eclipse, nowMs))
            {
                totals.AddAllResistDelta(-15);
                totals.AddRangePercent(-20);
            }
        }

        private int ToxinTickDamage(int targetMaxHp, int woodResist)
        {
            int cappedMaxHp = _isBoss ? Math.Max(1, targetMaxHp / 3) : targetMaxHp;
            int baseDmg = (int)Math.Clamp((long)cappedMaxHp * 25 / 1000, 1, DamageModel.MaxPower);
            return DamageModel.Compute(baseDmg, DamageModel.NeutralStat, woodResist);
        }
    }
}
