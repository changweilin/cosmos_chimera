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
    /// Joins world generation (scatter, buildings) to the combat rule layer: the destructible lookup moves hit, and the
    /// blocking test creatures obey. REQ: docs/design/destructibles.md (碰撞, 狀態保留, 實作分層), ADR-0009.
    /// </summary>
    /// <remarks>
    /// Ids: a scatter item is its cell key (≥ 0, below 6·n²); building i is <see cref="BuildingIdBase"/> + i. Both are
    /// derived from the generation result, so every peer and every LOD rebuild agrees.
    /// Pre: built once per run on the host. Post: see members. Side effects: caches scatter cells (inside the planner).
    /// </remarks>
    public sealed class WorldObjects : IDestructibleIndex
    {
        /// <summary>First building id; far above any scatter cell key (6·n² &lt; 2^32 for R ≤ 1e5 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const long BuildingIdBase = 1L << 40;

        private const float StoreyM = 3f; // SettlementView.StoreyM: HP and drop odds follow the drawn volume

        private readonly PlanetConfig _planet;
        private readonly ScatterPlanner _scatter;
        private readonly RunLayout _layout;
        private readonly LayoutClearance _clearance;
        private readonly Func<float3, float> _groundAltitude;
        private readonly EnvMaterials _env;
        private readonly DestructibleTable _table;
        private readonly Composition[] _kindComposition;
        private readonly float _maxBuildingHitM;
        private readonly List<ScatterItem> _items = new List<ScatterItem>(16);

        /// <summary>Creates the bridge.</summary>
        /// <remarks>
        /// Pre: every argument non-null and from the same run (groundAltitude = the terrain the scatter planner uses);
        ///      every scatter kind's substance id exists in env.
        /// Post: Query and IsBlocked answer for this run; MaxHitRadiusM covers every scatter item and building.
        /// Side effects: allocates.
        /// </remarks>
        public WorldObjects(in PlanetConfig planet, ScatterPlanner scatter, RunLayout layout, LayoutClearance clearance,
            Func<float3, float> groundAltitude, EnvMaterials env, DestructibleTable table)
        {
            Contract.Requires(scatter != null && layout != null && clearance != null && groundAltitude != null &&
                env != null && table != null, "every argument is required");
            _groundAltitude = groundAltitude;
            _planet = planet;
            _scatter = scatter;
            _layout = layout;
            _clearance = clearance;
            _env = env;
            _table = table;
            _kindComposition = new Composition[scatter.Kinds.Count];
            float maxScatter = 0f;
            for (int k = 0; k < _kindComposition.Length; k++)
            {
                ScatterKind kind = scatter.Kinds[k];
                Contract.Requires(env.TryGetSubstance(kind.SubstanceId, out Substance sub),
                    "scatter substance " + kind.SubstanceId + " missing from env-materials");
                _kindComposition[k] = Composition.Of(sub);
                maxScatter = math.max(maxScatter, HitRadius(kind.Shape, kind.MaxSizeM));
            }

            foreach (RunBuilding b in layout.Buildings)
            {
                _maxBuildingHitM = math.max(_maxBuildingHitM, BuildingHitRadius(b));
            }

            MaxHitRadiusM = math.max(maxScatter, math.max(_maxBuildingHitM, 0.1f));
        }

        /// <inheritdoc />
        public float MaxHitRadiusM { get; }

        /// <summary>Whether <paramref name="id"/> names a building.</summary>
        /// <remarks>Pre: none. Post: true iff id ≥ BuildingIdBase. Side effects: none.</remarks>
        public static bool IsBuilding(long id) => id >= BuildingIdBase;

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

                into.Add(BuildingCandidate(i));
            }
        }

        /// <summary>The candidate Query would report for <paramref name="id"/>, if that object exists in this run.</summary>
        /// <remarks>
        /// Pre: none. Post: true iff id is a building index or a scatter cell holding an item (destroyed ones included);
        ///      the candidate equals Query's for that id. Side effects: may cache the scatter cell.
        /// </remarks>
        public bool TryGet(long id, out DestructibleCandidate candidate)
        {
            if (IsBuilding(id))
            {
                long index = id - BuildingIdBase;
                bool ok = index < _layout.Buildings.Count;
                candidate = ok ? BuildingCandidate((int)index) : default;
                return ok;
            }

            if (id >= 0 && id < 6L * _scatter.CellsPerFace * _scatter.CellsPerFace &&
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

        private DestructibleCandidate BuildingCandidate(int i)
        {
            RunBuilding b = _layout.Buildings[i];
            float storeyH = b.Storeys * StoreyM;
            long volumeL = (long)math.round(b.WidthM * b.DepthM * storeyH * 1000f);
            return new DestructibleCandidate(BuildingIdBase + i,
                _planet.ToWorld(b.Direction, _groundAltitude(b.Direction) + storeyH * 0.5f),
                BuildingHitRadius(b), _env.Building(MaterialOf(i)), (int)math.min(volumeL, int.MaxValue));
        }

        /// <summary>Whether a body of radius <paramref name="clearanceM"/> at <paramref name="direction"/> overlaps an intact building, tree, rock or crystal.</summary>
        /// <remarks>
        /// REQ: destructibles.md#碰撞 — whole trees/rocks block creatures (trunk/body radius), destroyed ones do not.
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
            _items.Clear();
            _scatter.Query(up, clearanceM + _scatter.MaxBlockRadiusM, _items);
            foreach (ScatterItem it in _items)
            {
                if (math.distance(up, it.Direction) * _planet.Radius <= it.BlockRadiusM + clearanceM &&
                    !_table.IsDestroyed(it.CellKey))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>UI copy key naming what <paramref name="candidate"/> is (target HUD line).</summary>
        /// <remarks>
        /// Pre: candidate came from this instance's Query. Post: one of ui.object.building / tree / rock / crystal.
        /// Side effects: caches scatter cells; allocates nothing beyond Query's own caching.
        /// </remarks>
        public string NameKey(in DestructibleCandidate candidate)
        {
            if (IsBuilding(candidate.Id))
            {
                return "ui.object.building";
            }

            _items.Clear();
            _scatter.Query(_planet.ToDirection(candidate.Center), 1f, _items);
            foreach (ScatterItem it in _items)
            {
                if (it.CellKey == candidate.Id)
                {
                    return it.Shape switch
                    {
                        ScatterShape.Rock => "ui.object.rock",
                        ScatterShape.Crystal => "ui.object.crystal",
                        ScatterShape.Tree => "ui.object.tree",
                        _ => throw new ArgumentOutOfRangeException(nameof(candidate), it.Shape, "unnamed scatter shape"),
                    };
                }
            }

            Contract.Requires(false, "candidate " + candidate.Id + " is not a scatter item of this run");
            return "ui.object.tree";
        }

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
            _ => size * 0.25f,
        };

        private static float HitHeight(ScatterShape shape, float size) => shape switch
        {
            ScatterShape.Tree => size * 0.5f,
            ScatterShape.Rock => size * 0.25f,
            _ => size * 0.5f,
        };
    }
}
