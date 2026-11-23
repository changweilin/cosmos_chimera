using System.IO;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.WorldGen;
using UMA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cosmimera.EditorTools
{
    /// <summary>Renders each NPC base as the procedural resident (before) beside the same resident on its UMA race (after).</summary>
    public static class NpcUmaCapture
    {
        const int Width = 1200;
        const int Height = 900;

        /// <summary>Writes Logs/npc-uma/&lt;id&gt;.png for every built race: left the procedural mesh, right the UMA avatar.</summary>
        /// <remarks>
        /// Pre: tools/build-npc-races has run; a GPU is available (do not pass -nographics).
        /// Post: one PNG per race and sex; the resident is a typical adult (age 0.4, health 0.85, mean stature and bulk),
        /// the UMA side carries that resident's shape DNA. Logs each file.
        /// Side effects: opens a new unsaved scene; writes PNGs under Logs/npc-uma/.
        /// </remarks>
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The first render of a shader variant otherwise draws the cyan async-compile placeholder.
            ShaderUtil.allowAsyncCompilation = false;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, 152f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            var rt = new RenderTexture(Width, Height, 24);
            cam.targetTexture = rt;
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Material[] before =
            {
                PlanetLook.SceneryMaterial(scenery, Color.white, Color.white, 0f, 0f, 0f, 0f),
                PlanetLook.SceneryMaterial(scenery, Color.white, Color.white, 0f, 0f, 0f, 1.2f),
            };
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "npc-uma");
            Directory.CreateDirectory(dir);

            var gen = UMAAssetIndexer.Instance.Generator;
            var settings = ScriptableObject.CreateInstance<UMARendererAsset>();
            var merge = ScriptableObject.CreateInstance<TextureMerge>();
            var combiner = gen.gameObject.AddComponent<UMADefaultMeshCombiner>();
            var (oldCombiner, oldRenderer, oldMerge) = (gen.meshCombiner, gen.defaultRendererAsset, gen.textureMerge);
            (gen.meshCombiner, gen.defaultRendererAsset, gen.textureMerge) = (combiner, settings, merge);
            try
            {
                foreach (NpcRace race in System.Enum.GetValues(typeof(NpcRace)))
                {
                    if (race == NpcRace.Skeleton)
                        continue;
                    foreach (NpcSex sex in new[] { NpcSex.Male, NpcSex.Female })
                    {
                        string id = "npc_" + race.ToString().ToLowerInvariant() + "_" + sex.ToString().ToLowerInvariant();
                        var data = AssetDatabase.LoadAssetAtPath<RaceData>($"{NpcUmaRaces.RacesDir}/{id}/{id}.asset");
                        if (data == null)
                        {
                            Debug.LogError($"[NpcUmaCapture] {id}: no race; run tools/build-npc-races");
                            continue;
                        }
                        var spec = new NpcSpec(race, NpcRole.None, sex, 0.4f, 0.85f, 0f, 0f, ArchRegion.Verdant, ArchEra.Agrarian,
                            ArchRegion.Verdant, ArchEra.Agrarian, 0f, 700ul + (ulong)race * 2 + (ulong)sex);
                        Shoot(cam, rt, before, data, spec, Path.Combine(dir, id + ".png"));
                    }
                }
            }
            finally
            {
                (gen.meshCombiner, gen.defaultRendererAsset, gen.textureMerge) = (oldCombiner, oldRenderer, oldMerge);
                Object.DestroyImmediate(combiner);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(merge);
            }
        }

        static void Shoot(Camera cam, RenderTexture rt, Material[] beforeMats, RaceData data, NpcSpec spec, string path)
        {
            // Side by side at one scale, turned a little toward the camera so both the face and the profile read.
            var turn = Quaternion.Euler(0f, 180f - 20f, 0f);
            var oldGo = new GameObject("before");
            oldGo.transform.SetPositionAndRotation(new Vector3(-0.5f, 0f, 0f), turn);
            oldGo.AddComponent<MeshFilter>().sharedMesh = NpcMeshes.Build(spec);
            oldGo.AddComponent<MeshRenderer>().sharedMaterials = beforeMats;

            var newGo = new GameObject("after");
            var uma = newGo.AddComponent<UMAData>();
            try
            {
                uma.umaRecipe = new UMAData.UMARecipe();
                uma.umaRecipe.SetRace(data);
                data.baseRaceRecipe.Load(uma.umaRecipe);
                uma.umaRecipe.dnaInstanceCollection = data.DNACollection.GetDefaultDNA(data);
                uma.blendShapeSettings.ignoreBlendShapes = false;
                uma.blendShapeSettings.loadAllFrames = true;
                uma.isMeshDirty = uma.isTextureDirty = uma.isShapeDirty = true;
                UMAAssetIndexer.Instance.Generator.GenerateSingleUMA(uma, false);
                NpcUmaRecipe recipe = NpcMeshes.UmaRecipeOf(spec);
                data.DNACollection.LoadDictionary();
                for (int i = 0; i < NpcMeshes.UmaChannels.Count; i++)
                {
                    var c = NpcMeshes.UmaChannels[i];
                    data.DNACollection.dnaDictionary[c.Name].PostApply(uma, Mathf.Clamp01(c.Dna01(recipe.Dna(i))));
                }
                // The UMA body stands in its T-pose bind pose, so it gets the wider half of the frame.
                newGo.transform.SetPositionAndRotation(new Vector3(1.0f, 0f, 0f), turn);

                var bounds = new Bounds(Vector3.up, Vector3.zero);
                foreach (var r in oldGo.GetComponentsInChildren<Renderer>().Concat(newGo.GetComponentsInChildren<Renderer>()))
                    bounds.Encapsulate(r.bounds);
                float top = Mathf.Max(bounds.max.y, 0.5f);
                cam.orthographicSize = 0.54f * Mathf.Max(top, bounds.size.x * Height / Width);
                cam.transform.SetPositionAndRotation(new Vector3(bounds.center.x, top * 0.5f, -10f), Quaternion.identity);
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[NpcUmaCapture] {path} before={NpcMeshes.HeightM(spec):0.00}m after={newGo.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).DefaultIfEmpty(0f).Max():0.00}m");
            }
            finally
            {
                Object.DestroyImmediate(oldGo.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(oldGo);
                Object.DestroyImmediate(newGo);
            }
        }
    }
}
