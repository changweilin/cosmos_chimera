using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Combat
{
    /// <summary>
    /// Wild territory radius per temperament (<c>Data/balance.json#ai.territory_mm</c>). REQ: combat.md#野生領域
    /// (D0-E2, 2026-11-10); the radii themselves are provisional (Q-C35).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class TerritoryTuning
    {
        /// <summary>Creates validated radii.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= each radius &lt; Encounter.DisengageRadiusM (in mm); a radius at or beyond the disengage
        ///      distance would re-engage the moment a fight ends.
        /// Post: RadiusMm returns the matching argument. Side effects: allocates this instance.
        /// </remarks>
        public TerritoryTuning(int timidMm, int cautiousMm, int normalMm, int territorialMm, int fierceMm, int apexMm)
        {
            const int limitMm = (int)(Encounter.DisengageRadiusM * 1000f);
            Contract.Requires(timidMm >= 0 && timidMm < limitMm, "territory_mm.timid must be 0..disengage");
            Contract.Requires(cautiousMm >= 0 && cautiousMm < limitMm, "territory_mm.cautious must be 0..disengage");
            Contract.Requires(normalMm >= 0 && normalMm < limitMm, "territory_mm.normal must be 0..disengage");
            Contract.Requires(territorialMm >= 0 && territorialMm < limitMm, "territory_mm.territorial must be 0..disengage");
            Contract.Requires(fierceMm >= 0 && fierceMm < limitMm, "territory_mm.fierce must be 0..disengage");
            Contract.Requires(apexMm >= 0 && apexMm < limitMm, "territory_mm.apex must be 0..disengage");
            _radiusMm = new int[6];
            _radiusMm[(int)Temperament.Normal] = normalMm;
            _radiusMm[(int)Temperament.Timid] = timidMm;
            _radiusMm[(int)Temperament.Fierce] = fierceMm;
            _radiusMm[(int)Temperament.Cautious] = cautiousMm;
            _radiusMm[(int)Temperament.Territorial] = territorialMm;
            _radiusMm[(int)Temperament.Apex] = apexMm;
        }

        // Indexed by the Temperament value (Normal 0, Timid 1, Fierce 2, Cautious 3, Territorial 4, Apex 5).
        private readonly int[] _radiusMm;

        /// <summary>Territory radius in mm for <paramref name="temperament"/>.</summary>
        /// <remarks>Pre: temperament defined. Post: >= 0. Side effects: none.</remarks>
        public int RadiusMm(Temperament temperament)
        {
            Contract.Requires(temperament >= Temperament.Normal && temperament <= Temperament.Apex, "undefined Temperament");
            return _radiusMm[(int)temperament];
        }
    }

    /// <summary>
    /// Idle wandering of wild units around their spawn point (<c>Data/balance.json#ai.wander_*</c>). REQ: combat.md#遊蕩
    /// (D0-W1, 2026-11-10); the values themselves are provisional (Q-D2).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class WanderTuning
    {
        /// <summary>Creates validated wander tunables.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= each radius &lt; Encounter.DisengageRadiusM (in mm), so a wanderer never strays past the distance
        ///      at which a fight would already have ended; 0 &lt;= pauseMinMs &lt;= pauseMaxMs; 1 &lt;= speedPermille &lt;= 1000.
        /// Post: RadiusMm returns the matching argument; PauseMinMs/PauseMaxMs/SpeedPermille as given.
        /// Side effects: allocates this instance.
        /// </remarks>
        public WanderTuning(int timidMm, int cautiousMm, int normalMm, int territorialMm, int fierceMm, int apexMm,
            int pauseMinMs, int pauseMaxMs, int speedPermille)
        {
            const int limitMm = (int)(Encounter.DisengageRadiusM * 1000f);
            Contract.Requires(timidMm >= 0 && timidMm < limitMm, "wander_mm.timid must be 0..disengage");
            Contract.Requires(cautiousMm >= 0 && cautiousMm < limitMm, "wander_mm.cautious must be 0..disengage");
            Contract.Requires(normalMm >= 0 && normalMm < limitMm, "wander_mm.normal must be 0..disengage");
            Contract.Requires(territorialMm >= 0 && territorialMm < limitMm, "wander_mm.territorial must be 0..disengage");
            Contract.Requires(fierceMm >= 0 && fierceMm < limitMm, "wander_mm.fierce must be 0..disengage");
            Contract.Requires(apexMm >= 0 && apexMm < limitMm, "wander_mm.apex must be 0..disengage");
            Contract.Requires(pauseMinMs >= 0 && pauseMaxMs >= pauseMinMs, "wander pause must satisfy 0 <= min <= max");
            Contract.Requires(speedPermille >= 1 && speedPermille <= 1000, "wander_speed_permille must be 1..1000");
            _radiusMm = new int[6];
            _radiusMm[(int)Temperament.Normal] = normalMm;
            _radiusMm[(int)Temperament.Timid] = timidMm;
            _radiusMm[(int)Temperament.Fierce] = fierceMm;
            _radiusMm[(int)Temperament.Cautious] = cautiousMm;
            _radiusMm[(int)Temperament.Territorial] = territorialMm;
            _radiusMm[(int)Temperament.Apex] = apexMm;
            PauseMinMs = pauseMinMs;
            PauseMaxMs = pauseMaxMs;
            SpeedPermille = speedPermille;
        }

        // Indexed by the Temperament value, as in TerritoryTuning.
        private readonly int[] _radiusMm;

        /// <summary>Shortest idle pause (ms) between two wander legs.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int PauseMinMs { get; }

        /// <summary>Longest idle pause (ms) between two wander legs (inclusive).</summary>
        /// <remarks>Pre: none. Post: >= PauseMinMs. Side effects: none.</remarks>
        public int PauseMaxMs { get; }

        /// <summary>Wander walking speed as permille of the unit's combat move speed.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int SpeedPermille { get; }

        /// <summary>Wander radius in mm around the spawn point for <paramref name="temperament"/>.</summary>
        /// <remarks>Pre: temperament defined. Post: >= 0; 0 means the unit holds its spawn point. Side effects: none.</remarks>
        public int RadiusMm(Temperament temperament)
        {
            Contract.Requires(temperament >= Temperament.Normal && temperament <= Temperament.Apex, "undefined Temperament");
            return _radiusMm[(int)temperament];
        }
    }

    /// <summary>
    /// Body radii that keep characters and creatures from passing through each other (<c>Data/balance.json#ai.body_mm</c>).
    /// REQ: combat.md#實體碰撞 (D0-W2, 2026-11-10); the values themselves are provisional (Q-D2).
    /// </summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class CollisionTuning
    {
        // Twice the widest wander radius would already be absurd for a body; this only rejects unit mix-ups (m vs mm).
        private const int MaxBodyMm = 10000;

        /// <summary>Creates validated body radii.</summary>
        /// <remarks>
        /// Pre: every radius 1..10000 mm.
        /// Post: BodyMm returns the matching argument; CharacterMm == characterMm.
        /// Side effects: allocates this instance.
        /// </remarks>
        public CollisionTuning(int xsMm, int sMm, int mMm, int lMm, int xlMm, int characterMm)
        {
            Contract.Requires(xsMm >= 1 && xsMm <= MaxBodyMm, "body_mm.xs must be 1..10000");
            Contract.Requires(sMm >= 1 && sMm <= MaxBodyMm, "body_mm.s must be 1..10000");
            Contract.Requires(mMm >= 1 && mMm <= MaxBodyMm, "body_mm.m must be 1..10000");
            Contract.Requires(lMm >= 1 && lMm <= MaxBodyMm, "body_mm.l must be 1..10000");
            Contract.Requires(xlMm >= 1 && xlMm <= MaxBodyMm, "body_mm.xl must be 1..10000");
            Contract.Requires(characterMm >= 1 && characterMm <= MaxBodyMm, "body_mm.character must be 1..10000");
            _bodyMm = new int[5];
            _bodyMm[(int)CreatureSizeClass.XS] = xsMm;
            _bodyMm[(int)CreatureSizeClass.S] = sMm;
            _bodyMm[(int)CreatureSizeClass.M] = mMm;
            _bodyMm[(int)CreatureSizeClass.L] = lMm;
            _bodyMm[(int)CreatureSizeClass.XL] = xlMm;
            CharacterMm = characterMm;
        }

        // Indexed by the CreatureSizeClass value (XS == 4, see its remarks).
        private readonly int[] _bodyMm;

        /// <summary>Body radius (mm) of the player character.</summary>
        /// <remarks>Pre: none. Post: 1..10000. Side effects: none.</remarks>
        public int CharacterMm { get; }

        /// <summary>Body radius (mm) of a creature of <paramref name="sizeClass"/> (before the boss scale).</summary>
        /// <remarks>Pre: sizeClass defined. Post: 1..10000. Side effects: none.</remarks>
        public int BodyMm(CreatureSizeClass sizeClass)
        {
            Contract.Requires(sizeClass >= CreatureSizeClass.S && sizeClass <= CreatureSizeClass.XS,
                "undefined CreatureSizeClass");
            return _bodyMm[(int)sizeClass];
        }
    }

    /// <summary>All combat tunables parsed from <c>Data/balance.json</c> (the single source; the importer copies the text).</summary>
    /// <remarks>
    /// Pre: build through <see cref="FromJson"/>. Post: every member non-null and validated by its own constructor.
    /// Side effects: none after construction.
    /// </remarks>
    public sealed class CombatBalance
    {
        private CombatBalance(DerivedStats stats, HitVolumeDefaults hit, EffectRulesConfig effects, AiTuning ai,
            int switchCooldownMs, PlayerVitalsConfig player, int captureBasePercent, int captureBossPercent,
            TerritoryTuning territory, WanderTuning wander, CollisionTuning collision)
        {
            Collision = collision;
            Territory = territory;
            Wander = wander;
            Stats = stats;
            Hit = hit;
            Effects = effects;
            Ai = ai;
            SwitchCooldownMs = switchCooldownMs;
            Player = player;
            CaptureBasePercent = captureBasePercent;
            CaptureBossPercent = captureBossPercent;
        }

        /// <summary>Wild territory radii by temperament.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TerritoryTuning Territory { get; }

        /// <summary>Idle wandering of wild units.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public WanderTuning Wander { get; }

        /// <summary>Body radii for character/creature separation.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CollisionTuning Collision { get; }

        /// <summary>Derived-stat model.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public DerivedStats Stats { get; }

        /// <summary>Global hit-volume dimensions.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public HitVolumeDefaults Hit { get; }

        /// <summary>Effect multiplier clamps and field tick.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public EffectRulesConfig Effects { get; }

        /// <summary>AI stand-off ratios and leash.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public AiTuning Ai { get; }

        /// <summary>Switch cooldown in ms (team.switch_cooldown_ms).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int SwitchCooldownMs { get; }

        /// <summary>Player-character HP and roll timings.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public PlayerVitalsConfig Player { get; }

        /// <summary>Capture chance for a knocked-out wild creature, percent (capture.base_percent).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureBasePercent { get; }

        /// <summary>Capture chance for a knocked-out boss, percent (capture.boss_percent).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureBossPercent { get; }

        /// <summary>Parses balance.json text.</summary>
        /// <remarks>
        /// Pre: json is the content of Data/balance.json (all sections present; values within each config's Pre).
        /// Post: a validated balance; missing sections fail with ContractException rather than defaulting to 0.
        /// Side effects: allocates.
        /// </remarks>
        public static CombatBalance FromJson(string json)
        {
            Contract.Requires(!string.IsNullOrEmpty(json), "balance json must not be empty");
            Dto d = JsonUtility.FromJson<Dto>(json);
            Contract.Requires(d != null && d.hp != null && d.melee_volume != null && d.ranged_distance != null &&
                d.cd != null && d.speed != null && d.evasion != null && d.hit != null && d.hit_volume != null &&
                d.team != null && d.field != null && d.effects != null && d.player != null && d.ai != null && d.ai.territory_mm != null &&
                d.ai.wander_mm != null && d.ai.body_mm != null &&
                d.capture != null, "balance json is missing a section");
            Contract.Requires(d.capture.base_percent >= 0 && d.capture.base_percent <= 100 &&
                d.capture.boss_percent >= 0 && d.capture.boss_percent <= 100, "capture percents must be 0..100");

            var coefficients = new DerivedStatsCoefficients(d.model_version, d.stat_effective_cap,
                d.hp.@base, d.hp.gain, d.hp.half_stat,
                d.melee_volume.gain_permille, d.melee_volume.half_stat,
                d.ranged_distance.gain_permille, d.ranged_distance.half_stat, d.ranged_distance.cap_mm,
                d.cd.min_permille, d.cd.half_stat, d.cd.floor_ms,
                d.speed.base_mm_s, d.speed.gain_mm_s, d.speed.half_stat,
                d.evasion.cap_permille, d.evasion.half_stat, d.hit.floor_permille);
            HitVolumeDto h = d.hit_volume;
            var hit = new HitVolumeDefaults(h.height_mm, h.front_width_mm, h.sweep_angle_deg, h.cone_angle_deg,
                h.line_width_mm, h.arc_landing_radius_mm, h.field_radius_mm, h.target_cast_mid_mm, h.target_cast_long_mm);
            var effects = new EffectRulesConfig(d.effects.multiplier_min_percent, d.effects.multiplier_max_percent,
                d.field.tick_ms);
            var ai = new AiTuning(d.ai.keep_distance_permille, d.ai.close_in_permille, d.ai.hold_permille,
                d.ai.follow_leash_mm);
            var player = new PlayerVitalsConfig(d.player.max_hp, d.player.roll_duration_ms, d.player.roll_invulnerable_ms,
                d.player.roll_cooldown_ms);
            Contract.Requires(d.team.switch_cooldown_ms >= 0, "switch cooldown must be >= 0");
            var territory = new TerritoryTuning(
                d.ai.territory_mm.timid,
                d.ai.territory_mm.cautious,
                d.ai.territory_mm.normal,
                d.ai.territory_mm.territorial,
                d.ai.territory_mm.fierce,
                d.ai.territory_mm.apex);
            // JsonUtility leaves an absent int at 0; a 0 speed is rejected by WanderTuning, so a missing key fails loudly.
            var wander = new WanderTuning(
                d.ai.wander_mm.timid,
                d.ai.wander_mm.cautious,
                d.ai.wander_mm.normal,
                d.ai.wander_mm.territorial,
                d.ai.wander_mm.fierce,
                d.ai.wander_mm.apex,
                d.ai.wander_pause_min_ms,
                d.ai.wander_pause_max_ms,
                d.ai.wander_speed_permille);
            // An absent body_mm key parses as 0 and is rejected by CollisionTuning.
            BodyDto b = d.ai.body_mm;
            var collision = new CollisionTuning(b.xs, b.s, b.m, b.l, b.xl, b.character);
            return new CombatBalance(new DerivedStats(coefficients), hit, effects, ai, d.team.switch_cooldown_ms, player,
                d.capture.base_percent, d.capture.boss_percent, territory, wander, collision);
        }

        // Field names mirror balance.json keys because JsonUtility maps by name.
#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Dto
        {
            public int model_version;
            public int stat_effective_cap;
            public HpDto hp;
            public GainDto melee_volume;
            public RangedDto ranged_distance;
            public CdDto cd;
            public SpeedDto speed;
            public EvasionDto evasion;
            public HitDto hit;
            public HitVolumeDto hit_volume;
            public TeamDto team;
            public FieldDto field;
            public EffectsDto effects;
            public PlayerDto player;
            public AiDto ai;
            public CaptureDto capture;
        }

        [Serializable] private sealed class HpDto { public int @base; public int gain; public int half_stat; }
        [Serializable] private sealed class GainDto { public int gain_permille; public int half_stat; }
        [Serializable] private sealed class RangedDto { public int gain_permille; public int half_stat; public int cap_mm; }
        [Serializable] private sealed class CdDto { public int min_permille; public int half_stat; public int floor_ms; }
        [Serializable] private sealed class SpeedDto { public int base_mm_s; public int gain_mm_s; public int half_stat; }
        [Serializable] private sealed class EvasionDto { public int cap_permille; public int half_stat; }
        [Serializable] private sealed class HitDto { public int floor_permille; }
        [Serializable] private sealed class TeamDto { public int switch_cooldown_ms; }
        [Serializable] private sealed class FieldDto { public int tick_ms; }
        [Serializable] private sealed class EffectsDto { public int multiplier_min_percent; public int multiplier_max_percent; }
        [Serializable] private sealed class CaptureDto { public int base_percent; public int boss_percent; }

        [Serializable] private sealed class HitVolumeDto
        {
            public int height_mm;
            public int front_width_mm;
            public int sweep_angle_deg;
            public int cone_angle_deg;
            public int line_width_mm;
            public int arc_landing_radius_mm;
            public int field_radius_mm;
            public int target_cast_mid_mm;
            public int target_cast_long_mm;
        }

        [Serializable] private sealed class PlayerDto
        {
            public int max_hp;
            public int roll_duration_ms;
            public int roll_invulnerable_ms;
            public int roll_cooldown_ms;
        }

        [Serializable] private sealed class AiDto
        {
            public int keep_distance_permille;
            public int close_in_permille;
            public int hold_permille;
            public int follow_leash_mm;
            public TerritoryDto territory_mm;
            public TerritoryDto wander_mm;
            public int wander_pause_min_ms;
            public int wander_pause_max_ms;
            public int wander_speed_permille;
            public BodyDto body_mm;
        }

        [Serializable] private sealed class BodyDto
        {
            public int xs;
            public int s;
            public int m;
            public int l;
            public int xl;
            public int character;
        }

        [Serializable] private sealed class TerritoryDto
        {
            public int timid;
            public int cautious;
            public int normal;
            public int territorial;
            public int fierce;
            public int apex;
        }
#pragma warning restore 0649, IDE1006
    }
}
