using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>Why a food gathering action was accepted or refused.</summary>
    public enum GatheringRefusalReason
    {
        /// <summary>Gathering succeeded.</summary>
        None = 0,
        /// <summary>The active party lacks the required life skill.</summary>
        MissingSkill = 1,
        /// <summary>The current location/biome does not support this gathering method.</summary>
        InvalidEnvironment = 2,
        /// <summary>No ingredients are registered for this gathering method.</summary>
        NoPoolAvailable = 3,
        /// <summary>Maximum active traps reached.</summary>
        TrapLimitReached = 4,
    }

    /// <summary>Environmental and team context for evaluating a food gathering action.</summary>
    /// <remarks>Pre: constructed with valid modifiers. Post: immutable value. Side effects: none.</remarks>
    public readonly struct GatheringContext
    {
        /// <summary>Creates a gathering context.</summary>
        /// <remarks>
        /// Pre: teamModifiers valid; extraDropBonusPercent &gt;= 0.
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public GatheringContext(
            in TeamUtilityModifiers teamModifiers,
            bool onGround,
            bool hasOpenSky,
            bool nearWater,
            bool hasFoliage,
            bool hasFarmland,
            int extraDropBonusPercent = 0)
        {
            Contract.Requires(extraDropBonusPercent >= 0, "extraDropBonusPercent must be >= 0");
            TeamModifiers = teamModifiers;
            OnGround = onGround;
            HasOpenSky = hasOpenSky;
            NearWater = nearWater;
            HasFoliage = hasFoliage;
            HasFarmland = hasFarmland;
            ExtraDropBonusPercent = extraDropBonusPercent;
        }

        /// <summary>Active team life-skill modifiers.</summary>
        /// <remarks>Pre: none. Post: valid struct. Side effects: none.</remarks>
        public TeamUtilityModifiers TeamModifiers { get; }

        /// <summary>True when on solid terrain (not deep sea).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool OnGround { get; }

        /// <summary>True when outdoors with sky access (not underground/indoor).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool HasOpenSky { get; }

        /// <summary>True when adjacent to or in water (river, lake, ocean).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool NearWater { get; }

        /// <summary>True when near forest, bushes, or wild flora.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool HasFoliage { get; }

        /// <summary>True when near cultivated crops or settlement fields.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool HasFarmland { get; }

        /// <summary>Additional drop bonus (e.g. from active food buff) in percentage points.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int ExtraDropBonusPercent { get; }
    }

    /// <summary>One ingredient stack yielded by a gathering action.</summary>
    /// <remarks>Pre: ingredient != null; quantity &gt;= 1. Post: immutable. Side effects: none.</remarks>
    public readonly struct GatheredStack
    {
        /// <summary>Creates a gathered stack.</summary>
        /// <remarks>Pre: ingredient != null; quantity &gt;= 1. Post: properties equal arguments. Side effects: none.</remarks>
        public GatheredStack(IngredientSpec ingredient, int quantity)
        {
            Contract.Requires(ingredient != null, "ingredient must not be null");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");
            Ingredient = ingredient;
            Quantity = quantity;
        }

        /// <summary>Ingredient definition.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IngredientSpec Ingredient { get; }

        /// <summary>Units gathered.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Quantity { get; }
    }

    /// <summary>State of a deployed wildlife/insect trap.</summary>
    /// <remarks>Pre: created by <see cref="TrapManager"/>. Post: immutable snapshot. Side effects: none.</remarks>
    public readonly struct TrapState
    {
        internal TrapState(int id, long placedAtMs, long readyAtMs, int dropBonusPercent)
        {
            Id = id;
            PlacedAtMs = placedAtMs;
            ReadyAtMs = readyAtMs;
            DropBonusPercent = dropBonusPercent;
        }

        /// <summary>Unique trap instance id within the run.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Id { get; }

        /// <summary>Simulation time when trap was placed (ms).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public long PlacedAtMs { get; }

        /// <summary>Simulation time when trap becomes ready to harvest (ms).</summary>
        /// <remarks>Pre: none. Post: &gt; PlacedAtMs. Side effects: none.</remarks>
        public long ReadyAtMs { get; }

        /// <summary>Drop bonus captured at placement time.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int DropBonusPercent { get; }

        /// <summary>Whether this trap is ready at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: nowMs in CombatTime. Post: true iff nowMs &gt;= ReadyAtMs. Side effects: none.</remarks>
        public bool IsReady(long nowMs) => nowMs >= ReadyAtMs;
    }

    /// <summary>
    /// Pure deterministic manager for deployed traps (<see cref="FoodAcquisitionMethod.SetTrap"/>).
    /// </summary>
    /// <remarks>Pre: monotonic simulation time in CombatTime. Post: at most MaxActiveTraps live. Side effects: mutates internal trap list.</remarks>
    public sealed class TrapManager
    {
        /// <summary>Maximum concurrent traps a player can deploy.</summary>
        /// <remarks>Pre: none. Post: constant 3. Side effects: none.</remarks>
        public const int MaxActiveTraps = 3;

        /// <summary>Default trap soak duration in milliseconds (30 seconds).</summary>
        /// <remarks>Pre: none. Post: constant 30000. Side effects: none.</remarks>
        public const int DefaultTrapDurationMs = 30_000;

        private readonly List<TrapState> _traps = new List<TrapState>(MaxActiveTraps);
        private int _nextId = 1;
        private long _watermarkMs;

        /// <summary>Currently deployed traps.</summary>
        /// <remarks>Pre: none. Post: count 0..MaxActiveTraps. Side effects: none.</remarks>
        public IReadOnlyList<TrapState> ActiveTraps => _traps;

        /// <summary>Attempts to deploy a new trap at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: nowMs in CombatTime and &gt;= watermark; durationMs &gt;= 1.
        /// Post: returns None and adds trap if skill, ground, and capacity checks pass; otherwise returns refusal reason and leaves traps unchanged.
        /// Side effects: advances watermark; mutates active traps on success.
        /// </remarks>
        public GatheringRefusalReason TryPlaceTrap(
            in GatheringContext context,
            long nowMs,
            out TrapState placed,
            int durationMs = DefaultTrapDurationMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            Contract.Requires(durationMs >= 1 && durationMs <= EffectBook.MaxDurationMs, "durationMs must be 1..MaxDurationMs");
            _watermarkMs = nowMs;

            if (!context.TeamModifiers.CanSetTrap)
            {
                placed = default;
                return GatheringRefusalReason.MissingSkill;
            }

            if (!context.OnGround)
            {
                placed = default;
                return GatheringRefusalReason.InvalidEnvironment;
            }

            if (_traps.Count >= MaxActiveTraps)
            {
                placed = default;
                return GatheringRefusalReason.TrapLimitReached;
            }

            int totalBonus = context.TeamModifiers.DropRateBonusPercent + context.ExtraDropBonusPercent;
            placed = new TrapState(_nextId++, nowMs, nowMs + durationMs, totalBonus);
            _traps.Add(placed);
            return GatheringRefusalReason.None;
        }

        /// <summary>Collects all ready traps at <paramref name="nowMs"/>, rolling yields into <paramref name="inventory"/>.</summary>
        /// <remarks>
        /// Pre: catalog != null; inventory != null; nowMs in CombatTime and &gt;= watermark; outCollected != null.
        /// Post: ready traps are removed and their rolled ingredients added to inventory and outCollected; unready traps remain.
        /// Side effects: advances watermark, mutates active traps, rng, inventory, and outCollected.
        /// </remarks>
        public int CollectReadyTraps(
            FoodCatalog catalog,
            FoodInventory inventory,
            long nowMs,
            ref Pcg32 rng,
            List<GatheredStack> outCollected)
        {
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");
            Contract.Requires(outCollected != null, "outCollected must not be null");
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;

            outCollected.Clear();
            var pool = new List<IngredientSpec>();
            catalog.GetIngredientsBySource(FoodAcquisitionMethod.SetTrap, pool);
            if (pool.Count == 0)
            {
                return 0;
            }

            int collectedCount = 0;
            for (int i = _traps.Count - 1; i >= 0; i--)
            {
                TrapState trap = _traps[i];
                if (!trap.IsReady(nowMs))
                {
                    continue;
                }

                _traps.RemoveAt(i);
                collectedCount++;

                IngredientSpec pick = FoodGatheringRules.PickWeighted(pool, trap.DropBonusPercent, ref rng);
                int qty = rng.NextInt(1, 3);
                inventory.AddIngredient(pick.Id, qty);
                outCollected.Add(new GatheredStack(pick, qty));
            }

            return collectedCount;
        }
    }

    /// <summary>
    /// Pure deterministic rules for acquiring food ingredients across all 8 life-skill methods:
    /// HuntGround, HuntSky, SetTrap, Fishing, CastNet, ForageBerries, HarvestCrops, ProduceMilk.
    /// </summary>
    /// <remarks>Pre: valid catalog, context, inventory, and Pcg32. Post: deterministic yields. Side effects: mutates inventory and rng on success.</remarks>
    public static class FoodGatheringRules
    {
        /// <summary>Checks whether <paramref name="method"/> can be performed in <paramref name="context"/>.</summary>
        /// <remarks>
        /// Pre: method is a defined FoodAcquisitionMethod.
        /// Post: returns None iff both skill and environment requirements are satisfied.
        /// Side effects: none.
        /// </remarks>
        public static GatheringRefusalReason CanGather(FoodAcquisitionMethod method, in GatheringContext context)
        {
            Contract.Requires(Enum.IsDefined(typeof(FoodAcquisitionMethod), method), "method must be defined");

            switch (method)
            {
                case FoodAcquisitionMethod.HuntGround:
                    if (!context.TeamModifiers.CanHuntGround) return GatheringRefusalReason.MissingSkill;
                    return context.OnGround ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.HuntSky:
                    if (!context.TeamModifiers.CanHuntSky) return GatheringRefusalReason.MissingSkill;
                    return context.HasOpenSky ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.SetTrap:
                    if (!context.TeamModifiers.CanSetTrap) return GatheringRefusalReason.MissingSkill;
                    return context.OnGround ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.Fishing:
                    if (!context.TeamModifiers.CanFishing) return GatheringRefusalReason.MissingSkill;
                    return context.NearWater ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.CastNet:
                    if (!context.TeamModifiers.CanCastNet) return GatheringRefusalReason.MissingSkill;
                    return context.NearWater ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.ForageBerries:
                    if (!context.TeamModifiers.CanForageBerries) return GatheringRefusalReason.MissingSkill;
                    return context.HasFoliage ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.HarvestCrops:
                    if (!context.TeamModifiers.CanHarvestCrops) return GatheringRefusalReason.MissingSkill;
                    return context.HasFarmland ? GatheringRefusalReason.None : GatheringRefusalReason.InvalidEnvironment;

                case FoodAcquisitionMethod.ProduceMilk:
                    return context.TeamModifiers.CanProduceMilk ? GatheringRefusalReason.None : GatheringRefusalReason.MissingSkill;

                default:
                    return GatheringRefusalReason.InvalidEnvironment;
            }
        }

        /// <summary>Executes an immediate gathering action for <paramref name="method"/> and adds yields to <paramref name="inventory"/>.</summary>
        /// <remarks>
        /// Pre: method defined; catalog, inventory, outYields non-null.
        /// Post: returns None and populates outYields/inventory on success; on refusal, outYields is empty and inventory unchanged.
        /// Side effects: mutates inventory, rng, and outYields on success.
        /// </remarks>
        public static GatheringRefusalReason TryGather(
            FoodAcquisitionMethod method,
            in GatheringContext context,
            FoodCatalog catalog,
            FoodInventory inventory,
            ref Pcg32 rng,
            List<GatheredStack> outYields)
        {
            Contract.Requires(Enum.IsDefined(typeof(FoodAcquisitionMethod), method), "method must be defined");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");
            Contract.Requires(outYields != null, "outYields must not be null");

            outYields.Clear();
            GatheringRefusalReason refusal = CanGather(method, context);
            if (refusal != GatheringRefusalReason.None)
            {
                return refusal;
            }

            var pool = new List<IngredientSpec>();
            catalog.GetIngredientsBySource(method, pool);
            if (pool.Count == 0)
            {
                return GatheringRefusalReason.NoPoolAvailable;
            }

            int dropBonus = context.TeamModifiers.DropRateBonusPercent + context.ExtraDropBonusPercent;
            int rolls = RollsForMethod(method);
            int pickupMult = context.TeamModifiers.PickupMultiplier > 1 ? context.TeamModifiers.PickupMultiplier : 1;

            for (int r = 0; r < rolls; r++)
            {
                IngredientSpec picked = PickWeighted(pool, dropBonus, ref rng);
                int baseQty = BaseQtyForMethod(method, ref rng);
                int qty = baseQty * pickupMult;
                inventory.AddIngredient(picked.Id, qty);
                outYields.Add(new GatheredStack(picked, qty));
            }

            return GatheringRefusalReason.None;
        }

        internal static IngredientSpec PickWeighted(IReadOnlyList<IngredientSpec> pool, int dropBonusPercent, ref Pcg32 rng)
        {
            // Higher tier ingredients have lower base weight, boosted by dropBonusPercent.
            int totalWeight = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                totalWeight += WeightForTier(pool[i].Tier, dropBonusPercent);
            }

            int roll = rng.NextInt(0, totalWeight);
            int acc = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += WeightForTier(pool[i].Tier, dropBonusPercent);
                if (roll < acc)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        private static int WeightForTier(int tier, int dropBonusPercent)
        {
            int bonus = dropBonusPercent > 0 ? dropBonusPercent : 0;
            switch (tier)
            {
                case 1: return 60;
                case 2: return 30 + bonus / 2;
                default: return 10 + bonus / 3;
            }
        }

        private static int RollsForMethod(FoodAcquisitionMethod method)
        {
            // CastNet and HarvestCrops yield multiple stacks per action.
            switch (method)
            {
                case FoodAcquisitionMethod.CastNet: return 3;
                case FoodAcquisitionMethod.HarvestCrops: return 2;
                default: return 1;
            }
        }

        private static int BaseQtyForMethod(FoodAcquisitionMethod method, ref Pcg32 rng)
        {
            switch (method)
            {
                case FoodAcquisitionMethod.CastNet:
                case FoodAcquisitionMethod.HarvestCrops:
                    return rng.NextInt(1, 3);
                case FoodAcquisitionMethod.ForageBerries:
                    return rng.NextInt(1, 3);
                default:
                    return 1;
            }
        }
    }
}
