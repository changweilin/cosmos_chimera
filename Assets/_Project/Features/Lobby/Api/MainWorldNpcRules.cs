using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Lobby
{
    /// <summary>
    /// Six specialized NPC roles stationed inside every Main World settlement plaza.
    /// REQ: docs/design/lobby-meta.md#功能劃分選單通用與主世界-npc特定.
    /// </summary>
    /// <remarks>Pre: none. Post: defined role enum. Side effects: none.</remarks>
    public enum SettlementNpcRole
    {
        Merchant = 0,
        Craftsman = 1,
        Hatchery = 2,
        VesselConverter = 3,
        QuestGuide = 4,
        Healer = 5,
    }

    /// <summary>
    /// Deterministic spherical placement of one specialized NPC inside a settlement plaza.
    /// </summary>
    /// <remarks>Pre: constructed via <see cref="MainWorldNpcRules"/>. Post: immutable placement record. Side effects: none.</remarks>
    public readonly struct SettlementNpcPlacement
    {
        /// <summary>Zero-based settlement index.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int SettlementIndex { get; }

        /// <summary>Specialized service role offered by this NPC.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="SettlementNpcRole"/>. Side effects: none.</remarks>
        public SettlementNpcRole Role { get; }

        /// <summary>Unit sphere direction of the NPC.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Unit tangent vector facing toward the settlement plaza center.</summary>
        /// <remarks>Pre: none. Post: unit length, orthogonal to Direction. Side effects: none.</remarks>
        public float3 Facing { get; }

        /// <summary>Stable snake_case key matching localization strings.</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string RoleKey { get; }

        /// <summary>Constructs an NPC placement record.</summary>
        /// <remarks>
        /// Pre: settlementIndex &gt;= 0; role defined; roleKey non-empty; direction/facing non-zero.
        /// Post: fields assigned. Side effects: none.
        /// </remarks>
        public SettlementNpcPlacement(
            int settlementIndex,
            SettlementNpcRole role,
            float3 direction,
            float3 facing,
            string roleKey)
        {
            Contract.Requires(settlementIndex >= 0, "settlementIndex must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(SettlementNpcRole), role), "role must be defined");
            Contract.Requires(!string.IsNullOrEmpty(roleKey), "roleKey must be non-empty");
            Contract.Requires(math.lengthsq(direction) > 0.5f, "direction must be non-zero");
            Contract.Requires(math.lengthsq(facing) > 0.5f, "facing must be non-zero");

            SettlementIndex = settlementIndex;
            Role = role;
            Direction = math.normalize(direction);
            Facing = math.normalize(facing);
            RoleKey = roleKey;
        }
    }

    /// <summary>
    /// Placement and service unlock/cost rules for Main World settlement NPCs.
    /// REQ: docs/design/lobby-meta.md.
    /// </summary>
    /// <remarks>
    /// Pre: valid settlement centers and planet radius.
    /// Post: deterministic 6-NPC ring per settlement and vessel conversion rules.
    /// Side effects: none (pure).
    /// </remarks>
    public static class MainWorldNpcRules
    {
        /// <summary>Interaction distance (m) from the player to a settlement NPC.</summary>
        /// <remarks>Pre: none. Post: constant 4.5 m. Side effects: none.</remarks>
        public const float InteractRadiusM = 4.5f;

        private const float PlazaRingRadiusM = 11.5f;
        private const int RoleCount = 6;

        /// <summary>Places all 6 specialized NPC roles around every settlement center on the sphere.</summary>
        /// <remarks>
        /// Pre: settlementCenters != null; each entry has length &gt; 0.5; planetRadiusM &gt; 0.
        /// Post: returns settlementCenters.Count * 6 placements on the planet sphere without overlapping.
        /// Side effects: allocates returned list.
        /// </remarks>
        public static List<SettlementNpcPlacement> BuildSettlementNpcs(
            IReadOnlyList<float3> settlementCenters,
            float planetRadiusM,
            ulong worldSeed)
        {
            Contract.Requires(settlementCenters != null, "settlementCenters must be non-null");
            Contract.Requires(planetRadiusM > 0f, "planetRadiusM must be > 0");

            var list = new List<SettlementNpcPlacement>(settlementCenters.Count * RoleCount);
            for (int s = 0; s < settlementCenters.Count; s++)
            {
                Contract.Requires(math.lengthsq(settlementCenters[s]) > 0.5f, "settlement center must be non-zero");
                float3 center = math.normalize(settlementCenters[s]);
                PlanetFrame frame = PlanetFrame.FromUp(center);
                var rng = new Pcg32(worldSeed ^ unchecked((ulong)(s + 1) * 0x517CC1B727220A95UL), 'N');
                float baseAngle = rng.NextFloat() * (2f * math.PI);

                for (int r = 0; r < RoleCount; r++)
                {
                    float angle = baseAngle + r * (2f * math.PI / RoleCount);
                    float3 tangent = frame.East * math.cos(angle) + frame.North * math.sin(angle);
                    float3 dir = math.normalize(center * planetRadiusM + tangent * PlazaRingRadiusM);
                    float3 towardCenter = center - dir * math.dot(center, dir);
                    float3 facing = math.lengthsq(towardCenter) > 1e-8f ? math.normalize(towardCenter) : frame.North;
                    var role = (SettlementNpcRole)r;
                    list.Add(new SettlementNpcPlacement(s, role, dir, facing, RoleKeyOf(role)));
                }
            }

            return list;
        }

        /// <summary>Whether the player has unlocked late-game functional vessel conversion (after Moon progression).</summary>
        /// <remarks>REQ: docs/design/lobby-meta.md#收服道具. Pre: none. Post: returns moonUnlocked. Side effects: none.</remarks>
        public static bool CanConvertVessel(bool moonUnlocked) => moonUnlocked;

        /// <summary>Material cost to convert a creature's functional vessel category.</summary>
        /// <remarks>
        /// Pre: specialty is a defined <see cref="Element"/>.
        /// Post: tier-1 elemental material of specialty, quantity 2.
        /// Side effects: none.
        /// </remarks>
        public static MaterialStack VesselConvertCost(Element specialty)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), specialty), "specialty must be defined");
            return new MaterialStack(new MaterialKey(specialty, 1), 2);
        }

        /// <summary>Returns the stable localization suffix key for <paramref name="role"/>.</summary>
        /// <remarks>Pre: role defined. Post: non-empty snake_case key. Side effects: none.</remarks>
        public static string RoleKeyOf(SettlementNpcRole role)
        {
            Contract.Requires(Enum.IsDefined(typeof(SettlementNpcRole), role), "role must be defined");
            switch (role)
            {
                case SettlementNpcRole.Merchant: return "merchant";
                case SettlementNpcRole.Craftsman: return "craftsman";
                case SettlementNpcRole.Hatchery: return "hatchery";
                case SettlementNpcRole.VesselConverter: return "vessel_converter";
                case SettlementNpcRole.QuestGuide: return "quest_guide";
                case SettlementNpcRole.Healer: return "healer";
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "undefined SettlementNpcRole");
            }
        }
    }
}
