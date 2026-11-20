using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#進階地形地城 牆與頂的美術, combat-feel.md#地城氛圍.
    public sealed class DungeonDressingTests
    {
        private const float Radius = 640f;
        private static readonly string[] Affixes = { "elite_swift", "elite_shielded", "elite_frenzy", "elite_aura" };
        private readonly List<Object> _owned = new List<Object>();
        private GameObject _stage;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_stage != null)
            {
                Object.Destroy(_stage);
            }

            foreach (Object o in _owned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _owned.Clear();
            RenderSettings.fog = false;
            yield return null;
        }

        [Test]
        public void Every_terrain_builds_walls_and_a_lamp_with_three_sub_meshes_and_16_bit_indices()
        {
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                RoomDungeonGeometry g = GeometryFor(spec, 2);
                var config = new PlanetConfig(g.Radius, float3.zero);
                List<Mesh> meshes = DungeonDressing.BuildWalls(g, spec.Id, CultureFor(spec.Id, 7), config, _ => 0f, config.ToWorld(g.Anchor, 0f));
                _owned.AddRange(meshes);
                Assert.That(meshes.Count, Is.GreaterThan(0), spec.Id);
                foreach (Mesh m in meshes)
                {
                    Assert.That(m.subMeshCount, Is.EqualTo(DungeonDressing.SubCount), spec.Id);
                    Assert.That(m.vertexCount, Is.LessThan(65536), spec.Id);
                }

                Mesh lamp = DungeonDressing.BuildLamp(spec.Id, out float lightHeight);
                _owned.Add(lamp);
                Assert.That(lamp.subMeshCount, Is.EqualTo(DungeonDressing.SubCount), spec.Id);
                Assert.That(lightHeight, Is.GreaterThan(0f).And.LessThan(DungeonDressing.PanelTopM), spec.Id);
                Assert.That(DungeonDressing.HasCeiling(spec.Id), Is.EqualTo(spec.Form == AdvancedDungeonForm.Rooms), spec.Id);
            }
        }

        [Test]
        public void Same_dungeon_gives_the_same_wall_meshes()
        {
            AdvancedTerrains.TryGet("temple", out AdvancedTerrainSpec spec);
            RoomDungeonGeometry g = GeometryFor(spec, 5);
            var config = new PlanetConfig(g.Radius, float3.zero);
            List<Mesh> a = DungeonDressing.BuildWalls(g, spec.Id, CultureFor(spec.Id, 7), config, _ => 0f, config.ToWorld(g.Anchor, 0f));
            List<Mesh> b = DungeonDressing.BuildWalls(g, spec.Id, CultureFor(spec.Id, 7), config, _ => 0f, config.ToWorld(g.Anchor, 0f));
            _owned.AddRange(a);
            _owned.AddRange(b);
            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].vertices, Is.EqualTo(b[i].vertices));
            }
        }

        [Test]
        public void Builders_change_the_dressing_of_the_same_dungeon()
        {
            foreach (string id in new[] { "rainforest", "laboratory" })
            {
                AdvancedTerrains.TryGet(id, out AdvancedTerrainSpec spec);
                RoomDungeonGeometry g = GeometryFor(spec, 4);
                var config = new PlanetConfig(g.Radius, float3.zero);
                DungeonCulture a = CultureFor(id, 1);
                DungeonCulture b = a;
                for (ulong seed = 2; b.Era == a.Era || b.Look == a.Look; seed++)
                {
                    b = CultureFor(id, seed);
                }

                int va = 0, vb = 0;
                foreach (Mesh m in DungeonDressing.BuildWalls(g, id, a, config, _ => 0f, config.ToWorld(g.Anchor, 0f)))
                {
                    _owned.Add(m);
                    va += m.vertexCount;
                    Assert.That(m.vertexCount, Is.LessThan(65536), id);
                }

                foreach (Mesh m in DungeonDressing.BuildWalls(g, id, b, config, _ => 0f, config.ToWorld(g.Anchor, 0f)))
                {
                    _owned.Add(m);
                    vb += m.vertexCount;
                    Assert.That(m.vertexCount, Is.LessThan(65536), id);
                }

                Assert.That(vb, Is.Not.EqualTo(va), id);
            }
        }

        private static DungeonCulture CultureFor(string terrainId, ulong seed) =>
            DungeonCulture.For(ArchitectureMeshes.PlanetStyleFor("twilight_planet", seed), terrainId, seed);

        // Visual proof only: Logs/dungeon-<terrain>.png (indoors from the entrance room with the lamps lit; surface
        // mazes from above the plaza edge).
        [UnityTest]
        public IEnumerator Capture_dungeon_dressing_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Assert.That(scenery, Is.Not.Null);
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            ulong cultureSeed = 0;
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                // A different builder culture per terrain so one capture run shows several eras and looks.
                DungeonCulture culture = CultureFor(spec.Id, 11 + 7 * cultureSeed++);
                Debug.Log($"dungeon-{spec.Id}: {culture.Region} {culture.Era} look {culture.Look} accent {culture.AccentRegion} " +
                    $"{culture.AccentEra} age {culture.Age:0.00} damage {culture.Damage:0.00}");
                _stage = new GameObject("dungeon-" + spec.Id);
                RoomDungeonGeometry g = GeometryFor(spec, 3);
                var config = new PlanetConfig(g.Radius, float3.zero);
                float3 origin = config.ToWorld(g.Anchor, 0f);
                DungeonDressing.Palette pal = DungeonDressing.PaletteFor(spec.Id, culture);
                Material[] mats =
                {
                    PlanetLook.SceneryMaterial(scenery, pal.Body, Color.white, 0f, 0f, 0f, 0f),
                    PlanetLook.SceneryMaterial(scenery, pal.Trim, Color.white, 0f, 0f, 0f, 0f),
                    PlanetLook.SceneryMaterial(scenery, pal.Glow, Color.white, 0f, 0f, 0f, pal.GlowStrength),
                };
                _owned.AddRange(mats);
                foreach (Mesh m in DungeonDressing.BuildWalls(g, spec.Id, culture, config, _ => 0f, origin))
                {
                    _owned.Add(m);
                    var go = new GameObject("walls");
                    go.transform.SetParent(_stage.transform, false);
                    go.transform.position = (Vector3)origin;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }

                Mesh ground = GroundCap(g, config, origin);
                _owned.Add(ground);
                var groundGo = new GameObject("ground");
                groundGo.transform.SetParent(_stage.transform, false);
                groundGo.transform.position = (Vector3)origin;
                groundGo.AddComponent<MeshFilter>().sharedMesh = ground;
                Material groundMat = PlanetLook.SceneryMaterial(scenery, pal.Body * 0.8f, Color.white, 0f, 0f, 0f, 0f);
                _owned.Add(groundMat);
                groundGo.AddComponent<MeshRenderer>().sharedMaterial = groundMat;

                var sun = new GameObject("sun").AddComponent<Light>();
                sun.transform.SetParent(_stage.transform, false);
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.2f;
                sun.color = new Color(1f, 0.96f, 0.88f);
                float3 up = g.Anchor;
                PlanetFrame f = PlanetFrame.FromUp(up);
                sun.transform.rotation = Quaternion.LookRotation((Vector3)math.normalize(-up * 1.2f + f.East * 0.6f + f.North * 0.4f),
                    (Vector3)f.North);
                RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 200f;
                RenderSettings.fogEndDistance = 600f;
                RenderSettings.fogColor = new Color(0.62f, 0.8f, 0.94f);

                var camera = new GameObject("camera").AddComponent<Camera>();
                camera.transform.SetParent(_stage.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1500f;
                bool indoor = DungeonDressing.HasCeiling(spec.Id);
                float2 eye2, look2;
                float eyeH, lookH;
                if (indoor)
                {
                    float2 entrance = Local(g, g.RoomCenter(g.Dungeon.EntranceRoom));
                    int door = FirstDoor(g.Dungeon.Rooms[g.Dungeon.EntranceRoom].Doors);
                    float2 axis = new float2(door == RoomDungeon.East ? 1 : door == RoomDungeon.West ? -1 : 0,
                        door == RoomDungeon.North ? 1 : door == RoomDungeon.South ? -1 : 0);
                    eye2 = entrance - axis * 8f + new float2(axis.y, -axis.x) * 4f;
                    look2 = entrance + axis * 16f;
                    eyeH = 2.2f;
                    lookH = 3f;
                }
                else
                {
                    float2 plaza = g.ArenaCenterLocal;
                    eye2 = plaza + g.ArenaGateAxis * (g.ArenaRadiusM + 40f) + new float2(12f, 0f);
                    look2 = plaza + g.ArenaGateAxis * (g.ArenaRadiusM * 0.6f);
                    eyeH = 26f;
                    lookH = 2f;
                }

                Vector3 eye = (Vector3)config.ToWorld(g.LocalToDirection(eye2), eyeH);
                Vector3 target = (Vector3)config.ToWorld(g.LocalToDirection(look2), lookH);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, (Vector3)g.LocalToDirection(eye2)));

                DungeonAtmosphere mood = _stage.AddComponent<DungeonAtmosphere>();
                mood.Configure(camera, sun, camera.transform, DungeonMood.For(spec.Id));
                if (indoor)
                {
                    Mesh lamp = DungeonDressing.BuildLamp(spec.Id, out float lightHeight);
                    _owned.Add(lamp);
                    mood.BuildRoomLights(g, _ => 0f, config, lamp, mats, lightHeight, _stage.transform);
                }

                mood.SetInside(true);
                yield return new WaitForSecondsRealtime(0.8f);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "dungeon-" + spec.Id + ".png"));
                if (!indoor)
                {
                    // Open walls up close: the entrance room of the maze, at eye height.
                    float2 entrance = Local(g, g.RoomCenter(g.Dungeon.EntranceRoom));
                    int door = FirstDoor(g.Dungeon.Rooms[g.Dungeon.EntranceRoom].Doors);
                    float2 axis = new float2(door == RoomDungeon.East ? 1 : door == RoomDungeon.West ? -1 : 0,
                        door == RoomDungeon.North ? 1 : door == RoomDungeon.South ? -1 : 0);
                    float2 across = new float2(axis.y, -axis.x);
                    float half = 0.5f * g.Dungeon.CellSizeM;
                    eye2 = entrance - across * (half * 0.2f) - axis * (half * 0.3f);
                    look2 = entrance + across * half + axis * (half * 0.5f);
                    eye = (Vector3)config.ToWorld(g.LocalToDirection(eye2), 2.4f);
                    target = (Vector3)config.ToWorld(g.LocalToDirection(look2), 5f);
                    camera.transform.SetPositionAndRotation(eye,
                        Quaternion.LookRotation(target - eye, (Vector3)g.LocalToDirection(eye2)));
                    yield return new WaitForSecondsRealtime(0.4f);
                    ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "dungeon-" + spec.Id + "-close.png"));
                }

                Object.Destroy(_stage);
                _stage = null;
                yield return null;
            }
        }

        private static RoomDungeonGeometry GeometryFor(AdvancedTerrainSpec spec, ulong seed)
        {
            float3 anchor = math.normalize(new float3(0.2f, 1f, 0.1f));
            if (spec.Form == AdvancedDungeonForm.SurfaceMaze)
            {
                RoomDungeon maze = RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, spec.MinSize, Affixes, 20f);
                return RoomDungeonGeometry.Build(maze, RadiusFor(maze), anchor, 20f);
            }

            RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, spec.MinSize, Affixes);
            return RoomDungeonGeometry.Build(d, RadiusFor(d), anchor);
        }

        // The capture sphere must hold the biggest grid (a 40 m ice cap maze); every terrain uses the same radius.
        private static float RadiusFor(RoomDungeon d) => math.max(Radius, d.CellSizeM * (math.max(d.Width, d.Height) + 3));

        private static int FirstDoor(int doors)
        {
            for (int s = RoomDungeon.North; s <= RoomDungeon.West; s <<= 1)
            {
                if ((doors & s) != 0)
                {
                    return s;
                }
            }

            return RoomDungeon.North;
        }

        private static float2 Local(RoomDungeonGeometry g, float3 dir)
        {
            PlanetFrame f = PlanetFrame.FromUp(g.Anchor);
            float3 p = dir * (g.Radius / math.dot(dir, g.Anchor));
            return new float2(math.dot(p, f.East), math.dot(p, f.North));
        }

        // A finely tessellated cap of the sphere under the grid (a Unity sphere primitive is far too coarse at 640 m).
        private static Mesh GroundCap(RoomDungeonGeometry g, in PlanetConfig config, float3 origin)
        {
            const int n = 96;
            float half = g.HalfExtentM + 120f;
            var verts = new Vector3[(n + 1) * (n + 1)];
            var tris = new int[n * n * 6];
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    var local = new float2(-half + 2f * half * i / n, -half + 2f * half * j / n);
                    verts[j * (n + 1) + i] = (Vector3)(config.ToWorld(g.LocalToDirection(local), 0f) - origin);
                }
            }

            int t = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i;
                    tris[t++] = a;
                    tris[t++] = a + n + 1;
                    tris[t++] = a + 1;
                    tris[t++] = a + 1;
                    tris[t++] = a + n + 1;
                    tris[t++] = a + n + 2;
                }
            }

            var mesh = new Mesh { vertices = verts, triangles = tris, colors = NewColors(verts.Length) };
            mesh.RecalculateNormals();
            // The winding depends on the frame's handedness; face the cap up either way.
            if (Vector3.Dot(mesh.normals[(n + 1) * (n / 2) + n / 2], (Vector3)g.Anchor) < 0f)
            {
                for (int k = 0; k < tris.Length; k += 3)
                {
                    (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);
                }

                mesh.triangles = tris;
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        private static Color[] NewColors(int count)
        {
            var c = new Color[count];
            for (int i = 0; i < count; i++)
            {
                c[i] = Color.white;
            }

            return c;
        }
    }
}
