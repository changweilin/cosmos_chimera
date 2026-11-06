using Cosmimera.Core.Contracts;

namespace Cosmimera.Combat
{
    /// <summary>Why a dodge-roll request was accepted or refused.</summary>
    public enum RollResult
    {
        /// <summary>Roll started.</summary>
        Ok = 0,
        /// <summary>The previous roll's cooldown is still running.</summary>
        OnCooldown = 1,
        /// <summary>The player is at 0 HP.</summary>
        Downed = 2,
    }

    /// <summary>Player-character tuning from <c>Data/balance.json#player</c>.</summary>
    /// <remarks>Values confirmed as provisional by the user 2026-11-06 (P2-06).</remarks>
    public readonly struct PlayerVitalsConfig
    {
        /// <summary>Builds a config.</summary>
        /// <remarks>
        /// Pre: maxHp >= 1; 0 &lt;= rollInvulnerableMs &lt;= rollDurationMs &lt;= rollCooldownMs.
        ///      Invulnerability inside the roll keeps i-frames tied to the animation; cooldown >= duration stops
        ///      chained rolls from giving permanent i-frames.
        /// Post: properties return the arguments unchanged.
        /// Side effects: none.
        /// </remarks>
        public PlayerVitalsConfig(int maxHp, int rollDurationMs, int rollInvulnerableMs, int rollCooldownMs)
        {
            Contract.Requires(maxHp >= 1, "player.max_hp must be >= 1");
            Contract.Requires(rollInvulnerableMs >= 0, "player.roll_invulnerable_ms must be >= 0");
            Contract.Requires(rollInvulnerableMs <= rollDurationMs, "i-frames must fit inside the roll");
            Contract.Requires(rollDurationMs <= rollCooldownMs, "roll cooldown must cover the roll");
            MaxHp = maxHp;
            RollDurationMs = rollDurationMs;
            RollInvulnerableMs = rollInvulnerableMs;
            RollCooldownMs = rollCooldownMs;
        }

        /// <summary>Fixed player HP; not derived from creature stats.</summary>
        /// <remarks>Pre: none. Post: >= 1 when built through the constructor; 0 for default(PlayerVitalsConfig). Side effects: none.</remarks>
        public int MaxHp { get; }

        /// <summary>Length of one dodge roll.</summary>
        /// <remarks>Pre: none. Post: >= RollInvulnerableMs. Side effects: none.</remarks>
        public int RollDurationMs { get; }

        /// <summary>I-frame window measured from roll start.</summary>
        /// <remarks>Pre: none. Post: 0..RollDurationMs. Side effects: none.</remarks>
        public int RollInvulnerableMs { get; }

        /// <summary>Minimum time between roll starts.</summary>
        /// <remarks>Pre: none. Post: >= RollDurationMs. Side effects: none.</remarks>
        public int RollCooldownMs { get; }
    }

    /// <summary>
    /// Player HP and dodge-roll i-frames. The player never attacks (Q-C8); i-frames bypass the hit roll entirely.
    /// REQ: docs/design/combat.md#命中與閃避; feeds <see cref="HitResolver.Resolve"/>'s invulnerable argument.
    /// </summary>
    /// <remarks>
    /// Time is integer ms in 0..CombatTime.MaxMs supplied by the simulation (host-authoritative, replayable); the
    /// bounded domain keeps now − rollStart from wrapping (SOL P2-06 V1). Windows are half-open [start, start + length)
    /// so a hit landing exactly on the boundary tick is not invulnerable on any peer.
    /// Time watermark: TryRoll and ApplyDamage, accepted or refused, record their nowMs; every member taking nowMs
    /// requires nowMs >= that watermark. Queries never move it. "State unchanged" means HP and roll state.
    /// </remarks>
    public sealed class PlayerVitals
    {
        private readonly PlayerVitalsConfig _config;
        private long _rollStartMs;
        private bool _hasRolled;
        private long _lastNowMs;

        /// <summary>Creates a player at full HP who has never rolled.</summary>
        /// <remarks>Pre: config built through its constructor. Post: Hp == MaxHp; !IsDowned. Side effects: none.</remarks>
        public PlayerVitals(PlayerVitalsConfig config)
        {
            Contract.Requires(config.MaxHp >= 1, "config was default-constructed");
            _config = config;
            Hp = config.MaxHp;
        }

        /// <summary>Current HP.</summary>
        /// <remarks>Pre: none. Post: 0..MaxHp. Side effects: none.</remarks>
        public int Hp { get; private set; }

        /// <summary>Maximum HP.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int MaxHp => _config.MaxHp;

        /// <summary>True at 0 HP. What happens next (defeat, rescue in co-op) belongs to P3/P6.</summary>
        /// <remarks>Pre: none. Post: IsDowned iff Hp == 0. Side effects: none.</remarks>
        public bool IsDowned => Hp == 0;

        /// <summary>Starts a dodge roll.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: Downed if IsDowned (checked first, even during cooldown); else OnCooldown if a roll started within
        ///       the last RollCooldownMs; else Ok and a roll starts at nowMs. Refusals leave HP and roll state unchanged.
        /// Side effects: records the roll start on Ok; advances the watermark to nowMs.
        /// </remarks>
        public RollResult TryRoll(long nowMs)
        {
            AdvanceTime(nowMs);
            if (IsDowned)
            {
                return RollResult.Downed;
            }

            if (_hasRolled && nowMs - _rollStartMs < _config.RollCooldownMs)
            {
                return RollResult.OnCooldown;
            }

            _rollStartMs = nowMs;
            _hasRolled = true;
            return RollResult.Ok;
        }

        /// <summary>Whether the roll animation is playing at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: true iff a roll started at s with s &lt;= nowMs &lt; s + RollDurationMs.
        /// Side effects: none (does not move the watermark).
        /// </remarks>
        public bool IsRolling(long nowMs) => InWindow(nowMs, _config.RollDurationMs);

        /// <summary>Whether hits at <paramref name="nowMs"/> are ignored without a roll.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: true iff a roll started at s with s &lt;= nowMs &lt; s + RollInvulnerableMs.
        /// Side effects: none (does not move the watermark).
        /// </remarks>
        public bool IsInvulnerable(long nowMs) => InWindow(nowMs, _config.RollInvulnerableMs);

        /// <summary>Applies damage that already passed the hit test.</summary>
        /// <remarks>
        /// Pre: amount >= 0 (DamageModel output); 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: if IsInvulnerable(nowMs) or IsDowned, returns 0 and Hp is unchanged; otherwise Hp decreases by
        ///       min(amount, Hp) and that value is returned. Hp never goes below 0.
        /// Side effects: mutates Hp; advances the watermark to nowMs. The i-frame check is repeated here so a caller that skipped HitResolver
        ///       cannot damage a rolling player.
        /// </remarks>
        public int ApplyDamage(int amount, long nowMs)
        {
            Contract.Requires(amount >= 0, "damage must be >= 0");
            AdvanceTime(nowMs);
            if (IsDowned || IsInvulnerable(nowMs))
            {
                return 0;
            }

            int dealt = amount < Hp ? amount : Hp;
            Hp -= dealt;
            return dealt;
        }

        private bool InWindow(long nowMs, int lengthMs)
        {
            CombatTime.Require(nowMs, _lastNowMs);
            return _hasRolled && nowMs >= _rollStartMs && nowMs - _rollStartMs < lengthMs;
        }

        private void AdvanceTime(long nowMs)
        {
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
        }
    }
}
