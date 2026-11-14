using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Unity.Mathematics;

namespace Cosmimera.Net.Api
{
    /// <summary>Team arrangement for an arena match. REQ: docs/design/multiplayer.md (Q-N2).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ArenaMode
    {
        /// <summary>2–4 participants, each on their own team (1v1, 3-way, or 4-way FFA).</summary>
        FreeForAll = 0,

        /// <summary>4 participants split into 2 teams of 2 (Team 0 vs Team 1).</summary>
        Teams2v2 = 1,
    }

    /// <summary>Lifecycle phase of an arena match.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ArenaPhase
    {
        /// <summary>Created, waiting for <c>Start</c>.</summary>
        Lobby = 0,

        /// <summary>Combat running; ticks and player commands accepted.</summary>
        Active = 1,

        /// <summary>At most one team remains; winner settled.</summary>
        Finished = 2,
    }

    /// <summary>Arena simulation event kinds for HUD, VFX, and match log.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ArenaEventKind
    {
        /// <summary>A creature cast a move.</summary>
        Cast = 0,

        /// <summary>A hostile move hit a creature or player.</summary>
        Hit = 1,

        /// <summary>A hostile move touched but the evasion roll missed.</summary>
        Miss = 2,

        /// <summary>A self/ally support move landed.</summary>
        Support = 3,

        /// <summary>A creature reached 0 HP and retired.</summary>
        UnitFainted = 4,

        /// <summary>A player character reached 0 HP.</summary>
        PlayerDowned = 5,

        /// <summary>A creature was summoned onto the field.</summary>
        UnitSummoned = 6,

        /// <summary>A creature was recalled from the field.</summary>
        UnitRecalled = 7,

        /// <summary>Two creatures swapped field and pool slots.</summary>
        UnitSwitched = 8,

        /// <summary>A participant lost all creatures or was downed.</summary>
        ParticipantEliminated = 9,

        /// <summary>Match ended; <see cref="ArenaEvent.Amount"/> holds winning team index (-1 on draw).</summary>
        MatchFinished = 10,
    }

    /// <summary>Immutable event emitted during <c>ArenaMatch.Start</c>, commands, or <c>Tick</c>.</summary>
    /// <remarks>Pre: created by ArenaMatch. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArenaEvent
    {
        /// <summary>Constructs an arena event.</summary>
        /// <remarks>Pre: kind defined. Post: properties equal arguments. Side effects: none.</remarks>
        public ArenaEvent(ArenaEventKind kind, int participantId, int sourceUnitId, int targetId, int slot, int amount)
        {
            Contract.Requires(kind >= ArenaEventKind.Cast && kind <= ArenaEventKind.MatchFinished, "undefined ArenaEventKind");
            Kind = kind;
            ParticipantId = participantId;
            SourceUnitId = sourceUnitId;
            TargetId = targetId;
            Slot = slot;
            Amount = amount;
        }

        /// <summary>Event kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArenaEventKind Kind { get; }

        /// <summary>Owning or affected participant id, or -1 for match-wide events.</summary>
        /// <remarks>Pre: none. Post: >= -1. Side effects: none.</remarks>
        public int ParticipantId { get; }

        /// <summary>Acting unit id, or -1 if not applicable.</summary>
        /// <remarks>Pre: none. Post: >= -1. Side effects: none.</remarks>
        public int SourceUnitId { get; }

        /// <summary>Target unit id, or -1 if not applicable.</summary>
        /// <remarks>Pre: none. Post: >= -1. Side effects: none.</remarks>
        public int TargetId { get; }

        /// <summary>Move slot or roster index, or -1 if not applicable.</summary>
        /// <remarks>Pre: none. Post: >= -1. Side effects: none.</remarks>
        public int Slot { get; }

        /// <summary>Damage, heal, or winning team index (MatchFinished).</summary>
        /// <remarks>Pre: none. Post: any int. Side effects: none.</remarks>
        public int Amount { get; }
    }

    /// <summary>Specification for one carried creature in an arena participant's roster.</summary>
    /// <remarks>Pre: build through constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArenaCreatureSpec
    {
        /// <summary>Creates a creature spec.</summary>
        /// <remarks>
        /// Pre: def != null; def.SignatureMove != null; 1 &lt;= level &lt;= 100.
        /// Post: properties equal arguments. Side effects: none.
        /// </remarks>
        public ArenaCreatureSpec(CreatureDef def, MoveDef partnerMove, int level, bool overloadUnlocked = false)
        {
            Contract.Requires(def != null && def.SignatureMove != null, "creature needs a signature move");
            Contract.Requires(level >= 1 && level <= 100, "level must be 1..100");
            Def = def;
            PartnerMove = partnerMove;
            Level = level;
            OverloadUnlocked = overloadUnlocked;
        }

        /// <summary>Creature species definition.</summary>
        /// <remarks>Pre: none. Post: non-null when constructed. Side effects: none.</remarks>
        public CreatureDef Def { get; }

        /// <summary>Optional partner move in slot 1.</summary>
        /// <remarks>Pre: none. Post: may be null. Side effects: none.</remarks>
        public MoveDef PartnerMove { get; }

        /// <summary>Combat level (1..100).</summary>
        /// <remarks>Pre: none. Post: 1..100 when constructed. Side effects: none.</remarks>
        public int Level { get; }

        /// <summary>Whether adult overload is unlocked for this creature.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool OverloadUnlocked { get; }
    }

