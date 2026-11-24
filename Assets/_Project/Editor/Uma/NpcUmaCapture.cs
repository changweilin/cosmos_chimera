using System.Collections.Generic;
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

        /// <summary>Writes Logs/npc-uma/&lt;id&gt;.png for every built race (left the procedural mesh, right the UMA
        /// avatar) and &lt;id&gt;_tpose.png (the UMA avatar alone in its T bind pose, where its clothes are fitted).</summary>
        /// <remarks>
        /// Pre: tools/build-npc-races has run; a GPU is available (do not pass -nographics).
        /// Post: two PNGs per race and sex; the resident is a typical adult (age 0.4, health 0.85, mean stature and bulk).
        /// The UMA side carries that resident's DNA (shape keys and bones) and colours and wears the procedural parts UMA
        /// has no slots for yet (NpcMeshes.BuildParts), skinned to its bones and fitted in the T pose: cloth inside the
        /// skin is pushed out and body triangles under tight cloth are hidden, as UMA wardrobe does. The comparison shot
        /// then poses the skeleton like the procedural rig. Beasts keep the procedural head. Logs each file.
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
                        Shoot(cam, rt, before, data, spec, Path.Combine(dir, id + ".png"), Path.Combine(dir, id + "_tpose.png"));
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

        static void Shoot(Camera cam, RenderTexture rt, Material[] beforeMats, RaceData data, NpcSpec spec, string path, string tposePath)
        {
            // Side by side at one scale, turned a little toward the camera so both the face and the profile read.
            var turn = Quaternion.Euler(0f, 180f - 20f, 0f);
            var oldGo = new GameObject("before");
            oldGo.transform.SetPositionAndRotation(new Vector3(-0.5f, 0f, 0f), turn);
            oldGo.AddComponent<MeshFilter>().sharedMesh = NpcMeshes.Build(spec);
            oldGo.AddComponent<MeshRenderer>().sharedMaterials = beforeMats;

            var newGo = new GameObject("after");
            var uma = newGo.AddComponent<UMAData>();
            Mesh parts = null;
            try
            {
                NpcUmaRecipe recipe = NpcMeshes.UmaRecipeOf(spec);
                uma.umaRecipe = new UMAData.UMARecipe();
                uma.umaRecipe.SetRace(data);
                data.baseRaceRecipe.Load(uma.umaRecipe);
                uma.umaRecipe.dnaInstanceCollection = data.DNACollection.GetDefaultDNA(data);
                var values = uma.umaRecipe.dnaInstanceCollection.ToDictionary();
                for (int i = 0; i < NpcMeshes.UmaChannels.Count; i++)
                {
                    var c = NpcMeshes.UmaChannels[i];
                    values[c.Name].Value = Mathf.Clamp01(c.Dna01(recipe.Dna(i)));
                }
                uma.blendShapeSettings.ignoreBlendShapes = false;
                uma.blendShapeSettings.loadAllFrames = true;
                uma.isMeshDirty = uma.isTextureDirty = uma.isShapeDirty = true;
                UMAAssetIndexer.Instance.Generator.GenerateSingleUMA(uma, false);
                data.DNACollection.LoadDictionary();
                for (int i = 0; i < NpcMeshes.UmaChannels.Count; i++)
                {
                    var c = NpcMeshes.UmaChannels[i];
                    data.DNACollection.dnaDictionary[c.Name].PostApply(uma, Mathf.Clamp01(c.Dna01(recipe.Dna(i))));
                }
                Colour(newGo, recipe);
                parts = NpcMeshes.BuildParts(spec, out var rig);
                var bones = newGo.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var bind = newGo.GetComponentsInChildren<Transform>(true)
                    .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToList();
                bool beast = recipe.HeadSlot != null;
                // Place the parts where the procedural rig wears them: on the skeleton posed like that rig.
                Pose(bones, rig);
                var body = Body.Sample(newGo.GetComponentsInChildren<SkinnedMeshRenderer>(), beast);
                float umaTop = body.Top;
                var segs = Retarget(parts, rig, bones, body);
                var cloth = Bind(newGo, parts, segs, bones, beforeMats, rig.H);
                // Fit in the bind pose, where a wardrobe slot is authored; the posed shot then only moves bones.
                foreach (var (t, pos, rot, scale) in bind)
                {
                    t.localPosition = pos;
                    t.localRotation = rot;
                    t.localScale = scale;
                }
                int hidden = Fit(newGo, cloth, segs, beast, 0.006f * rig.H);
                foreach (var smr in newGo.GetComponentsInChildren<SkinnedMeshRenderer>())
                    smr.updateWhenOffscreen = true;

                oldGo.SetActive(false);
                newGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                using (Freeze(newGo))
                    Render(cam, rt, newGo, tposePath);
                oldGo.SetActive(true);

                Pose(bones, rig);
                // The UMA body stands in the procedural pose now, so both share one frame layout.
                newGo.transform.SetPositionAndRotation(new Vector3(0.6f, 0f, 0f), turn);
                using (Freeze(newGo))
                    Render(cam, rt, null, path);
                Debug.Log($"[NpcUmaCapture] {path} before={NpcMeshes.HeightM(spec):0.00}m after={umaTop:0.00}m " +
                          $"head before={2f * rig.HeadR:0.000}..{rig.Neck.y:0.00} after={body.HeadTop - body.Chin:0.000}..{body.Chin:0.00} hidden={hidden}");
            }
            finally
            {
                Object.DestroyImmediate(oldGo.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(oldGo);
                Object.DestroyImmediate(newGo);
                if (parts != null)
                    Object.DestroyImmediate(parts);
            }
        }

        // A manual camera render inside one editor frame reuses the frame's GPU skinning, so a pose change between two
        // shots would not show. Each shot draws baked copies of the skinned meshes instead; disposing restores them.
        sealed class Frozen : System.IDisposable
        {
            public readonly List<(SkinnedMeshRenderer skin, GameObject still)> Pairs = new List<(SkinnedMeshRenderer, GameObject)>();

            public void Dispose()
            {
                foreach (var (skin, still) in Pairs)
                {
                    Object.DestroyImmediate(still.GetComponent<MeshFilter>().sharedMesh);
                    Object.DestroyImmediate(still);
                    skin.enabled = true;
                }
            }
        }

        static Frozen Freeze(GameObject avatar)
        {
            var frozen = new Frozen();
            var block = new MaterialPropertyBlock();
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled)
                    continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var still = new GameObject(smr.name + "-still");
                still.transform.SetParent(smr.transform, false);
                still.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = still.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                for (int m = 0; m < smr.sharedMaterials.Length; m++)
                {
                    smr.GetPropertyBlock(block, m);
                    mr.SetPropertyBlock(block, m);
                }
                smr.enabled = false;
                frozen.Pairs.Add((smr, still));
            }
            return frozen;
        }

        // Frames every active renderer (or only `only`'s) and writes the camera's view to `path`.
        static void Render(Camera cam, RenderTexture rt, GameObject only, string path)
        {
            var renderers = only != null ? only.GetComponentsInChildren<Renderer>() : Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var bounds = new Bounds(Vector3.up, Vector3.zero);
            foreach (var r in renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy))
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
        }

        // Colours go in as shader parameters (npc-uma-looks.md#原則: no atlas). Nose and lips keep the tint the bases
        // were baked with over their skin (#b88a72 and #9a5a50 over #d8b49a), so a darker skin darkens them too.
        static readonly (string material, int colour, Color tint)[] Tints =
        {
            ("skin_", 0, Color.white),
            ("nose_", 0, new Color(0.852f, 0.767f, 0.741f)),
            ("mouth_", 0, new Color(0.713f, 0.5f, 0.52f)),
            ("brow_", 1, new Color(0.8f, 0.8f, 0.8f)),
            ("lash_", 1, new Color(0.45f, 0.45f, 0.45f)),
        };

        static void Colour(GameObject avatar, NpcUmaRecipe recipe)
        {
            var block = new MaterialPropertyBlock();
            int tinted = 0;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    foreach (var (material, colour, tint) in Tints)
                    {
                        if (mats[i] == null || !mats[i].name.Contains(material))
                            continue;
                        Color c = recipe.Colour(colour) * tint;
                        c.a = 1f;
                        block.Clear();
                        block.SetColor("_BaseColor", c);
                        r.SetPropertyBlock(block, i);
                        tinted++;
                    }
                }
            }
            NpcEyes eyes = recipe.Eyes;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    string name = mats[i].name;
                    block.Clear();
                    if (name.Contains("_eye_"))
                    {
                        block.SetColor("_BaseColor", eyes.Left);
                        block.SetColor("_IrisRight", eyes.Right);
                        block.SetColor("_PupilColor", Color.Lerp(eyes.Left * 0.2f, new Color(0.05f, 0.03f, 0.04f), 0.6f));
                        block.SetColor("_ScleraColor", eyes.Sclera);
                        block.SetFloat("_PupilType", (float)eyes.Type);
                        block.SetFloat("_IrisScale", eyes.IrisScale);
                        block.SetFloat("_PupilScale", eyes.PupilScale);
                        block.SetFloat("_Glow", eyes.Glow);
                    }
                    else if (name.Contains("_sclerashade_"))
                        block.SetColor("_BaseColor", eyes.Sclera * 0.82f);
                    else if (name.Contains("_sclera_"))
                        block.SetColor("_BaseColor", eyes.Sclera);
                    else
                        continue;
                    r.SetPropertyBlock(block, i);
                    tinted++;
                }
            }
            if (tinted == 0)
                Debug.LogError("[NpcUmaCapture] no material took a colour: " + string.Join(", ",
                    avatar.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Select(m => m == null ? "null" : m.name)));
        }

        // The posed UMA body as world points: every vertex, which ones the head bone carries most, and the head's extent.
        sealed class Body
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<bool> OnHead = new List<bool>();
            public float Top, Chin = float.MaxValue, HeadTop = float.MinValue;
            public Vector3 HeadCentre;

            public static Body Sample(SkinnedMeshRenderer[] renderers, bool beast)
            {
                var body = new Body { Top = 0f };
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (var smr in renderers)
                {
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                    var verts = mesh.vertices;
                    var weights = smr.sharedMesh.boneWeights;
                    int head = System.Array.FindIndex(smr.bones, b => b != null && b.name == "head");
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 w = smr.transform.TransformPoint(verts[i]);
                        bool onHead = head >= 0 && i < weights.Length && weights[i].boneIndex0 == head && weights[i].weight0 > 0.5f;
                        body.Top = Mathf.Max(body.Top, w.y);
                        body.Points.Add(w);
                        body.OnHead.Add(onHead);
                        if (onHead)
                        {
                            lo = Vector3.Min(lo, w);
                            hi = Vector3.Max(hi, w);
                        }
                    }
                    Object.DestroyImmediate(mesh);
                }
                body.Chin = lo.y;
                body.HeadTop = hi.y;
                body.HeadCentre = (lo + hi) * 0.5f;
                if (beast)
                {
                    // The head folds away after this; only the rest of the body still has a skin to clear.
                    for (int i = body.Points.Count - 1; i >= 0; i--)
                    {
                        if (body.OnHead[i])
                        {
                            body.Points.RemoveAt(i);
                            body.OnHead.RemoveAt(i);
                        }
                    }
                }
                return body;
            }
        }

        // The procedural rig's limbs hang or hold; the UMA body arrives in its T bind pose. Each limb bone turns so the
        // segment to its child points where the rig's does (left limbs are the ones at negative x in both). Fingers curl
        // first, while the palms still face down: a flat spread hand reads thin next to a sleeve.
        static void Pose(Dictionary<string, Transform> bones, NpcMeshes.Rig rig)
        {
            foreach (var s in new[] { "_l", "_r" })
            {
                float curl = bones["hand" + s].position.x < 0f ? 1f : -1f;
                foreach (var finger in new[] { "index", "middle", "ring", "pinky" })
                {
                    for (int joint = 1; joint <= 3; joint++)
                    {
                        if (bones.TryGetValue($"{finger}_0{joint}{s}", out var bone))
                            bone.rotation = Quaternion.AngleAxis(curl * (joint == 1 ? 30f : 45f), Vector3.forward) * bone.rotation;
                    }
                }
            }
            foreach (var s in new[] { "_l", "_r" })
            {
                bool left = bones["upperarm" + s].position.x < 0f;
                Aim(bones["upperarm" + s], bones["lowerarm" + s], (Vector3)((left ? rig.ElbowL : rig.ElbowR) - (left ? rig.ShoulderL : rig.ShoulderR)));
                Aim(bones["lowerarm" + s], bones["hand" + s], (Vector3)((left ? rig.HandL : rig.HandR) - (left ? rig.ElbowL : rig.ElbowR)));
                left = bones["thigh" + s].position.x < 0f;
                Aim(bones["thigh" + s], bones["calf" + s], (Vector3)((left ? rig.KneeL : rig.KneeR) - (left ? rig.HipL : rig.HipR)));
                Aim(bones["calf" + s], bones["foot" + s], (Vector3)((left ? rig.AnkleL : rig.AnkleR) - (left ? rig.KneeL : rig.KneeR)));
            }
        }

        static void Aim(Transform bone, Transform child, Vector3 want) =>
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, want) * bone.rotation;

        // One bone segment of the procedural rig (A to B) and the same segment on the UMA skeleton, read from its bones
        // whenever asked so it follows the skeleton between the procedural pose and the bind pose.
        sealed class Segment
        {
            public readonly Vector3 A, B;
            public readonly bool Head;
            readonly System.Func<Vector3> _a2, _b2;

            // The bone that carries this segment's parts, and the one past `Switch` along it (a forearm's hand).
            public readonly string Bone, EndBone;
            public readonly float Switch;

            // -1 the left arm, 1 the right arm, 0 elsewhere: which parts may ride it (NpcMeshes.Ride* tags).
            public int ArmSide;

            public bool Carries(int rider) => rider switch
            {
                NpcMeshes.RideBody => ArmSide == 0,
                NpcMeshes.RideArmLeft => ArmSide == -1,
                NpcMeshes.RideArmRight => ArmSide == 1,
                _ => true,
            };

            public Segment(Vector3 a, Vector3 b, System.Func<Vector3> a2, System.Func<Vector3> b2, string bone, bool head = false,
                string endBone = null, float at = 1f)
            {
                A = a;
                B = b;
                _a2 = a2;
                _b2 = b2;
                Bone = bone;
                Head = head;
                EndBone = endBone ?? bone;
                Switch = at;
            }

            public Vector3 A2 => _a2();
            public Vector3 B2 => _b2();

            public float Along(Vector3 p, bool uma)
            {
                Vector3 a = uma ? A2 : A, ab = (uma ? B2 : B) - a;
                return Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            }

            public Vector3 At(float t, bool uma) => uma ? A2 + t * (B2 - A2) : A + t * (B - A);

            public string BoneAt(float t) => t > Switch ? EndBone : Bone;
        }

        static int Nearest(List<Segment> segs, Vector3 p, bool uma, out float along, int rider = NpcMeshes.RideFree)
        {
            int best = -1;
            float bestD = float.MaxValue;
            along = 0f;
            for (int i = 0; i < segs.Count; i++)
            {
                if (!segs[i].Carries(rider))
                    continue;
                float t = segs[i].Along(p, uma);
                float d = (p - segs[i].At(t, uma)).sqrMagnitude;
                if (d < bestD)
                    (best, bestD, along) = (i, d, t);
            }
            return best;
        }

        // How far the UMA skin reaches from each segment, binned by position along it and angle around it.
        sealed class Reach
        {
            const int Rows = 10, Sectors = 16;
            readonly float[,,] _r;
            readonly List<Segment> _segs;

            public Reach(List<Segment> segs, Body body)
            {
                _segs = segs;
                _r = new float[segs.Count, Rows, Sectors];
                foreach (var p in body.Points)
                {
                    int i = Nearest(segs, p, true, out float t);
                    var (row, sector, d) = Bin(i, p, t);
                    _r[i, row, sector] = Mathf.Max(_r[i, row, sector], d);
                }
            }

            // A limb is binned as a cylinder; the head as a ball around its middle, or hair over the crown would sit in
            // the last ring of the cylinder and be pushed straight up into a point.
            Vector3 Centre(int i, float t) => _segs[i].Head ? (_segs[i].A2 + _segs[i].B2) * 0.5f : _segs[i].At(t, true);

            (int row, int sector, float d) Bin(int i, Vector3 p, float t)
            {
                var s = _segs[i];
                if (s.Head)
                {
                    Vector3 o = p - Centre(i, t);
                    float lat = Mathf.Acos(Mathf.Clamp(o.normalized.y, -1f, 1f)) / Mathf.PI;
                    float lon = Mathf.Atan2(o.z, o.x);
                    return (Mathf.Min((int)(lat * Rows), Rows - 1),
                        Mathf.Clamp((int)((lon + Mathf.PI) / (2f * Mathf.PI) * Sectors), 0, Sectors - 1), o.magnitude);
                }
                Vector3 axis = (s.B2 - s.A2).normalized;
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.z) < 0.9f ? Vector3.forward : Vector3.right).normalized;
                Vector3 v = Vector3.Cross(axis, u);
                Vector3 off = p - s.At(t, true);
                float angle = Mathf.Atan2(Vector3.Dot(off, v), Vector3.Dot(off, u));
                int sector = Mathf.Clamp((int)((angle + Mathf.PI) / (2f * Mathf.PI) * Sectors), 0, Sectors - 1);
                return (Mathf.Min((int)(t * Rows), Rows - 1), sector, off.magnitude);
            }

            // Pushes p out along its offset from the segment until it clears the skin of its bin (and both neighbours
            // around, so a seam between sectors does not leave a sliver of skin showing).
            public Vector3 Clear(int i, Vector3 p, float t, float margin)
            {
                var (row, sector, d) = Bin(i, p, t);
                float skin = 0f;
                for (int k = -1; k <= 1; k++)
                    skin = Mathf.Max(skin, _r[i, row, (sector + k + Sectors) % Sectors]);
                if (skin <= 0f || d >= skin + margin || d < 1e-5f)
                    return p;
                Vector3 c = Centre(i, t);
                return c + (p - c) * ((skin + margin) / d);
            }
        }

        // Moves every part vertex with the nearest bone segment of the procedural rig onto the same segment of the posed UMA
        // skeleton: same fraction along it, same offset from it, turned with it. The head maps chin to crown onto the UMA
        // head's own chin to crown and scales as a whole, so hair, hats and beast heads keep their size relative to the face
        // they replace. Returns the segments, which keep reading the UMA bones.
        static List<Segment> Retarget(Mesh parts, NpcMeshes.Rig rig, Dictionary<string, Transform> bones, Body body)
        {
            System.Func<Vector3> P(string name)
            {
                var t = bones[name];
                return () => t.position;
            }

            var headBone = bones["head"];
            // The head does not move between the poses; its extent is measured once, relative to the head bone.
            Vector3 chin = headBone.InverseTransformPoint(new Vector3(body.HeadCentre.x, body.Chin, body.HeadCentre.z));
            Vector3 crown = headBone.InverseTransformPoint(new Vector3(body.HeadCentre.x, body.HeadTop, body.HeadCentre.z));
            Vector3 throat = headBone.InverseTransformPoint(new Vector3(body.HeadCentre.x, body.Chin, headBone.position.z));
            Vector3 head = (Vector3)rig.Head;
            var segs = new List<Segment>
            {
                new Segment((Vector3)rig.Hip, (Vector3)rig.Chest, P("pelvis"), P("neck_01"), "spine_01"),
                new Segment((Vector3)rig.Chest, (Vector3)rig.Neck, P("neck_01"), () => headBone.TransformPoint(throat), "neck_01"),
                new Segment(new Vector3(head.x, rig.Neck.y, head.z), head + Vector3.up * rig.HeadR,
                    () => headBone.TransformPoint(chin), () => headBone.TransformPoint(crown), "head", true),
            };
            Vector3 chestMid = (Vector3)rig.Chest + Vector3.down * (0.02f * rig.H);
            var armL = bones[Side("upperarm", true, bones)];
            var armR = bones[Side("upperarm", false, bones)];
            segs.Add(new Segment((Vector3)rig.HipL, (Vector3)rig.HipR, P(Side("thigh", true, bones)), P(Side("thigh", false, bones)), "pelvis"));
            foreach (bool left in new[] { true, false })
            {
                string S(string b) => Side(b, left, bones);
                var hand = bones[S("hand")];
                var middle = bones[S("middle_01")];
                var ball = bones[S("ball")];
                Vector3 ankle = (Vector3)(left ? rig.AnkleL : rig.AnkleR);
                Vector3 toe = new Vector3(ankle.x, 0f, ankle.z + 0.12f * rig.H);
                segs.Add(new Segment(chestMid, (Vector3)(left ? rig.ShoulderL : rig.ShoulderR), () => (armL.position + armR.position) * 0.5f,
                    P(S("upperarm")), S("clavicle")));
                segs.Add(new Segment((Vector3)(left ? rig.ShoulderL : rig.ShoulderR), (Vector3)(left ? rig.ElbowL : rig.ElbowR), P(S("upperarm")),
                    P(S("lowerarm")), S("upperarm")) { ArmSide = left ? -1 : 1 });
                segs.Add(new Segment((Vector3)(left ? rig.ElbowL : rig.ElbowR), (Vector3)(left ? rig.HandL : rig.HandR), P(S("lowerarm")),
                    () => (hand.position + middle.position) * 0.5f, S("lowerarm"), false, S("hand"), 0.85f) { ArmSide = left ? -1 : 1 });
                segs.Add(new Segment((Vector3)(left ? rig.HipL : rig.HipR), (Vector3)(left ? rig.KneeL : rig.KneeR), P(S("thigh")), P(S("calf")), S("thigh")));
                segs.Add(new Segment((Vector3)(left ? rig.KneeL : rig.KneeR), ankle, P(S("calf")), P(S("foot")), S("calf")));
                segs.Add(new Segment(ankle, toe, P(S("foot")), () => new Vector3(ball.position.x, 0f, ball.position.z), S("foot")));
            }

            var verts = parts.vertices;
            var normals = parts.normals;
            var riders = Riders(parts);
            for (int v = 0; v < verts.Length; v++)
            {
                int best = Nearest(segs, verts[v], false, out float t, riders[v]);
                var s = segs[best];
                Vector3 from = s.B - s.A, to = s.B2 - s.A2;
                Quaternion turn = Quaternion.FromToRotation(from, to);
                float k = s.Head ? to.magnitude / Mathf.Max(from.magnitude, 1e-5f) : 1f;
                verts[v] = s.A2 + t * to + turn * (verts[v] - s.At(t, false)) * k;
                if (normals.Length == verts.Length)
                    normals[v] = turn * normals[v];
            }
            parts.vertices = verts;
            if (normals.Length == verts.Length)
                parts.normals = normals;
            parts.RecalculateBounds();
            return segs;
        }

        // The rider tag of each part vertex (NpcMeshes.BuildParts writes it to UV1.x).
        static int[] Riders(Mesh parts)
        {
            var uv = new List<Vector2>();
            parts.GetUVs(1, uv);
            var riders = new int[parts.vertexCount];
            for (int i = 0; i < riders.Length && i < uv.Count; i++)
                riders[i] = Mathf.RoundToInt(uv[i].x);
            return riders;
        }

        // Skins the placed parts to the UMA skeleton as it stands: each vertex follows the bone of its nearest segment,
        // blended with the second nearest where the two are within a few centimetres (shoulders, elbows, hips, knees), so
        // a sleeve bends with the arm instead of tearing at the joint.
        static SkinnedMeshRenderer Bind(GameObject avatar, Mesh parts, List<Segment> segs, Dictionary<string, Transform> bones,
            Material[] mats, float height)
        {
            var go = new GameObject("parts");
            go.transform.SetParent(avatar.transform, false);
            var used = new List<Transform>();
            int Index(string name)
            {
                var t = bones[name];
                int i = used.IndexOf(t);
                if (i < 0)
                {
                    used.Add(t);
                    i = used.Count - 1;
                }
                return i;
            }

            float blend = 0.03f * height;
            var verts = parts.vertices;
            var riders = Riders(parts);
            var weights = new BoneWeight[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 p = go.transform.TransformPoint(verts[v]);
                int first = -1, second = -1;
                float d1 = float.MaxValue, d2 = float.MaxValue, t1 = 0f, t2 = 0f;
                for (int i = 0; i < segs.Count; i++)
                {
                    float t = segs[i].Along(p, true);
                    float d = (p - segs[i].At(t, true)).magnitude;
                    if (!segs[i].Carries(riders[v]))
                        continue;
                    if (d < d1)
                    {
                        (second, d2, t2) = (first, d1, t1);
                        (first, d1, t1) = (i, d, t);
                    }
                    else if (d < d2)
                    {
                        (second, d2, t2) = (i, d, t);
                    }
                }

                int b1 = Index(segs[first].BoneAt(t1));
                int b2 = second < 0 ? b1 : Index(segs[second].BoneAt(t2));
                float w2 = b2 == b1 ? 0f : 0.5f * Mathf.Clamp01(1f - (d2 - d1) / blend);
                weights[v] = new BoneWeight { boneIndex0 = b1, weight0 = 1f - w2, boneIndex1 = b2, weight1 = w2 };
            }

            parts.boneWeights = weights;
            parts.bindposes = used.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.bones = used.ToArray();
            smr.rootBone = bones["pelvis"];
            smr.sharedMesh = parts;
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;
            return smr;
        }

        // Fits the skinned parts to the body in the bind pose: cloth inside the skin is pushed out past it, then every
        // body triangle whose corners all sit under tight cloth is hidden (UMA wardrobe hides the body under clothes the
        // same way; low-poly cloth spans the body's curves, so pushing cloth alone leaves skin showing between its
        // vertices). The face is never hidden. The parts are rebound in this pose. Returns the hidden triangle count.
        static int Fit(GameObject avatar, SkinnedMeshRenderer cloth, List<Segment> segs, bool beast, float margin)
        {
            var bodies = avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r != cloth).ToArray();
            var body = Body.Sample(bodies, beast);
            var reach = new Reach(segs, body);
            var mesh = cloth.sharedMesh;
            var baked = new Mesh();
            cloth.BakeMesh(baked, true);
            var verts = baked.vertices;
            Object.DestroyImmediate(baked);
            var riders = Riders(mesh);
            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 p = cloth.transform.TransformPoint(verts[v]);
                int best = Nearest(segs, p, true, out float t, riders[v]);
                // A beast's procedural head is whole: nothing of the UMA head is left for it to clear.
                if (!(segs[best].Head && beast))
                    p = reach.Clear(best, p, t, margin);
                verts[v] = cloth.transform.InverseTransformPoint(p);
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bindposes = cloth.bones.Select(b => b.worldToLocalMatrix * cloth.transform.localToWorldMatrix).ToArray();

            var colliderGo = new GameObject("cloth-collider");
            colliderGo.transform.SetParent(cloth.transform, false);
            var collider = colliderGo.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            int hidden = 0;
            try
            {
                foreach (var smr in bodies)
                {
                    var skin = new Mesh();
                    smr.BakeMesh(skin, true);
                    var points = skin.vertices;
                    Object.DestroyImmediate(skin);
                    var weights = smr.sharedMesh.boneWeights;
                    int head = System.Array.FindIndex(smr.bones, b => b != null && b.name == "head");
                    var covered = new bool[points.Length];
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (head >= 0 && i < weights.Length && weights[i].boneIndex0 == head && weights[i].weight0 > 0.5f)
                        {
                            // Beasts wear the procedural head whole (stand-in for their head slot): the UMA head hides.
                            covered[i] = beast;
                            continue;
                        }
                        Vector3 w = smr.transform.TransformPoint(points[i]);
                        int seg = Nearest(segs, w, true, out float t);
                        Vector3 c = segs[seg].Head ? (segs[seg].A2 + segs[seg].B2) * 0.5f : segs[seg].At(t, true);
                        Vector3 outward = w - c;
                        float d = outward.magnitude;
                        // Cloth more than a few centimetres off the skin is loose (a cape, a skirt's flare): the skin
                        // under it stays, since it shows through the gaps such cloth leaves.
                        covered[i] = d > 1e-4f && Physics.Raycast(c, outward / d, out var hit, d + 4f * margin) && hit.collider == collider;
                    }

                    var shared = smr.sharedMesh;
                    for (int sub = 0; sub < shared.subMeshCount; sub++)
                    {
                        var tris = shared.GetTriangles(sub);
                        var kept = new List<int>(tris.Length);
                        for (int i = 0; i + 2 < tris.Length; i += 3)
                        {
                            if (covered[tris[i]] && covered[tris[i + 1]] && covered[tris[i + 2]])
                            {
                                hidden++;
                                continue;
                            }
                            kept.Add(tris[i]);
                            kept.Add(tris[i + 1]);
                            kept.Add(tris[i + 2]);
                        }
                        shared.SetTriangles(kept, sub);
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
                Object.DestroyImmediate(colliderGo);
            }
            return hidden;
        }

        // The rig's left is negative x; the base's "_l" bones may face either way, so sides are matched by position.
        static string Side(string bone, bool left, Dictionary<string, Transform> bones) =>
            (bones[bone + "_l"].position.x < 0f) == left ? bone + "_l" : bone + "_r";
    }
}
