using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>Player command for the main creature. REQ: docs/design/combat.md#戰鬥-ai (Q-C20).</summary>
    /// <remarks>Pre: none. Post: stable values; constructors reject undefined ones. Side effects: none.</remarks>
    public enum AiCommandKind
    {
        /// <summary>Fight by own style; nearest living enemy.</summary>
        Free = 0,
        /// <summary>Fight the commanded enemy while it lives, then fall back to Free.</summary>
        Focus = 1,
        /// <summary>Stay within the leash of the player; cast only what already reaches.</summary>
        Follow = 2,
        /// <summary>Walk to the player; never cast.</summary>
        Retreat = 3,
    }

    /// <summary>Which of the two fielded creatures this is. REQ: combat.md#戰鬥-ai (Q-C11).</summary>
    /// <remarks>Pre: none. Post: stable values; constructors reject undefined ones. Side effects: none.</remarks>
    public enum AiRole
    {
        /// <summary>Receives the player's command.</summary>
        Main = 0,
        /// <summary>Ignores commands; prefers the main creature's target.</summary>
        Support = 1,
    }

    /// <summary>Who a move is aimed at, as far as the AI is concerned.</summary>
    /// <remarks>Pre: none. Post: stable values; constructors reject undefined ones. Side effects: none.</remarks>
    public enum AiMoveTargeting
    {
        /// <summary>Any shape that tests enemies (front/line/sweep/cone/burst/arc/field).</summary>
        Hostile = 0,
        /// <summary>shape=self.</summary>
        Self = 1,
        /// <summary>shape=ally: the partner creature.</summary>
        Ally = 2,
    }

    /// <summary>A player command.</summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct AiCommand
    {
        /// <summary>Creates a command.</summary>
        /// <remarks>
        /// Pre: kind defined; targetId >= 0 when kind == Focus, otherwise targetId == CombatAi.NoTarget.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public AiCommand(AiCommandKind kind, int targetId)
        {
            Contract.Requires(kind >= AiCommandKind.Free && kind <= AiCommandKind.Retreat, "undefined AiCommandKind");
            Contract.Requires(kind == AiCommandKind.Focus ? targetId >= 0 : targetId == CombatAi.NoTarget,
                "only Focus carries a target id (>= 0); other commands use NoTarget");
            Kind = kind;
            TargetId = targetId;
        }

        /// <summary>The command.</summary>
        /// <remarks>Pre: none. Post: defined value (Free for default). Side effects: none.</remarks>
        public AiCommandKind Kind { get; }

        /// <summary>Focused enemy id, or <see cref="CombatAi.NoTarget"/>.</summary>
        /// <remarks>
        /// Pre: none. Post: for constructed values, >= 0 iff Kind == Focus. Exception: default(AiCommand) is Free with
        /// TargetId 0; Decide never reads TargetId of a non-Focus command, so default behaves as Free. Side effects: none.
        /// </remarks>
        public int TargetId { get; }
    }

    /// <summary>AI tuning from <c>Data/balance.json#ai</c>.</summary>
    /// <remarks>
    /// REQ: docs/design/derived-stats/balance-keys.md. Pre: build through the constructor. Post: immutable.
    /// Side effects: none after construction.
    /// </remarks>
    public sealed class AiTuning
    {
        /// <summary>Creates validated tuning.</summary>
        /// <remarks>
        /// Pre: 1 &lt;= keepDistancePermille, closeInPermille, holdPermille &lt;= 1000; followLeashMm >= 1.
        /// Post: properties equal the arguments. Side effects: allocates this instance (build once, not per tick).
        /// </remarks>
        public AiTuning(int keepDistancePermille, int closeInPermille, int holdPermille, int followLeashMm)
        {
            // Upper bound 1000: standing beyond the reach the AI positions for would make every move miss.
            Contract.Requires(keepDistancePermille >= 1 && keepDistancePermille <= 1000, "keepDistancePermille must be 1..1000");
            Contract.Requires(closeInPermille >= 1 && closeInPermille <= 1000, "closeInPermille must be 1..1000");
            Contract.Requires(holdPermille >= 1 && holdPermille <= 1000, "holdPermille must be 1..1000");
            Contract.Requires(followLeashMm >= 1, "followLeashMm must be positive");
            KeepDistancePermille = keepDistancePermille;
            CloseInPermille = closeInPermille;
            HoldPermille = holdPermille;
            FollowLeashMm = followLeashMm;
        }

        /// <summary>keep_distance stand-off as a fraction of the longest hostile reach.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int KeepDistancePermille { get; }

        /// <summary>charge / flank stand-off as a fraction of the shortest hostile reach.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int CloseInPermille { get; }

        /// <summary>hold stays put while the target is within this fraction of the longest hostile reach.</summary>
        /// <remarks>Pre: none. Post: 1..1000. Side effects: none.</remarks>
        public int HoldPermille { get; }

        /// <summary>Surface distance from the player beyond which Follow (and an idle creature) walks back.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int FollowLeashMm { get; }
    }

    /// <summary>An enemy as the AI sees it.</summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct AiEnemy
    {
        /// <summary>Creates an enemy view.</summary>
        /// <remarks>
        /// Pre: id >= 0. Position and forward are checked by <see cref="CombatAi.Decide"/> (they need the planet).
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public AiEnemy(int id, float3 position, float3 forward, bool alive)
        {
            Contract.Requires(id >= 0, "enemy id must be nonnegative");
            Id = id;
            Position = position;
            Forward = forward;
            IsAlive = alive;
        }

        /// <summary>Stable id, unique among the enemies passed to one Decide call.</summary>
        /// <remarks>Pre: none. Post: >= 0 when constructed. Side effects: none.</remarks>
        public int Id { get; }

        /// <summary>World position.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 Position { get; }

        /// <summary>World facing; flank stands opposite to its tangent part.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 Forward { get; }

        /// <summary>False once fainted; dead enemies are never targeted.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool IsAlive { get; }
    }

    /// <summary>AI trigger category of a move. REQ: docs/design/combat.md#戰鬥-ai (Q-C22).</summary>
    /// <remarks>Pre: none. Post: stable values; constructors reject undefined ones. Side effects: none.</remarks>
    public enum AiMoveCategory
    {
        /// <summary>Default hostile damage or legacy untagged move.</summary>
        Standard = 0,
        /// <summary>Restores HP; triggers without overflow or whenever target HP &lt; 50%, even out of combat.</summary>
        Heal = 1,
        /// <summary>Self/ally status or stat buff; triggers in combat when ready.</summary>
        Buff = 2,
        /// <summary>Hostile control/debuff move; triggers in combat within reach.</summary>
        Control = 3,
    }

    /// <summary>One of the creature's moves, reduced to what target and cast choice need.</summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct AiMoveOption
    {
        /// <summary>Creates a move option with <see cref="AiMoveCategory.Standard"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; MoveCooldowns.MaxSlots; targeting defined; reachMm >= 0, and reachMm == 0 when
        ///      targeting == Self; 0 &lt;= power &lt;= DamageModel.MaxPower.
        /// Post: properties equal the arguments; Category == Standard, HealPercent == 0. Side effects: none.
        /// </remarks>
        public AiMoveOption(int slot, AiMoveTargeting targeting, int reachMm, int power, bool ready)
            : this(slot, targeting, reachMm, power, ready, AiMoveCategory.Standard, 0)
        {
        }

        /// <summary>Creates a move option with an explicit trigger category.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; MoveCooldowns.MaxSlots; targeting and category defined; reachMm >= 0, and reachMm == 0 when
        ///      targeting == Self; 0 &lt;= power &lt;= DamageModel.MaxPower; 0 &lt;= healPercent &lt;= 100 (and &gt; 0 when category == Heal).
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public AiMoveOption(int slot, AiMoveTargeting targeting, int reachMm, int power, bool ready,
            AiMoveCategory category, int healPercent = 0)
        {
            Contract.Requires(slot >= 0 && slot < MoveCooldowns.MaxSlots, "slot must be 0..MaxSlots-1");
            Contract.Requires(targeting >= AiMoveTargeting.Hostile && targeting <= AiMoveTargeting.Ally,
                "undefined AiMoveTargeting");
            Contract.Requires(reachMm >= 0, "reachMm must be nonnegative");
            Contract.Requires(targeting != AiMoveTargeting.Self || reachMm == 0, "a self move has no reach");
            Contract.Requires(power >= 0 && power <= DamageModel.MaxPower, "power must be 0..MaxPower");
            Contract.Requires(category >= AiMoveCategory.Standard && category <= AiMoveCategory.Control,
                "undefined AiMoveCategory");
            Contract.Requires(healPercent >= 0 && healPercent <= 100, "healPercent must be 0..100");
            Contract.Requires(category != AiMoveCategory.Heal || healPercent > 0, "heal move must have healPercent > 0");
            Slot = slot;
            Targeting = targeting;
            ReachMm = reachMm;
            Power = power;
            IsReady = ready;
            Category = category;
            HealPercent = healPercent;
        }

        /// <summary>Cooldown slot, as passed to <see cref="Combatant.BeginCast"/>.</summary>
        /// <remarks>Pre: none. Post: 0..MaxSlots-1 when constructed. Side effects: none.</remarks>
        public int Slot { get; }

        /// <summary>Hostile, self or ally.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public AiMoveTargeting Targeting { get; }

        /// <summary>Largest surface distance to the target's (or ally's) center at which the move can still connect.</summary>
        /// <remarks>Pre: none. Post: >= 0; 0 for Self. Side effects: none. Build it with <see cref="CombatAi.ReachMmOf"/>.</remarks>
        public int ReachMm { get; }

        /// <summary>Move power (0 for pure effect moves); picks among hostile moves.</summary>
        /// <remarks>Pre: none. Post: 0..MaxPower. Side effects: none.</remarks>
        public int Power { get; }

        /// <summary>True when the cooldown slot is ready now (<see cref="MoveCooldowns.IsReady"/>).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool IsReady { get; }

        /// <summary>Trigger category for Q-C22 priority rules.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public AiMoveCategory Category { get; }

        /// <summary>Max-HP heal percentage (1..100) for <see cref="AiMoveCategory.Heal"/>; 0 otherwise.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int HealPercent { get; }
    }

    /// <summary>Everything about the deciding creature itself.</summary>
    /// <remarks>Pre: build through the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct AiInput
    {
        /// <summary>Creates the input assuming full HP for self and partner.</summary>
        /// <remarks>
        /// Pre: style and role defined; mainTargetId >= CombatAi.NoTarget. Positions are checked by Decide.
        ///      partnerPosition is ignored when !hasPartner; command is ignored by Support and mainTargetId by Main
        ///      (Q-C11: only the main creature hears commands; only the support follows the main's target).
        /// Post: properties equal the arguments; SelfHp == SelfMaxHp == 1, PartnerHp == PartnerMaxHp == 1. Side effects: none.
        /// </remarks>
        public AiInput(float3 selfPosition, AiStyle style, AiRole role, in AiCommand command, int mainTargetId,
            float3 playerPosition, bool hasPartner, float3 partnerPosition)
            : this(selfPosition, style, role, command, mainTargetId, playerPosition, hasPartner, partnerPosition,
                1, 1, 1, 1)
        {
        }

        /// <summary>Creates the input with explicit HP values for Q-C22 heal trigger evaluation.</summary>
        /// <remarks>
        /// Pre: same as the 8-arg constructor, plus selfMaxHp >= 1, 0 &lt;= selfHp &lt;= selfMaxHp,
        ///      and when hasPartner: partnerMaxHp >= 1, 0 &lt;= partnerHp &lt;= partnerMaxHp.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public AiInput(float3 selfPosition, AiStyle style, AiRole role, in AiCommand command, int mainTargetId,
            float3 playerPosition, bool hasPartner, float3 partnerPosition,
            int selfHp, int selfMaxHp, int partnerHp, int partnerMaxHp)
        {
            Contract.Requires(style >= AiStyle.KeepDistance && style <= AiStyle.Hold, "undefined AiStyle");
            Contract.Requires(role == AiRole.Main || role == AiRole.Support, "undefined AiRole");
            Contract.Requires(mainTargetId >= CombatAi.NoTarget, "mainTargetId must be an id or NoTarget");
            Contract.Requires(selfMaxHp >= 1 && selfHp >= 0 && selfHp <= selfMaxHp, "invalid self HP");
            Contract.Requires(!hasPartner || (partnerMaxHp >= 1 && partnerHp >= 0 && partnerHp <= partnerMaxHp),
                "invalid partner HP");
            SelfPosition = selfPosition;
            Style = style;
            Role = role;
            Command = command;
            MainTargetId = mainTargetId;
            PlayerPosition = playerPosition;
            HasPartner = hasPartner;
            PartnerPosition = partnerPosition;
            SelfHp = selfHp;
            SelfMaxHp = selfMaxHp;
            PartnerHp = partnerHp;
            PartnerMaxHp = partnerMaxHp;
        }

        /// <summary>The creature's world position.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 SelfPosition { get; }

        /// <summary>Positioning style (creature data ai_style).</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public AiStyle Style { get; }

        /// <summary>Main or support.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public AiRole Role { get; }

        /// <summary>Player command (read only when Role == Main).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public AiCommand Command { get; }

        /// <summary>The main creature's current target id, or NoTarget (read only when Role == Support).</summary>
        /// <remarks>Pre: none. Post: >= NoTarget. Side effects: none.</remarks>
        public int MainTargetId { get; }

        /// <summary>The player character's world position (Follow / Retreat anchor).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 PlayerPosition { get; }

        /// <summary>Whether the other fielded creature exists (ally moves need it).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool HasPartner { get; }

        /// <summary>The partner's world position; meaningless when !HasPartner.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 PartnerPosition { get; }

        /// <summary>Current HP of the deciding creature.</summary>
        /// <remarks>Pre: none. Post: 0..SelfMaxHp. Side effects: none.</remarks>
        public int SelfHp { get; }

        /// <summary>Maximum HP of the deciding creature.</summary>
        /// <remarks>Pre: none. Post: >= 1 (1 for 8-arg constructor). Side effects: none.</remarks>
        public int SelfMaxHp { get; }

        /// <summary>Current HP of the partner creature; meaningless when !HasPartner.</summary>
        /// <remarks>Pre: none. Post: 0..PartnerMaxHp when HasPartner. Side effects: none.</remarks>
        public int PartnerHp { get; }

        /// <summary>Maximum HP of the partner creature; meaningless when !HasPartner.</summary>
        /// <remarks>Pre: none. Post: >= 1 when HasPartner. Side effects: none.</remarks>
        public int PartnerMaxHp { get; }
    }

    /// <summary>What the creature wants this tick.</summary>
    /// <remarks>Pre: produced by <see cref="CombatAi.Decide"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct AiDecision
    {
        internal AiDecision(int targetId, int castSlot, float3 destinationDirection)
        {
            TargetId = targetId;
            CastSlot = castSlot;
            DestinationDirection = destinationDirection;
        }

        /// <summary>Chosen enemy id, or <see cref="CombatAi.NoTarget"/>.</summary>
        /// <remarks>Pre: none. Post: see Decide. Side effects: none.</remarks>
        public int TargetId { get; }

        /// <summary>Move slot to cast now, or <see cref="CombatAi.NoCast"/>.</summary>
        /// <remarks>Pre: none. Post: see Decide. Side effects: none.</remarks>
        public int CastSlot { get; }

        /// <summary>Unit direction from the planet center to stand at; ground height and pathing are the caller's.</summary>
        /// <remarks>Pre: none. Post: unit length (±1e-5) for any Decide result. Side effects: none.</remarks>
        public float3 DestinationDirection { get; }
    }

    /// <summary>
    /// Deterministic utility-style combat AI for one fielded creature. REQ: docs/design/combat.md#戰鬥-ai.
    /// </summary>
    /// <remarks>
    /// Pure and allocation-free so the host can run it for all 8 creatures per tick (combat.md 效能預算) and clients
    /// can replay it. It only chooses; contact is still MoveGeometry's and the cast still goes through Combatant, so a
    /// wrong choice here costs AI quality (wasted or withheld casts), never rule correctness. Distances are geodesic
    /// at the caster's radius, matching MoveGeometry's footprint convention.
    /// </remarks>
    public static class CombatAi
    {
        /// <summary>Id meaning "no enemy".</summary>
        /// <remarks>Pre: none. Post: constant -1. Side effects: none.</remarks>
        public const int NoTarget = -1;

        /// <summary>Slot meaning "do not cast".</summary>
        /// <remarks>Pre: none. Post: constant -1. Side effects: none.</remarks>
        public const int NoCast = -1;

        private const float UnitTolerance = 1e-3f;


        /// <summary>Range gate: surface distance from the caster to a target's center beyond which the AI does not cast <paramref name="volume"/>.</summary>
        /// <remarks>
        /// Pre: volume built by MoveGeometry.Build.
        /// Post: Caster → 0; Ally → CastDistanceMm; Rect → ceil(√(ReachMm² + (WidthMm/2)²)) (the far corners, SOL A1);
        ///       Sector / caster Circle → ReachMm; aim-point Circle → CastDistanceMm + ReachMm. Saturates at int.MaxValue.
        ///       An upper bound on contact only for zero-radius targets at the caster's radius. It is an approximation,
        ///       not a hit test: it ignores target radius, the sector angle and the π/2 cut-off (the gate passes, the
        ///       hit may still miss), and for aim-point volumes the disc is measured at the anchor's radius, so with the
        ///       target well above or below the caster the true reach scales by casterRadius / anchorRadius (several
        ///       times off inside the combat shell, SOL A2). The gate can therefore withhold a move that would connect
        ///       or pick one that will miss, possibly on every ready tick; contact is always decided by MoveGeometry,
        ///       so this affects AI quality, never rule correctness.
        /// Side effects: none.
        /// </remarks>
        public static int ReachMmOf(in HitVolume volume)
        {
            switch (volume.Kind)
            {
                case HitVolumeKind.Caster:
                    return 0;
                case HitVolumeKind.Ally:
                    return volume.CastDistanceMm;
            }

            if (volume.Anchor == HitVolumeAnchor.AimPoint)
            {
                return (int)math.min((long)volume.CastDistanceMm + volume.ReachMm, int.MaxValue);
            }

            if (volume.Kind == HitVolumeKind.Rect)
            {
                return RectCornerMm(volume.ReachMm, volume.WidthMm);
            }

            return volume.ReachMm;
        }

        // Exact ceil(√(L² + (W/2)²)) = smallest n with 4n² >= 4L² + W². double alone rounded a 1e9 × 1 mm rect down
        // by 1 mm (SOL r2); decimal (96-bit, allocation-free) holds 4·int.MaxValue² ≈ 1.8e19 exactly.
        private static int RectCornerMm(int lengthMm, int widthMm)
        {
            decimal need = 4m * lengthMm * lengthMm + (decimal)widthMm * widthMm;
            long n = (long)math.ceil(math.sqrt((double)lengthMm * lengthMm + 0.25 * widthMm * widthMm));
            while (4m * n * n < need)
            {
                n++;
            }

            while (n > 0 && 4m * (n - 1) * (n - 1) >= need)
            {
                n--;
            }

            return (int)math.min(n, int.MaxValue);
        }

        /// <summary>Chooses target, cast and destination for one creature this tick.</summary>
        /// <remarks>
        /// Pre: planet valid with Radius >= MoveGeometry.MinSupportedRadiusM; tuning, enemies, moves non-null;
        ///      input.SelfPosition, input.PlayerPosition, (input.PartnerPosition when HasPartner) and every enemy
        ///      position within R/2..2R of the center; every living enemy's Forward unit length (±1e-3) and tangent at its
        ///      position (|dot(forward, up)| &lt;= 1e-3); enemy ids distinct; moves.Count &lt;= MoveCooldowns.MaxSlots with
        ///      distinct slots.
        /// Post:
        ///  - Effective command = input.Command for Main, Free for Support.
        ///  - Retreat: TargetId == NoTarget, CastSlot == NoCast, destination = player's direction.
        ///  - Otherwise TargetId is a living enemy or NoTarget (NoTarget iff none lives). Focus picks its target while
        ///    alive and present; Support picks MainTargetId while alive and present; else the nearest living enemy
        ///    (geodesic, ties → smaller id).
        ///  - A move qualifies iff IsReady, TargetId != NoTarget, and: Hostile with the target within ReachMm (inclusive),
        ///    or Self, or Ally with HasPartner and the partner within ReachMm. CastSlot is the qualifying Hostile option
        ///    with max Power, then smaller slot; else the qualifying Self/Ally option with the smaller slot; NoCast iff
        ///    none qualifies (so never with no living enemy: out-of-combat support casts are not chosen).
        ///  - Destination: Follow, no target, or no Hostile option at all (ready or not) → stay unless farther than
        ///    FollowLeashMm from the player, then the point FollowLeashMm/2 from the player toward self. Else by style
        ///    (combat.md table) using the min / max ReachMm of all Hostile options, ready or not; a stand-off beyond a
        ///    quarter circumference is clamped to it.
        ///  - Direction: a stand-off toward self lies on the great circle through the anchor (target or player) and
        ///    self whenever their lateral separation |anchorRel × selfRel| / |anchorRel| (center-relative) is at least
        ///    2⁻²² · (max |center component| + 2R) — the float rounding scale of the world coordinates themselves.
        ///    Below that the plane is not resolved by the inputs (e.g. self and target radially aligned to within float
        ///    precision): any tangent may be used, chosen deterministically; an exactly zero normal uses the
        ///    target's facing (leash: a fixed world axis). The stand-off distance holds in every case.
        ///  - DestinationDirection is unit length. Same inputs → same output.
        /// Side effects: none; no allocation on the success path in any build.
        /// </remarks>
        public static AiDecision Decide(in PlanetConfig planet, AiTuning tuning, in AiInput input,
            IReadOnlyList<AiEnemy> enemies, IReadOnlyList<AiMoveOption> moves)
        {
            Contract.Requires(tuning != null && enemies != null && moves != null, "tuning, enemies and moves must not be null");
            MoveGeometry.RequireCombatPosition(planet, input.SelfPosition, "self");
            MoveGeometry.RequireCombatPosition(planet, input.PlayerPosition, "player");
            if (input.HasPartner)
            {
                MoveGeometry.RequireCombatPosition(planet, input.PartnerPosition, "partner");
            }

            RequireEnemies(planet, enemies);
            RequireMoves(moves);

            double3 center = planet.Center;
            double3 selfFromCenter = (double3)input.SelfPosition - center;
            double radius = math.length(selfFromCenter);
            double3 self = selfFromCenter / radius;
            double3 playerFromCenter = (double3)input.PlayerPosition - center;
            double3 player = math.normalize(playerFromCenter);

            AiCommandKind command = input.Role == AiRole.Main ? input.Command.Kind : AiCommandKind.Free;
            if (command == AiCommandKind.Retreat)
            {
                return new AiDecision(NoTarget, NoCast, (float3)player);
            }

            int preferred = NoTarget;
            if (command == AiCommandKind.Focus)
            {
                preferred = input.Command.TargetId;
            }
            else if (input.Role == AiRole.Support)
            {
                preferred = input.MainTargetId;
            }

            int targetIndex = PickTarget(enemies, preferred, center, self);
            double targetDistanceM = 0;
            double3 target = default;
            double3 targetFromCenter = default;
            if (targetIndex >= 0)
            {
                targetFromCenter = (double3)enemies[targetIndex].Position - center;
                target = math.normalize(targetFromCenter);
                targetDistanceM = Angle(self, target) * radius;
            }

            double partnerDistanceM = double.PositiveInfinity;
            if (input.HasPartner)
            {
                partnerDistanceM = Angle(self, math.normalize((double3)input.PartnerPosition - center)) * radius;
            }

            int castSlot = PickCast(moves, targetIndex >= 0, targetDistanceM, partnerDistanceM, input);
            int targetId = targetIndex >= 0 ? enemies[targetIndex].Id : NoTarget;

            bool hasHostile = HostileReach(moves, out int minReachMm, out int maxReachMm);
            if (command == AiCommandKind.Follow || targetIndex < 0 || !hasHostile)
            {
                return new AiDecision(targetId, castSlot, (float3)Leash(self, player, playerFromCenter, selfFromCenter, radius,
                    tuning.FollowLeashMm));
            }

            double3 destination;
            switch (input.Style)
            {
                case AiStyle.KeepDistance:
                    destination = Toward(target, targetFromCenter, selfFromCenter,
                        maxReachMm * (double)tuning.KeepDistancePermille / 1e6 / radius,
                        TangentOf(enemies[targetIndex].Forward, target));
                    break;
                case AiStyle.Charge:
                    destination = Toward(target, targetFromCenter, selfFromCenter,
                        minReachMm * (double)tuning.CloseInPermille / 1e6 / radius,
                        TangentOf(enemies[targetIndex].Forward, target));
                    break;
                case AiStyle.Flank:
                    destination = Along(target, -TangentOf(enemies[targetIndex].Forward, target),
                        minReachMm * (double)tuning.CloseInPermille / 1e6 / radius);
                    break;
                default:
                    double holdM = maxReachMm * (double)tuning.HoldPermille / 1e6;
                    destination = targetDistanceM <= holdM
                        ? self
                        : Toward(target, targetFromCenter, selfFromCenter, holdM / radius,
                            TangentOf(enemies[targetIndex].Forward, target));
                    break;
            }

            return new AiDecision(targetId, castSlot, (float3)destination);
        }

        private static int PickTarget(IReadOnlyList<AiEnemy> enemies, int preferredId, double3 center, double3 self)
        {
            int best = -1;
            double bestAngle = double.PositiveInfinity;
            for (int i = 0; i < enemies.Count; i++)
            {
                AiEnemy e = enemies[i];
                if (!e.IsAlive)
                {
                    continue;
                }

                if (e.Id == preferredId)
                {
                    return i;
                }

                double angle = Angle(self, math.normalize((double3)e.Position - center));
                if (angle < bestAngle || (angle == bestAngle && e.Id < enemies[best].Id))
                {
                    best = i;
                    bestAngle = angle;
                }
            }

            return best;
        }

        private static int PickCast(IReadOnlyList<AiMoveOption> moves, bool hasTarget, double targetDistanceM,
            double partnerDistanceM, in AiInput input)
        {
            // REQ: docs/design/combat.md#戰鬥-ai (Q-C22) — heals trigger out of combat too, without overflow or when HP < 50%,
            // and take priority over damage when triggered; buffs trigger in combat; hostile picks max power (then smaller slot).
            int heal = NoCast;
            int buff = NoCast;
            int hostile = NoCast;
            int hostilePower = -1;
            int control = NoCast;
            int legacySupport = NoCast;

            for (int i = 0; i < moves.Count; i++)
            {
                AiMoveOption m = moves[i];
                if (!m.IsReady)
                {
                    continue;
                }

                if (m.Category == AiMoveCategory.Heal)
                {
                    bool inReach = m.Targeting == AiMoveTargeting.Self
                        || (m.Targeting == AiMoveTargeting.Ally && input.HasPartner
                            && partnerDistanceM <= m.ReachMm / 1000.0);
                    if (!inReach)
                    {
                        continue;
                    }

                    int hp = m.Targeting == AiMoveTargeting.Ally ? input.PartnerHp : input.SelfHp;
                    int maxHp = m.Targeting == AiMoveTargeting.Ally ? input.PartnerMaxHp : input.SelfMaxHp;
                    if (ShouldHeal(hp, maxHp, m.HealPercent) && (heal == NoCast || m.Slot < heal))
                    {
                        heal = m.Slot;
                    }

                    continue;
                }

                if (!hasTarget)
                {
                    continue;
                }

                if (m.Targeting == AiMoveTargeting.Hostile)
                {
                    if (targetDistanceM > m.ReachMm / 1000.0)
                    {
                        continue;
                    }

                    if (m.Category == AiMoveCategory.Control)
                    {
                        if (control == NoCast || m.Slot < control)
                        {
                            control = m.Slot;
                        }
                    }
                    else if (m.Power > hostilePower || (m.Power == hostilePower && m.Slot < hostile))
                    {
                        hostile = m.Slot;
                        hostilePower = m.Power;
                    }
                }
                else if (m.Targeting == AiMoveTargeting.Self
                         || (input.HasPartner && partnerDistanceM <= m.ReachMm / 1000.0))
                {
                    if (m.Category == AiMoveCategory.Buff)
                    {
                        if (buff == NoCast || m.Slot < buff)
                        {
                            buff = m.Slot;
                        }
                    }
                    else if (legacySupport == NoCast || m.Slot < legacySupport)
                    {
                        legacySupport = m.Slot;
                    }
                }
            }

            if (heal != NoCast)
            {
                return heal;
            }

            if (buff != NoCast)
            {
                return buff;
            }

            if (hostile != NoCast)
            {
                return hostile;
            }

            return control != NoCast ? control : legacySupport;
        }

        private static bool ShouldHeal(int hp, int maxHp, int healPercent)
        {
            if (maxHp <= 0 || hp >= maxHp)
            {
                return false;
            }

            // Below 50% always heals; otherwise avoid overflowing MaxHp.
            if (hp * 2L < maxHp)
            {
                return true;
            }

            long healAmount = (long)maxHp * healPercent / 100L;
            return hp + healAmount <= maxHp;
        }

        private static bool HostileReach(IReadOnlyList<AiMoveOption> moves, out int minReachMm, out int maxReachMm)
        {
            minReachMm = int.MaxValue;
            maxReachMm = 0;
            bool any = false;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Targeting != AiMoveTargeting.Hostile)
                {
                    continue;
                }

                any = true;
                minReachMm = math.min(minReachMm, moves[i].ReachMm);
                maxReachMm = math.max(maxReachMm, moves[i].ReachMm);
            }

            return any;
        }

        private static double3 Leash(double3 self, double3 player, double3 playerFromCenter, double3 selfFromCenter,
            double radius, int leashMm)
        {
            double leashM = leashMm / 1000.0;
            if (Angle(self, player) * radius <= leashM)
            {
                return self;
            }

            // Return to half the leash, not its edge, so a creature does not oscillate across the boundary every tick.
            return Toward(player, playerFromCenter, selfFromCenter, leashM * 0.5 / radius, AnyTangent(player));
        }

        // Point at `angle` radians from unit `from` along the great circle through the center-relative points
        // `fromRel` → `toRel`; `fallback` (a unit tangent at `from`) only when they define no plane (exact zero normal:
        // same direction or exactly antipodal).
        // The plane normal is fromRel × toRel on the raw coordinates, never on normalized directions: each component
        // then rounds at most once relative to its own size (and is exact when the center is zero, float × float fitting
        // in double), so a tiny sideways offset is never swamped by normalization error. Every earlier variant built
        // the plane from something already rounded and lost it (SOL P2-10 r1 normalized directions, r2 relative cut-off,
        // r3 projection residual, r4 normalize-then-cross: 11 m off the true circle). t = n × from is a true tangent.
        private static double3 Toward(double3 from, double3 fromRel, double3 toRel, double angle, double3 fallback)
        {
            double3 n = math.cross(fromRel, toRel);
            double3 t = math.cross(n, from);
            t -= from * math.dot(t, from);
            double len = math.length(t);
            return Along(from, len > 0.0 ? t / len : fallback, angle);
        }

        // Clamped to a quarter circumference by choice: MoveGeometry never connects beyond π/2, so standing farther
        // buys nothing; unclamped, angles past π also folded back and landed nearer than asked (SOL A3).
        private static double3 Along(double3 from, double3 unitTangent, double angle)
        {
            double a = math.min(angle, math.PI_DBL * 0.5);
            return math.normalize(from * math.cos(a) + unitTangent * math.sin(a));
        }

        private static double3 TangentOf(float3 forward, double3 up)
        {
            double3 f = forward;
            double3 t = f - up * math.dot(f, up);
            // Pre guarantees |dot(forward, up)| <= 1e-3 with unit forward, so |t| >= ~0.999.
            return t / math.length(t);
        }

        private static double3 AnyTangent(double3 up)
        {
            double3 axis = math.abs(up.y) < 0.9 ? new double3(0, 1, 0) : new double3(1, 0, 0);
            return math.normalize(math.cross(axis, up));
        }

        // atan2 instead of acos: acos loses ~1e-4 rad near 0, i.e. ~6 cm at R = 640, enough to flip a reach gate.
        private static double Angle(double3 a, double3 b)
        {
            return math.atan2(math.length(math.cross(a, b)), math.dot(a, b));
        }

        private static void RequireEnemies(in PlanetConfig planet, IReadOnlyList<AiEnemy> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                AiEnemy e = enemies[i];
                MoveGeometry.RequireCombatPosition(planet, e.Position, "enemy");
                if (e.IsAlive)
                {
                    float3 up = planet.ToDirection(e.Position);
                    Contract.Requires(math.all(math.isfinite(e.Forward)) && math.abs(math.length(e.Forward) - 1f) <= UnitTolerance,
                        "living enemy forward must be unit length");
                    Contract.Requires(math.abs(math.dot(e.Forward, up)) <= UnitTolerance,
                        "living enemy forward must be tangent to the surface");
                }

                for (int j = 0; j < i; j++)
                {
                    Contract.Requires(enemies[j].Id != e.Id, "enemy ids must be distinct");
                }
            }
        }

        private static void RequireMoves(IReadOnlyList<AiMoveOption> moves)
        {
            Contract.Requires(moves.Count <= MoveCooldowns.MaxSlots, "at most MaxSlots moves");
            for (int i = 0; i < moves.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    Contract.Requires(moves[j].Slot != moves[i].Slot, "move slots must be distinct");
                }
            }
        }
    }
}
