using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: phase-1.md P1-15 — tree and rock silhouettes stay on screen past the near scatter field.
    public sealed class FarSceneryTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();
        private static readonly PlanetConfig Planet = new PlanetConfig(640f, float3.zero);
        private const float FarM = 220f;

        private (FarScenery far, ScatterField field, ScatterPlanner planner) Layer(System.Func<float3> focus,
            HashSet<long> destroyed = null)
        {
            var root = new GameObject("far-test");
            _roots.Add(root);
            var planner = new ScatterPlanner(Planet, 12345, "verdant_planet", _ => 0f, null, null, _ => 0.8f,
                (d, roll) => ScatterBiomes.BiomeOf("forest"));
            ScatterField field = root.AddComponent<ScatterField>();
            field.enabled = false;
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Assert.That(scenery, Is.Not.Null);
            field.Configure(Planet, planner, "verdant_planet", focus, Shader.Find("Cosmimera/ToonOutlined"),
                destroyed != null ? destroyed.Contains : (System.Func<long, bool>)null, scenery);
            FarScenery far = root.AddComponent<FarScenery>();
            far.Configure(Planet, field, planner, focus, scenery, FarM);
            return (far, field, planner);
        }

        private static IEnumerator Settle(FarScenery far)
        {
            yield return null;
            for (int f = 0; f < 600 && far.PendingTileCount > 0; f++)
            {
                yield return null;
            }

            Assert.That(far.PendingTileCount, Is.EqualTo(0), "the layer catches up with a still focus");
        }

        private static int Vertices(FarScenery far)
        {
            int n = 0;
            foreach (MeshFilter filter in far.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name.StartsWith("FarTile_")) n += filter.sharedMesh.vertexCount;
            }

            return n;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in _roots) Object.Destroy(root);
            _roots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tiles_around_the_focus_fill_in_a_few_per_frame_and_draw_every_far_item()
        {
            float3 up = math.normalize(new float3(1f, 0.2f, 0.1f));
            (FarScenery far, ScatterField field, ScatterPlanner planner) = Layer(() => Planet.ToWorld(up, 0f));
            yield return null;
            Assert.That(far.BuiltTileCount, Is.InRange(1, FarScenery.TilesPerFrame), "never more than the per-frame budget");
            yield return Settle(far);
            Assert.That(far.BuiltTileCount, Is.GreaterThan(10));

            // Every tree the rule layer places within the far radius is in some tile, at its own ground point.
            var anchors = new HashSet<Vector3>();
            var buffer = new List<Vector3>();
            foreach (MeshFilter filter in far.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderer.sharedMaterial.GetVector("_FarFade").y, Is.GreaterThan(ScatterField.RadiusM));
                Assert.That(renderer.sharedMaterial.GetVector("_FarFade").w, Is.EqualTo(FarM));
                filter.sharedMesh.GetUVs(2, buffer);
                Assert.That(buffer.Count, Is.EqualTo(filter.sharedMesh.vertexCount));
                foreach (Vector3 a in buffer) anchors.Add(a);
            }

            var items = new List<ScatterItem>();
            planner.Query(up, FarM, items);
            int trees = 0;
            foreach (ScatterItem item in items)
            {
                if (item.Shape != ScatterShape.Tree) continue;
                trees++;
                float3 world = Planet.ToWorld(item.Direction, item.AltitudeM - 0.1f);
                Assert.That(anchors.Contains(new Vector3(world.x, world.y, world.z)), Is.True, "tree " + item.CellKey);
            }

            Assert.That(trees, Is.GreaterThan(300));
            Assert.That((Vector3)Shader.GetGlobalVector("_CosmiFarFocus"), Is.EqualTo((Vector3)Planet.ToWorld(up, 0f)));
        }

        [UnityTest]
        public IEnumerator Tiles_follow_the_focus_and_leave_nothing_behind()
        {
            float3 up = new float3(1f, 0f, 0f);
            (FarScenery far, _, _) = Layer(() => Planet.ToWorld(up, 0f));
            yield return Settle(far);
            var before = new HashSet<string>();
            foreach (MeshFilter filter in far.GetComponentsInChildren<MeshFilter>()) before.Add(filter.name);

            up = new float3(-1f, 0f, 0f);
            yield return Settle(far);
            yield return null;
            int kept = 0;
            foreach (MeshFilter filter in far.GetComponentsInChildren<MeshFilter>())
            {
                kept += before.Contains(filter.name) ? 1 : 0;
            }

            Assert.That(kept, Is.EqualTo(0), "tiles on the far side of the planet are destroyed");
            Assert.That(far.BuiltTileCount, Is.GreaterThan(10));
        }

        [UnityTest]
        public IEnumerator A_destroyed_item_leaves_its_tile()
        {
            float3 up = math.normalize(new float3(0.3f, 1f, 0.2f));
            var destroyed = new HashSet<long>();
            (FarScenery far, ScatterField field, ScatterPlanner planner) = Layer(() => Planet.ToWorld(up, 0f), destroyed);
            yield return Settle(far);
            int full = Vertices(far);
            var items = new List<ScatterItem>();
            planner.Query(up, 60f, items);
            ScatterItem tree = items.Find(i => i.Shape == ScatterShape.Tree);
            Assert.That(field.TryGetFarItem(tree.CellKey, out _, out FarShape shape, out _, out _, out _), Is.True);

            destroyed.Add(tree.CellKey);
            field.Remove(tree.CellKey);
            Assert.That(far.PendingTileCount, Is.EqualTo(1), "only the item's tile is rebuilt");
            yield return Settle(far);
            Assert.That(Vertices(far), Is.EqualTo(full - FarSceneryMeshes.VertexCount(shape)));
            Assert.That(field.TryGetFarItem(tree.CellKey, out _, out _, out _, out _, out _), Is.False);
        }

        [Test]
        public void Contract_violations_throw()
        {
            (FarScenery far, ScatterField field, ScatterPlanner planner) = Layer(() => Planet.ToWorld(new float3(0f, 1f, 0f), 0f));
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Assert.Throws<ContractException>(() => far.Configure(Planet, null, planner, () => float3.zero, scenery, FarM));
            Assert.Throws<ContractException>(() => far.Configure(Planet, field, planner, () => float3.zero, null, FarM));
            Assert.Throws<ContractException>(() => far.Configure(Planet, field, planner, () => float3.zero, scenery, 100f));
        }
    }
}
