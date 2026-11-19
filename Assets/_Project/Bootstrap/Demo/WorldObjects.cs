using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Joins terrain scatter, settlement buildings and moving field objects to the combat rule layer: the destructible lookup moves hit, and the
    /// blocking test creatures obey. REQ: docs/design/destructibles.md (碰撞, 狀態保留, 實作分層), ADR-0009.
    /// </summary>
    /// <remarks>
    /// Ids: a scatter item is its cell key (≥ 0, below ScatterPlanner.KeyCount); building i is <see cref="BuildingIdBase"/> + i. Treasure and field objects use separate higher ranges. All are
    /// derived from the generation result, so every peer and every LOD rebuild agrees.
    /// Pre: built once per run on the host. Post: see members. Side effects: caches scatter cells (inside the planner).
    /// </remarks>
    public sealed class WorldObjects : IDestructibleIndex
    {
        /// <summary>First building id; far above any scatter cell key (KeyCount &lt; 2^34 for R ≤ 1e5 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const long BuildingIdBase = 1L << 40;

        /// <summary>First treasure-chest id (Q-W3); above all building ids.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const long TreasureIdBase = 1L << 41;

        internal const long FieldIdBase = 1L << 42;
        private IReadOnlyList<FieldSpecimen> _field = Array.Empty<FieldSpecimen>();
        private Composition[] _fieldComposition = Array.Empty<Composition>();

        internal void AttachField(IReadOnlyList<FieldSpecimen> field)
        {
            _field = field;
            foreach (var item in field) MaxHitRadiusM = math.max(MaxHitRadiusM, item.Radius);
            _fieldComposition = new Composition[field.Count];
            for (int i = 0; i < field.Count; i++)
            {
                var item = field[i];
                string material = item.Animal ? "fiber" : item.Form == FieldForm.SteamCar || item.Form == FieldForm.MineCart ? "metal" : "wood";
                if (!_env.TryGetSubstance(material, out Substance sub)) throw new InvalidOperationException("Missing field substance " + material);
                _fieldComposition[i] = Composition.Of(sub);
            }
        }

        internal void StepField(long nowMs)
        {
            for (int i = 0; i < _field.Count; i++)
            {
                if (_table.IsDestroyed(FieldIdBase + i)) continue;
                _field[i].Step(nowMs);
                int slot = _table.IndexOf(FieldIdBase + i);
                if (slot >= 0) _table.MoveCenter(slot, _field[i].Center);
            }
        }

        private DestructibleCandidate FieldCandidate(int i)
        {
            var item = _field[i];
            return new DestructibleCandidate(FieldIdBase + i, item.Center, item.Radius, _fieldComposition[i],
                (int)((item.Animal ? 24 : 410) * item.Scale * item.Scale * item.Scale));
        }

        private const float StoreyM = 3f; // SettlementView.StoreyM: HP and drop odds follow the drawn volume
        private const float TreasureHitRadiusM = 0.9f;
        private const int TreasureVolumeL = 800;

        private readonly PlanetConfig _planet;
        private readonly ScatterPlanner _scatter;
        private readonly RunLayout _layout;
        private readonly RunPlan _plan;
        private readonly LayoutClearance _clearance;
        private readonly Func<float3, float> _groundAltitude;
        private readonly EnvMaterials _env;
        private readonly DestructibleTable _table;
        private readonly Composition[] _kindComposition;
        private readonly string[] _kindNameKey;
        private readonly float _maxBuildingHitM;
        private readonly float _maxGrassCoverM;
        private readonly List<ScatterItem> _items = new List<ScatterItem>(16);
        private readonly List<int> _treasureRegions = new List<int>();

        /// <summary>Creates the bridge.</summary>
        /// <remarks>
        /// Pre: every argument non-null and from the same run (groundAltitude = the terrain the scatter planner uses);
        ///      every scatter kind's substance id exists in env.
        /// Post: Query and IsBlocked answer for this run; MaxHitRadiusM covers every scatter item and building.
        /// Side effects: allocates.
        /// </remarks>
        public WorldObjects(in PlanetConfig planet, ScatterPlanner scatter, RunLayout layout, LayoutClearance clearance,
            Func<float3, float> groundAltitude, EnvMaterials env, DestructibleTable table,
            DungeonLayout dungeon = null, RunPlan plan = null)
        {
            Contract.Requires(scatter != null && layout != null && clearance != null && groundAltitude != null &&
                env != null && table != null, "every argument is required");
            Contract.Requires(dungeon == null || plan != null, "plan is required when dungeon is provided");
            _groundAltitude = groundAltitude;
            _planet = planet;
            _scatter = scatter;
            _layout = layout;
            _plan = plan;
            _clearance = clearance;
            _env = env;
            _table = table;
            _kindComposition = new Composition[scatter.Kinds.Count];
            _kindNameKey = new string[scatter.Kinds.Count];
            float maxScatter = 0f;
            for (int k = 0; k < _kindComposition.Length; k++)
            {
                ScatterKind kind = scatter.Kinds[k];
                Contract.Requires(env.TryGetSubstance(kind.SubstanceId, out Substance sub),
                    "scatter substance " + kind.SubstanceId + " missing from env-materials");
                _kindComposition[k] = Composition.Of(sub);
                _kindNameKey[k] = "ui.object." + kind.Id;
                maxScatter = math.max(maxScatter, HitRadius(kind.Shape, kind.MaxSizeM));
                if (kind.Shape == ScatterShape.TallGrass)
                {
                    _maxGrassCoverM = math.max(_maxGrassCoverM, HitRadius(kind.Shape, kind.MaxSizeM));
                }
            }

            foreach (RunBuilding b in layout.Buildings)
            {
                _maxBuildingHitM = math.max(_maxBuildingHitM, BuildingHitRadius(b));
            }

            if (dungeon != null)
            {
                for (int r = 0; r < dungeon.Rooms.Count; r++)
                {
                    if (dungeon.Rooms[r] == RoomRole.Treasure)
                    {
                        _treasureRegions.Add(r);
                    }
                }
            }

            MaxHitRadiusM = math.max(maxScatter, math.max(_maxBuildingHitM, math.max(TreasureHitRadiusM, 0.1f)));
        }

        /// <inheritdoc />
        public float MaxHitRadiusM { get; private set; }

        /// <summary>Whether <paramref name="id"/> names a building.</summary>
        /// <remarks>Pre: none. Post: true iff BuildingIdBase ≤ id &lt; TreasureIdBase. Side effects: none.</remarks>
        public static bool IsBuilding(long id) => id >= BuildingIdBase && id < TreasureIdBase;

        /// <summary>Whether <paramref name="id"/> names a treasure chest (Q-W3).</summary>
        /// <remarks>Pre: none. Post: true iff TreasureIdBase ≤ id &lt; FieldIdBase. Side effects: none.</remarks>
        public static bool IsTreasure(long id) => id >= TreasureIdBase && id < FieldIdBase;

        internal bool HasFoliage(float3 direction, float reachM)
        {
            _items.Clear();
            _scatter.Query(direction, reachM, _items);
            foreach (ScatterItem item in _items)
            {
                if (!IsFoliage(item.Shape)) continue;
                if (!_table.IsDestroyed(item.CellKey) && math.distance(direction, item.Direction) * _planet.Radius <= reachM)
                    return true;
            }
            return false;
        }

        // The scatter kind id gathering uses as the node's source; buildings and chests are not gathering nodes.
        internal bool TryGetGatherSource(long id, out string source)
        {
            source = null;
            if (id < 0 || id >= _scatter.KeyCount || !_scatter.TryGetItem(id, out ScatterItem item)) return false;
            source = _scatter.Kinds[item.KindIndex].Id;
            return true;
        }

        internal void QueryGathering(float3 direction, float reachM, List<GatheringNode> nodes)
        {
            nodes.Clear();
            _items.Clear();
            _scatter.Query(direction, reachM, _items);
            foreach (ScatterItem item in _items)
            {
                if (_table.IsDestroyed(item.CellKey) ||
                    math.distance(direction, item.Direction) * _planet.Radius > reachM) continue;
                nodes.Add(new GatheringNode(item.CellKey, _scatter.Kinds[item.KindIndex].Id));
            }
            nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        /// <summary>Whether intact tall grass covers a ground direction.</summary>
        /// <remarks>
        /// REQ: docs/design/destructibles.md#碰撞.
        /// Pre: direction unit length and finite.
        /// Post: true iff direction is inside an intact tall-grass item's hit-radius footprint; does not register objects.
        /// Side effects: caches scatter cells and reuses the query buffer.
        /// </remarks>
        public bool HasGrassCover(float3 direction)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.abs(math.lengthsq(direction) - 1f) <= 2e-3f,
                "direction must be finite and unit length");
            _items.Clear();
            _scatter.Query(direction, _maxGrassCoverM, _items);
            foreach (ScatterItem item in _items)
            {
                if (item.Shape != ScatterShape.TallGrass || _table.IsDestroyed(item.CellKey)) continue;
                if (math.distance(direction, item.Direction) * _planet.Radius <= HitRadius(item.Shape, item.SizeM))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void Query(float3 worldPoint, float radiusM, List<DestructibleCandidate> into)
        {
            Contract.Requires(into != null && math.all(math.isfinite(worldPoint)), "into non-null, worldPoint finite");
            Contract.Requires(math.isfinite(radiusM) && radiusM >= 0f, "radiusM must be finite and >= 0");
            float3 up = _planet.ToDirection(worldPoint);
            _items.Clear();
            _scatter.Query(up, radiusM, _items);
            foreach (ScatterItem it in _items)
            {
                into.Add(ScatterCandidate(it));
            }

            for (int i = 0; i < _layout.Buildings.Count; i++)
            {
                RunBuilding b = _layout.Buildings[i];
                if (math.distance(up, b.Direction) * _planet.Radius > radiusM + _maxBuildingHitM)
                {
                    continue;
                }

                DestructibleCandidate candidate = BuildingCandidate(i);
                if (math.distance(worldPoint, candidate.Center) <= radiusM + candidate.HitRadiusM)
                {
                    into.Add(candidate);
                }
            }

            for (int i = 0; i < _treasureRegions.Count; i++)
            {
                int r = _treasureRegions[i];
                float3 dir = _plan.Regions[r].Center;
                if (math.distance(up, dir) * _planet.Radius <= radiusM + TreasureHitRadiusM)
                {
                    into.Add(TreasureCandidate(r));
                }
            }

            for (int i = 0; i < _field.Count; i++)
            {
                var item = _field[i];
                if (math.distance(item.Center, worldPoint) <= radiusM + item.Radius && !_table.IsDestroyed(FieldIdBase + i))
                    into.Add(FieldCandidate(i));
            }
        }

        /// <summary>The candidate Query would report for <paramref name="id"/>, if that object exists in this run.</summary>
        /// <remarks>
        /// Pre: none. Post: true iff id belongs to a generated building, scatter item, chest or field object (destroyed ones included);
        ///      the candidate equals Query's for that id. Side effects: may cache the scatter cell.
        /// </remarks>
        public bool TryGet(long id, out DestructibleCandidate candidate)
        {
            if (id >= FieldIdBase)
            {
                long i = id - FieldIdBase;
                candidate = i < _field.Count ? FieldCandidate((int)i) : default;
                return i < _field.Count;
            }
            if (IsTreasure(id))
            {
                int region = (int)(id - TreasureIdBase);
                bool ok = _treasureRegions.Contains(region);
                candidate = ok ? TreasureCandidate(region) : default;
                return ok;
            }

            if (IsBuilding(id))
            {
                long index = id - BuildingIdBase;
                bool ok = index < _layout.Buildings.Count;
                candidate = ok ? BuildingCandidate((int)index) : default;
                return ok;
            }

            if (id >= 0 && id < _scatter.KeyCount &&
                _scatter.TryGetItem(id, out ScatterItem it))
            {
                candidate = ScatterCandidate(it);
                return true;
            }

            candidate = default;
            return false;
        }

        private DestructibleCandidate ScatterCandidate(in ScatterItem it) =>
            new DestructibleCandidate(it.CellKey, _planet.ToWorld(it.Direction, it.AltitudeM + HitHeight(it.Shape, it.SizeM)),
                HitRadius(it.Shape, it.SizeM), _kindComposition[it.KindIndex], it.VolumeL);

        private DestructibleCandidate TreasureCandidate(int region)
        {
            float3 dir = _plan.Regions[region].Center;
            return new DestructibleCandidate(TreasureIdBase + region,
                _planet.ToWorld(dir, _groundAltitude(dir) + 0.6f),
                TreasureHitRadiusM, _env.Building(BuildingMaterial.House), TreasureVolumeL);
        }

        private DestructibleCandidate BuildingCandidate(int i)
        {
            RunBuilding b = _layout.Buildings[i];
            RunSettlement s = _layout.Settlements[b.Settlement];
            float baseAlt = s.Domain == SettlementDomain.SkyIsland ? s.AltitudeM : _groundAltitude(b.Direction);
            float storeyH = b.Storeys * StoreyM;
            long volumeL = (long)math.round(b.WidthM * b.DepthM * storeyH * 1000f);
            return new DestructibleCandidate(BuildingIdBase + i,
                _planet.ToWorld(b.Direction, baseAlt + storeyH * 0.5f),
                BuildingHitRadius(b), _env.Building(MaterialOf(i)), (int)math.min(volumeL, int.MaxValue));
        }

        /// <summary>Whether a body of radius <paramref name="clearanceM"/> at <paramref name="direction"/> overlaps an intact building, tree, shrub, cane clump, rock or crystal.</summary>
        /// <remarks>
        /// REQ: destructibles.md#碰撞 — whole trees/shrubs/rocks block creatures (trunk/body radius), destroyed ones and
        /// grass do not.
        /// Pre: direction finite and non-zero; clearanceM finite and ≥ 0.
        /// Post: deterministic for a given input and destroyed set. Side effects: caches scatter cells.
        /// </remarks>
        public bool IsBlocked(float3 direction, float clearanceM)
        {
            if (_clearance.IsInsideBuilding(direction, clearanceM))
            {
                return true;
            }

            float3 up = math.normalize(direction);
            for (int i = 0; i < _field.Count; i++)
            {
                var item = _field[i];
                if (item.Solid && math.distance(up, item.Direction) * _planet.Radius <= clearanceM + item.Radius &&
                    !_table.IsDestroyed(FieldIdBase + i)) return true;
            }
            _items.Clear();
            _scatter.Query(up, clearanceM + _scatter.MaxBlockRadiusM, _items);
            foreach (ScatterItem it in _items)
            {
                if (it.Blocks && math.distance(up, it.Direction) * _planet.Radius <= it.BlockRadiusM + clearanceM &&
                    !_table.IsDestroyed(it.CellKey))
                {
                    return true;
                }
            }

            for (int i = 0; i < _treasureRegions.Count; i++)
            {
                int r = _treasureRegions[i];
                if (math.distance(up, _plan.Regions[r].Center) * _planet.Radius <= TreasureHitRadiusM + clearanceM &&
                    !_table.IsDestroyed(TreasureIdBase + r))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>UI copy key naming what <paramref name="candidate"/> is (target HUD line).</summary>
        /// <remarks>
        /// Pre: candidate came from this instance's Query.
        /// Post: ui.object.building, ui.object.&lt;species id&gt; for scatter, or the field object category.
        /// Side effects: caches scatter cells; allocates nothing beyond Query's own caching.
        /// </remarks>
        public string NameKey(in DestructibleCandidate candidate)
        {
            if (candidate.Id >= FieldIdBase)
            {
                var item = _field[(int)(candidate.Id - FieldIdBase)];
                return item.Animal ? "ui.object.fauna" : item.Form <= FieldForm.Hoist ? "ui.object.machine" : "ui.object.vehicle";
            }
            if (IsBuilding(candidate.Id) || IsTreasure(candidate.Id))
            {
                return "ui.object.building";
            }

            _items.Clear();
            _scatter.Query(_planet.ToDirection(candidate.Center), 1f, _items);
            foreach (ScatterItem it in _items)
            {
                if (it.CellKey == candidate.Id)
                {
                    return _kindNameKey[it.KindIndex];
                }
            }

            Contract.Requires(false, "candidate " + candidate.Id + " is not a scatter item of this run");
            return "ui.object.tree";
        }

        /// <summary>Whether a hit can knock object <paramref name="id"/> loose before it is destroyed.</summary>
        /// <remarks>
        /// Grass is rooted and has no body to roll: it only bends until it is destroyed (and has no collider to wake).
        /// Pre: none. Post: field objects and non-blocking scatter cannot be shoved. Side effects: may cache the scatter cell.
        /// </remarks>
        public bool CanBeShoved(long id) =>
            id < FieldIdBase && (id < 0 || id >= _scatter.KeyCount || !_scatter.TryGetItem(id, out ScatterItem it) || it.Blocks);

        /// <summary>Applies a destruction to generation-side state: a destroyed building stops blocking.</summary>
        /// <remarks>
        /// Pre: id was reported destroyed by the encounter. Post: IsBlocked ignores it (scatter reads the table directly).
        /// Side effects: mutates the layout clearance index.
        /// </remarks>
        public void OnDestroyed(long id)
        {
            if (IsBuilding(id))
            {
                _clearance.RemoveBuilding((int)(id - BuildingIdBase));
            }
        }

        private BuildingMaterial MaterialOf(int index)
        {
            RunBuilding b = _layout.Buildings[index];
            if (_layout.Settlements[b.Settlement].Kind == SettlementKind.Ruins)
            {
                return BuildingMaterial.RuinWall;
            }

            return b.Kind switch
            {
                BuildingKind.Tent => BuildingMaterial.Tent,
                BuildingKind.House => BuildingMaterial.House,
                BuildingKind.Hall => BuildingMaterial.Hall,
                BuildingKind.Tower => BuildingMaterial.Tower,
                _ => BuildingMaterial.Pillar,
            };
        }

        private static float BuildingHitRadius(RunBuilding b) => 0.5f * math.max(b.WidthM, b.DepthM);

        // Hit spheres follow ScatterField's placeholder silhouettes: a tree is hit through its crown as well as its trunk.
        private static float HitRadius(ScatterShape shape, float size) => shape switch
        {
            ScatterShape.Tree => size * 0.3f,
            ScatterShape.Rock => size * 0.45f,
            ScatterShape.Shrub => size * 0.45f,
            ScatterShape.Bamboo => size * 0.3f,
            ScatterShape.TallGrass => size * 0.35f,
            ScatterShape.Log => size * 0.4f,
            ScatterShape.Mushroom => size * 0.3f,
            ScatterShape.OreVein => size * 0.42f,
            ScatterShape.Fern => size * 0.4f,
            ScatterShape.Flowers => size * 0.45f,
            _ => size * 0.25f,
        };

        // Living plants a creature can forage in (Food gathering); deadwood, cacti and mushrooms are not foliage.
        private static bool IsFoliage(ScatterShape shape) =>
            shape == ScatterShape.Tree || shape == ScatterShape.Shrub || shape == ScatterShape.Bamboo ||
            shape == ScatterShape.TallGrass || shape == ScatterShape.Fern || shape == ScatterShape.Reed ||
            shape == ScatterShape.Flowers;

        private static float HitHeight(ScatterShape shape, float size) => shape switch
        {
            ScatterShape.Tree => size * 0.5f,
            ScatterShape.Rock => size * 0.25f,
            ScatterShape.Shrub => size * 0.35f,
            ScatterShape.TallGrass => size * 0.4f,
            ScatterShape.Log => size * 0.12f,
            ScatterShape.OreVein => size * 0.3f,
            ScatterShape.Fern => size * 0.2f,
            ScatterShape.Flowers => size * 0.12f,
            _ => size * 0.5f,
        };
    }
}
