using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Combat
{
    /// <summary>Why a team command was accepted or refused; every refusal is a player-facing reason for the HUD.</summary>
    /// <remarks>Player input can legally ask for any of these, so they are results, not contract violations.</remarks>
    public enum TeamResult
    {
        /// <summary>Accepted and applied.</summary>
        Ok = 0,
        /// <summary>The member is not one of the run's carried mains.</summary>
        NotInRoster = 1,
        /// <summary>The member is carried but not in the deploy pool.</summary>
        NotInPool = 2,
        /// <summary>The member is already on the field.</summary>
        AlreadyActive = 3,
        /// <summary>The member is not on the field.</summary>
        NotActive = 4,
        /// <summary>Both field slots are taken.</summary>
        NoFreeSlot = 5,
        /// <summary>The member fainted in this combat and may not return until it ends.</summary>
        Retired = 6,
        /// <summary>The shared switch cooldown is still running.</summary>
        SwitchOnCooldown = 7,
        /// <summary>The pool can only be rebuilt outside combat.</summary>
        InCombat = 8,
        /// <summary>Pool size outside 1..MaxPool, or the same member listed twice.</summary>
        InvalidPool = 9,
        /// <summary>A member on the field would be dropped from the new pool.</summary>
        ActiveNotInPool = 10,
    }

    /// <summary>Simulation-time domain shared by the combat rule layer.</summary>
    /// <remarks>
    /// Pre: none. Post: constants. Side effects: none.
    /// 0..2^62 ms (~146 million years) keeps now + any Int32 duration and now − earlier inside Int64, so cooldown and
    /// i-frame arithmetic never wraps (SOL P2-06 T3/V1: long.MaxValue − 1 + 3000 wrapped and cleared the cooldown).
    /// </remarks>
    public static class CombatTime
    {
        /// <summary>Largest accepted simulation time in ms.</summary>
        /// <remarks>Pre: none. Post: constant 2^62. Side effects: none.</remarks>
        public const long MaxMs = 1L << 62;

        /// <summary>Asserts 0 &lt;= nowMs &lt;= MaxMs and nowMs >= the caller's watermark.</summary>
        /// <remarks>
        /// Pre: none. Post: returns normally iff the conditions hold (Editor/Development).
        /// Side effects: throws ContractException on violation.
        /// </remarks>
        public static void Require(long nowMs, long watermarkMs)
        {
            Contract.Requires(nowMs >= 0 && nowMs <= MaxMs, "nowMs must be in 0..CombatTime.MaxMs");
            Contract.Requires(nowMs >= watermarkMs, "time went backwards");
        }
    }

    /// <summary>
    /// Rule layer for the carried team: 6 mains, a deploy pool of 4, at most 2 on the field, shared switch cooldown,
    /// and per-combat retirement on faint.
    /// REQ: docs/design/combat.md#召喚與輪替, docs/design/creatures.md (Q-C4); P2-06 rules confirmed 2026-11-06.
    /// </summary>
    /// <remarks>
    /// Members are opaque non-negative ids owned by the caller (creature instances). Time is integer ms supplied by
    /// the simulation so host and peers replay identically; this class never reads a clock.
    /// Time watermark: every command (Summon / Recall / Switch), accepted or refused, records its nowMs; commands and
    /// queries taking nowMs require nowMs >= that watermark. Queries never move it. "State unchanged" on a refusal
    /// means team state (pool, field, retirement, cooldown) — the watermark still advances.
    /// Invariant: Active ⊆ Pool ⊆ Roster; |Roster| 1..6, |Pool| 0..4, |Active| 0..2; Active ∩ Retired = ∅;
    ///            InCombat implies |Pool| >= 1. Exposed lists are read-only views, so callers cannot break it.
    /// </remarks>
    public sealed class TeamState
    {
        /// <summary>Most mains a run may carry.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxRoster = 6;

        /// <summary>Most mains in the deploy pool.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxPool = 4;

        /// <summary>Most creatures on the field at once, per player.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxActive = 2;

        private readonly List<int> _roster;
        private readonly List<int> _pool = new List<int>(MaxPool);
        private readonly List<int> _active = new List<int>(MaxActive);
        private readonly ReadOnlyCollection<int> _rosterView;
        private readonly ReadOnlyCollection<int> _poolView;
        private readonly ReadOnlyCollection<int> _activeView;
        private readonly HashSet<int> _retired = new HashSet<int>();
        private readonly int _switchCooldownMs;
        private bool _cooldownStarted;
        private long _cooldownStartMs;
        private long _lastNowMs;

        /// <summary>Creates a team with a fixed roster, an empty pool, nobody on the field and no combat running.</summary>
        /// <remarks>
        /// Pre: roster != null; 1 &lt;= roster.Count &lt;= MaxRoster; ids unique and >= 0;
        ///      switchCooldownMs >= 0 (Data/balance.json#team.switch_cooldown_ms).
        /// Post: Roster equals roster in order; Pool and Active empty; !InCombat; cooldown not running; watermark 0.
        /// Side effects: copies roster.
        /// </remarks>
        public TeamState(IReadOnlyList<int> roster, int switchCooldownMs)
        {
            Contract.Requires(roster != null, "roster is null");
            Contract.Requires(roster.Count >= 1 && roster.Count <= MaxRoster, "roster size must be 1..6");
            Contract.Requires(switchCooldownMs >= 0, "switchCooldownMs must be >= 0");
            _roster = new List<int>(roster);
            var seen = new HashSet<int>();
            foreach (int id in _roster)
            {
                Contract.Requires(id >= 0, "member ids must be >= 0");
                Contract.Requires(seen.Add(id), "roster ids must be unique");
            }

            _rosterView = _roster.AsReadOnly();
            _poolView = _pool.AsReadOnly();
            _activeView = _active.AsReadOnly();
            _switchCooldownMs = switchCooldownMs;
        }

        /// <summary>The run's carried mains, fixed at construction (chosen in the lobby).</summary>
        /// <remarks>Pre: none. Post: 1..MaxRoster unique ids; read-only view. Side effects: none.</remarks>
        public IReadOnlyList<int> Roster => _rosterView;

        /// <summary>Current deploy pool in the order it was set.</summary>
        /// <remarks>Pre: none. Post: 0..MaxPool ids, all in Roster; live read-only view. Side effects: none.</remarks>
        public IReadOnlyList<int> Pool => _poolView;

        /// <summary>Members on the field, in slot order.</summary>
        /// <remarks>Pre: none. Post: 0..MaxActive ids, all in Pool, none retired; live read-only view. Side effects: none.</remarks>
        public IReadOnlyList<int> Active => _activeView;

        /// <summary>Whether a combat is running (pool locked, faints retire).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool InCombat { get; private set; }

        /// <summary>Whether the member fainted during the current combat.</summary>
        /// <remarks>Pre: none. Post: false whenever !InCombat. Side effects: none.</remarks>
        public bool IsRetired(int id) => _retired.Contains(id);

        /// <summary>Remaining shared switch cooldown at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: 0..switchCooldownMs; 0 means switch / recall / summon are not blocked by cooldown.
        /// Side effects: none (does not move the watermark).
        /// </remarks>
        public long SwitchCooldownRemainingMs(long nowMs)
        {
            CombatTime.Require(nowMs, _lastNowMs);
            return RemainingAt(nowMs);
        }

        /// <summary>
        /// True iff a combat is running and the player has nobody on the field and nobody left in the pool who
        /// can be summoned.
        /// </summary>
        /// <remarks>Pre: none. Post: implies Active is empty. Side effects: none.</remarks>
        public bool IsWipedOut
        {
            get
            {
                if (!InCombat || _active.Count > 0)
                {
                    return false;
                }

                foreach (int id in _pool)
                {
                    if (!_retired.Contains(id))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Replaces the deploy pool; only allowed outside combat.</summary>
        /// <remarks>
        /// Pre: pool != null (may be this.Pool itself).
        /// Post: Ok iff !InCombat, 1 &lt;= pool.Count &lt;= MaxPool, ids unique and all in Roster, and every Active member
        ///       is in pool; then Pool equals pool's contents at call time, in order. Refusals are checked in that
        ///       order and leave state unchanged.
        /// Side effects: mutates Pool on Ok; copies pool first so passing a live view of Pool is safe.
        /// </remarks>
        public TeamResult SetPool(IReadOnlyList<int> pool)
        {
            Contract.Requires(pool != null, "pool is null");
            if (InCombat)
            {
                return TeamResult.InCombat;
            }

            // Snapshot before validating: SetPool(t.Pool) would otherwise be cleared by _pool.Clear() (SOL P2-06 T2).
            var candidate = new List<int>(pool);
            if (candidate.Count < 1 || candidate.Count > MaxPool)
            {
                return TeamResult.InvalidPool;
            }

            // Two passes so a pool that is both duplicated and foreign reports InvalidPool, matching the Post order.
            var seen = new HashSet<int>();
            foreach (int id in candidate)
            {
                if (!seen.Add(id))
                {
                    return TeamResult.InvalidPool;
                }
            }

            foreach (int id in candidate)
            {
                if (!_roster.Contains(id))
                {
                    return TeamResult.NotInRoster;
                }
            }

            foreach (int id in _active)
            {
                if (!seen.Contains(id))
                {
                    return TeamResult.ActiveNotInPool;
                }
            }

            _pool.Clear();
            _pool.AddRange(candidate);
            return TeamResult.Ok;
        }

        /// <summary>Starts a combat: locks the pool and makes faints retire members until <see cref="EndCombat"/>.</summary>
        /// <remarks>
        /// Pre: !InCombat; Pool.Count >= 1 (P2-06: a combat needs a chosen 1–4 pool).
        /// Post: InCombat; no member retired; Active and cooldown untouched.
        /// Side effects: mutates combat flag.
        /// </remarks>
        public void BeginCombat()
        {
            Contract.Requires(!InCombat, "combat already running");
            Contract.Requires(_pool.Count >= 1, "choose a deploy pool before combat");
            InCombat = true;
        }

        /// <summary>Ends the combat: unlocks the pool and clears per-combat retirement.</summary>
        /// <remarks>
        /// Pre: InCombat.
        /// Post: !InCombat; no member retired. Active and the cooldown are untouched (cooldown is time-based).
        ///       HP is not this class's concern: whoever owns creature HP decides whether a fainted member may be
        ///       summoned in the next combat.
        /// Side effects: mutates combat flag and retired set.
        /// </remarks>
        public void EndCombat()
        {
            Contract.Requires(InCombat, "no combat running");
            InCombat = false;
            _retired.Clear();
        }

        /// <summary>Puts a pool member onto a free field slot (opening summon or refilling after a faint).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: Ok iff id in Pool, not Active, not retired, cooldown not running and Active.Count &lt; MaxActive;
        ///       then id is appended to Active. Refusals are checked in exactly that order (NotInRoster, NotInPool,
        ///       AlreadyActive, Retired, SwitchOnCooldown, NoFreeSlot) and leave team state unchanged.
        ///       Summoning never starts the cooldown, so an opening summon or a refill after a faint is free, but it
        ///       still waits out a cooldown started by a switch or recall.
        /// Side effects: mutates Active on Ok; advances the watermark to nowMs.
        /// </remarks>
        public TeamResult Summon(int id, long nowMs)
        {
            TeamResult refusal = CheckCanEnter(id, nowMs);
            if (refusal != TeamResult.Ok)
            {
                return refusal;
            }

            if (_active.Count >= MaxActive)
            {
                return TeamResult.NoFreeSlot;
            }

            _active.Add(id);
            return TeamResult.Ok;
        }

        /// <summary>Voluntarily returns a field member to the pool.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: Ok iff id is Active and cooldown not running; then id leaves Active and the cooldown restarts at nowMs.
        ///       Refusals (NotActive, then SwitchOnCooldown) leave team state unchanged.
        /// Side effects: mutates Active and cooldown on Ok; advances the watermark to nowMs.
        /// </remarks>
        public TeamResult Recall(int id, long nowMs)
        {
            AdvanceTime(nowMs);
            if (!_active.Contains(id))
            {
                return TeamResult.NotActive;
            }

            // Recall must cost the cooldown too: otherwise recall + summon is a free switch (user decision 2026-11-06).
            if (RemainingAt(nowMs) > 0)
            {
                return TeamResult.SwitchOnCooldown;
            }

            _active.Remove(id);
            StartCooldown(nowMs);
            return TeamResult.Ok;
        }

        /// <summary>Swaps a field member for a pool member in one step, keeping the outgoing slot position.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= the time watermark.
        /// Post: Ok iff outId is Active and inId passes every Summon check except the free-slot check;
        ///       then inId takes outId's place in Active and the cooldown restarts at nowMs.
        ///       NotActive (outId) is reported before inId refusals. Refusals leave team state unchanged.
        /// Side effects: mutates Active and cooldown on Ok; advances the watermark to nowMs.
        /// </remarks>
        public TeamResult Switch(int outId, int inId, long nowMs)
        {
            AdvanceTime(nowMs);
            int slot = _active.IndexOf(outId);
            if (slot < 0)
            {
                return TeamResult.NotActive;
            }

            TeamResult refusal = CheckCanEnter(inId, nowMs);
            if (refusal != TeamResult.Ok)
            {
                return refusal;
            }

            _active[slot] = inId;
            StartCooldown(nowMs);
            return TeamResult.Ok;
        }

        /// <summary>Removes a fainted field member; it may not return during this combat.</summary>
        /// <remarks>
        /// Pre: InCombat; id is Active (the HP owner reports each faint once, for a member it knows is on the field).
        /// Post: id not Active; IsRetired(id). Cooldown untouched, so the refill summon is free unless a cooldown runs.
        /// Side effects: mutates Active and retired set.
        /// </remarks>
        public void Faint(int id)
        {
            Contract.Requires(InCombat, "faint outside combat has no defined rule yet");
            Contract.Requires(_active.Contains(id), "only a field member can faint");
            _active.Remove(id);
            _retired.Add(id);
        }

        private TeamResult CheckCanEnter(int id, long nowMs)
        {
            AdvanceTime(nowMs);
            if (!_roster.Contains(id))
            {
                return TeamResult.NotInRoster;
            }

            if (!_pool.Contains(id))
            {
                return TeamResult.NotInPool;
            }

            if (_active.Contains(id))
            {
                return TeamResult.AlreadyActive;
            }

            if (_retired.Contains(id))
            {
                return TeamResult.Retired;
            }

            return RemainingAt(nowMs) > 0 ? TeamResult.SwitchOnCooldown : TeamResult.Ok;
        }

        /// <summary>Clears per-combat retirement for <paramref name="memberId"/> after a revive (Q-C36).</summary>
        /// <remarks>
        /// Pre: memberId is in Roster.
        /// Post: result is true iff memberId was retired; memberId is no longer retired.
        /// Side effects: mutates _retired.
        /// </remarks>
        public bool TryClearRetired(int memberId)
        {
            Contract.Requires(_roster.Contains(memberId), "memberId must be in Roster");
            return _retired.Remove(memberId);
        }

        // Elapsed-based instead of a stored end time: with nowMs in 0..CombatTime.MaxMs the subtraction cannot wrap.
        private long RemainingAt(long nowMs)
        {
            if (!_cooldownStarted)
            {
                return 0;
            }

            long elapsed = nowMs - _cooldownStartMs;
            return elapsed >= _switchCooldownMs ? 0 : _switchCooldownMs - elapsed;
        }

        private void StartCooldown(long nowMs)
        {
            _cooldownStarted = true;
            _cooldownStartMs = nowMs;
        }

        private void AdvanceTime(long nowMs)
        {
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
        }
    }
}