    /// <summary>Setup for one participant (human or AI) entering an arena match.</summary>
    /// <remarks>Pre: build through constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArenaParticipantConfig
    {
        /// <summary>Creates a participant config.</summary>
        /// <remarks>
        /// Pre: participantId >= 0; teamIndex >= 0; roster != null and 1 &lt;= roster.Count &lt;= 6;
        ///      initialPoolIndices != null and 1 &lt;= initialPoolIndices.Count &lt;= 4;
        ///      every index in initialPoolIndices is unique and in 0..roster.Count - 1.
        /// Post: properties equal arguments. Side effects: none.
        /// </remarks>
        public ArenaParticipantConfig(int participantId, int teamIndex, IReadOnlyList<ArenaCreatureSpec> roster,
            IReadOnlyList<int> initialPoolIndices, bool autoRefill = true)
        {
            Contract.Requires(participantId >= 0, "participantId must be >= 0");
            Contract.Requires(teamIndex >= 0, "teamIndex must be >= 0");
            Contract.Requires(roster != null && roster.Count >= 1 && roster.Count <= 6, "roster size must be 1..6");
            Contract.Requires(initialPoolIndices != null && initialPoolIndices.Count >= 1 && initialPoolIndices.Count <= 4,
                "initialPoolIndices size must be 1..4");
            var seen = new HashSet<int>();
            for (int i = 0; i < initialPoolIndices.Count; i++)
            {
                int idx = initialPoolIndices[i];
                Contract.Requires(idx >= 0 && idx < roster.Count, "pool index out of roster range");
                Contract.Requires(seen.Add(idx), "pool indices must be unique");
            }

            ParticipantId = participantId;
            TeamIndex = teamIndex;
            Roster = roster;
            InitialPoolIndices = initialPoolIndices;
            AutoRefill = autoRefill;
        }

        /// <summary>Unique participant identifier.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int ParticipantId { get; }

        /// <summary>Team index; in FreeForAll each participant has a distinct team.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int TeamIndex { get; }

        /// <summary>Carried creatures (1..6).</summary>
        /// <remarks>Pre: none. Post: 1..6 elements. Side effects: none.</remarks>
        public IReadOnlyList<ArenaCreatureSpec> Roster { get; }

        /// <summary>Roster indices forming the initial deploy pool (1..4).</summary>
        /// <remarks>Pre: none. Post: 1..4 unique indices. Side effects: none.</remarks>
        public IReadOnlyList<int> InitialPoolIndices { get; }

        /// <summary>When true, fainted active slots automatically summon the next available pool member.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool AutoRefill { get; }
    }

    /// <summary>Match-level configuration for an arena.</summary>
    /// <remarks>Pre: build through constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArenaConfig
    {
        /// <summary>Creates an arena config.</summary>
        /// <remarks>
        /// Pre: mode defined; centerDirection unit length; arenaRadiusM >= 5f.
        /// Post: properties equal arguments. Side effects: none.
        /// </remarks>
        public ArenaConfig(ArenaMode mode, float3 centerDirection, float arenaRadiusM, ulong seed)
        {
            Contract.Requires(mode >= ArenaMode.FreeForAll && mode <= ArenaMode.Teams2v2, "undefined ArenaMode");
            Contract.Requires(math.abs(math.lengthsq(centerDirection) - 1f) <= 2e-3f, "centerDirection must be unit length");
            Contract.Requires(float.IsFinite(arenaRadiusM) && arenaRadiusM >= 5f, "arenaRadiusM must be >= 5 m");
            Mode = mode;
            CenterDirection = math.normalize(centerDirection);
            ArenaRadiusM = arenaRadiusM;
            Seed = seed;
        }

        /// <summary>FFA or 2v2.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArenaMode Mode { get; }

        /// <summary>Unit normal at the center of the arena on the planet sphere.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 CenterDirection { get; }

        /// <summary>Geodesic radius of the circular arena boundary in meters.</summary>
        /// <remarks>Pre: none. Post: >= 5. Side effects: none.</remarks>
        public float ArenaRadiusM { get; }

        /// <summary>Deterministic RNG seed for host-authoritative combat rolls.</summary>
        /// <remarks>Pre: none. Post: any ulong. Side effects: none.</remarks>
        public ulong Seed { get; }
    }
}
