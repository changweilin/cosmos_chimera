using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Cosmimera.EditorTools
{
    /// <summary>Imports and checks the Blender-built ACG main-character FBX files (ADR-0013).</summary>
    public static class AcgCharacterImport
    {
        static readonly string[] Ids = { "chr_roen", "chr_amemori_sumiha", "chr_yeh_chengxi" };

        static string FbxPath(string id) => $"Assets/_Project/Art/Characters/{id}/ACG/{id}_acg.fbx";

        /// <summary>Name of the one-pose idle clip holding the illustration stance (characters.md: 靜止姿勢).</summary>
        public const string IdleClip = "Idle_Art";

        // Model@clip convention: the clip lives in its own armature-only FBX so the model file's node
        // transforms stay in the T-pose the Humanoid avatar is built from.
        static string IdlePath(string id) => $"Assets/_Project/Art/Characters/{id}/ACG/{id}_acg@{IdleClip}.fbx";

        /// <summary>Sets every ACG character FBX to a Humanoid rig built from its own T-pose bind pose and imports its idle clip.</summary>
        /// <remarks>
        /// Pre: the model FBX files exist (tools/acg-character/build.py --fbx); the idle FBX exists when the spec has an idle_pose.
        /// Post: each model uses AnimationType.Human with CreateFromThisModel and no animation; each idle FBX uses the
        /// model's avatar and holds one looping clip named <see cref="IdleClip"/>; logs avatar validity and whether the clip exists.
        /// Side effects: rewrites the FBX .meta files and reimports them (the idle file twice: its take list is only known after import).
        /// </remarks>
        public static void Configure()
        {
            foreach (var id in Ids)
            {
                var path = FbxPath(id);
                if (AssetImporter.GetAtPath(path) is not ModelImporter mi)
                {
                    Debug.LogError($"[AcgCharacterImport] missing {path}");
                    continue;
                }
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                RemapToonMaterials(id, mi);
                mi.SaveAndReimport();
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(path);
                var idle = avatar != null && avatar.isValid ? ConfigureIdle(id, avatar) : null;
                Debug.Log($"[AcgCharacterImport] {id} avatar={(avatar != null)} valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman} idle={(idle != null)}");
            }
        }

        const string ToonShaderPath = "Assets/_Project/Art/Shaders/ToonOutlined.shader";

        // Face decals are flat strips on the skin: an inverted hull around them would draw black rims.
        static readonly string[] NoOutline = { "sclera", "iris", "eye_", "brow", "lash", "mouth", "nose", "teeth" };

        [System.Serializable]
        sealed class CelRow { public string name; public string @base; public string shade; public int bands; public string texture; }

        [System.Serializable]
        sealed class CelTable { public CelRow[] materials; }

        /// <summary>Rebuilds each FBX material as a Cosmimera/ToonOutlined asset from the build's cel colour table and
        /// remaps the model to it.</summary>
        /// <remarks>
        /// Why: FBX carries only a base colour; under URP Lit light skin washed out to the same white as the clothes, and
        /// nothing outlined one against the other. The table (<c>&lt;id&gt;_acg_materials.json</c>, written by
        /// tools/acg-character export_fbx) keeps the Blender shade colour, band count and strand/iris texture.
        /// Pre: the table sits beside the FBX. Post: one material per row under ACG/Materials/, mapped onto mi
        /// (the caller reimports). Side effects: creates or overwrites .mat assets; logs an error and keeps the imported
        /// materials when the table or shader is missing.
        /// </remarks>
        static void RemapToonMaterials(string id, ModelImporter mi)
        {
            var dir = Path.GetDirectoryName(FbxPath(id)).Replace('\\', '/');
            var tablePath = $"{dir}/{id}_acg_materials.json";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(tablePath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ToonShaderPath);
            if (json == null || shader == null)
            {
                Debug.LogError($"[AcgCharacterImport] {id}: missing {(json == null ? tablePath : ToonShaderPath)}; keeping imported materials");
                return;
            }
            var table = JsonUtility.FromJson<CelTable>(json.text);
            if (table?.materials == null || table.materials.Length == 0)
            {
                Debug.LogError($"[AcgCharacterImport] {id}: {tablePath} has no materials");
                return;
            }
            var matDir = $"{dir}/Materials";
            if (!AssetDatabase.IsValidFolder(matDir))
                AssetDatabase.CreateFolder(dir, "Materials");
            foreach (var row in table.materials)
            {
                if (!ColorUtility.TryParseHtmlString(row.@base, out var baseColor))
                {
                    Debug.LogError($"[AcgCharacterImport] {id}/{row.name}: bad colour {row.@base}");
                    continue;
                }
                var path = $"{matDir}/{row.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", baseColor);
                // CosmiToon darkens the unlit side to albedo * _Shadow * 0.6, so the tint that reproduces the
                // authored shade colour is shade / base / 0.6 per channel. No shade (decals) = no darkening.
                var tint = Color.white / 0.6f;
                if (!string.IsNullOrEmpty(row.shade) && ColorUtility.TryParseHtmlString(row.shade, out var shade))
                    tint = new Color(Ratio(shade.r, baseColor.r), Ratio(shade.g, baseColor.g), Ratio(shade.b, baseColor.b), 1f);
                mat.SetColor("_Shadow", tint);
                mat.SetFloat("_Bands", Mathf.Max(2, row.bands));
                var bare = System.Array.Exists(NoOutline, k => row.name.StartsWith(k));
                // Skin gets a finer line: a bent knee or elbow is concave, and a wide hull shows through there.
                mat.SetFloat("_OutlineWidth", bare ? 0f : row.name.StartsWith("skin_") ? 0.0012f : 0.0018f);
                mat.SetColor("_OutlineColor", new Color(0.16f, 0.11f, 0.12f, 1f));
                // A bent knee or elbow is concave; Humanoid retargeting also leaves a few degrees of knee twist the
                // rig cannot avoid. Pushing the hull back hides the slivers that would read as cracks there.
                mat.SetFloat("_OutlineDepthBias", 0.006f);
                var tex = string.IsNullOrEmpty(row.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{row.texture}");
                mat.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(mat);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), row.name), mat);
            }
            AssetDatabase.SaveAssets();
        }

        static float Ratio(float shade, float lit) => Mathf.Clamp(shade / Mathf.Max(lit, 0.02f) / 0.6f, 0f, 1.7f);

        static AnimationClip ConfigureIdle(string id, Avatar avatar)
        {
            var path = IdlePath(id);
            if (AssetImporter.GetAtPath(path) is not ModelImporter ai)
            {
                Debug.LogError($"[AcgCharacterImport] missing {path}; rebuild with an idle_pose in the spec");
                return null;
            }
            ai.animationType = ModelImporterAnimationType.Human;
            ai.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            ai.sourceAvatar = avatar;
            ai.importAnimation = true;
            ai.materialImportMode = ModelImporterMaterialImportMode.None;
            ai.clipAnimations = new ModelImporterClipAnimation[0];
            ai.SaveAndReimport();
            var takes = ai.defaultClipAnimations;
            if (takes.Length == 0)
            {
                Debug.LogError($"[AcgCharacterImport] {path} has no animation take");
                return null;
            }
            var clip = takes[0];
            clip.name = IdleClip;
            clip.loopTime = true;
            ai.clipAnimations = new[] { clip };
            ai.SaveAndReimport();
            return LoadIdle(id);
        }

        static AnimationClip LoadIdle(string id)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(IdlePath(id)))
                if (o is AnimationClip c && c.name == IdleClip)
                    return c;
            return null;
        }

        /// <summary>Renders front, side, back and 3/4 orthographic views of each ACG character to Logs/acg-&lt;id&gt;-&lt;view&gt;.png,
        /// then front and 3/4 in the idle stance to Logs/acg-&lt;id&gt;-idle-&lt;view&gt;.png.</summary>
        /// <remarks>
        /// Pre: Configure() has run; a GPU is available (do not pass -nographics).
        /// Post: four bind-pose PNGs per character, plus two idle PNGs when the idle clip exists; logs the measured bounds height.
        /// Side effects: opens a new empty scene (unsaved) and writes PNGs under Logs/.
        /// </remarks>
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The first render of a shader variant otherwise draws the cyan async-compile placeholder.
            ShaderUtil.allowAsyncCompilation = false;
            var lightGo = new GameObject("key");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.25f, 0.27f);
            var rt = new RenderTexture(600, 900, 24);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Logs");
            foreach (var id in Ids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(id));
                if (prefab == null)
                {
                    Debug.LogError($"[AcgCharacterImport] cannot load {FbxPath(id)}");
                    continue;
                }
                var go = Object.Instantiate(prefab);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(r.bounds);
                cam.orthographicSize = bounds.size.y * 0.55f;
                Shoot(cam, rt, bounds, id, new[] { 0f, 90f, 180f, 25f }, new[] { "front", "side", "back", "q34" });
                var idle = LoadIdle(id);
                if (idle != null)
                {
                    // Evaluating through the Animator's Humanoid avatar proves the clip retargets;
                    // AnimationClip.SampleAnimation leaves a Humanoid (muscle) clip unapplied.
                    var animator = go.GetComponent<Animator>();
                    var graph = PlayableGraph.Create("acg-idle-capture");
                    var output = AnimationPlayableOutput.Create(graph, "pose", animator);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(graph, idle));
                    var arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    var before = arm.rotation;
                    graph.Evaluate(0f);
                    SettleCloth(go, id);
                    var bindings = AnimationUtility.GetCurveBindings(idle);
                    Debug.Log($"[AcgCharacterImport] {id} idle human={idle.isHumanMotion} len={idle.length} curves={bindings.Length} avatar={(animator.avatar != null)} armDelta={Quaternion.Angle(before, arm.rotation):F1} hands={animator.GetBoneTransform(HumanBodyBones.LeftHand).name}/{animator.GetBoneTransform(HumanBodyBones.RightHand).name}");
                    // Batch-mode manual renders skip the player-loop skinning pass; without this the
                    // posed bones render with the bind-pose mesh.
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                        smr.forceMatrixRecalculationPerRender = true;
                    Shoot(cam, rt, bounds, id, new[] { 0f, 25f }, new[] { "idle-front", "idle-q34" });
                    graph.Destroy();
                }
                // Held props ship hidden except the spec's "hold"; importVisibility maps that to a disabled renderer.
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("prop_"))
                        Debug.Log($"[AcgCharacterImport] {id} {r.name} enabled={r.enabled}");
                Debug.Log($"[AcgCharacterImport] {id} height={bounds.size.y:F3} centre={bounds.center}");
                Object.DestroyImmediate(go);
            }
        }

        // Runs the cloth spring for 3 s so the stance captures show hanging cloth, not the modelled T-pose drape.
        static void SettleCloth(GameObject go, string id)
        {
            var type = AcgSpringImport.SpringBonesType();
            var springs = type == null ? null : go.GetComponent(type);
            if (springs == null)
                return;
            // The capture scene is a flat test stage, not the planet, so world down is its gravity here.
            type.GetProperty("Gravity").SetValue(springs, Vector3.down * 9.81f);
            var simulate = type.GetMethod("Simulate");
            for (int i = 0; i < 180; i++)
                simulate.Invoke(springs, new object[] { 1f / 60f });
            Debug.Log($"[AcgCharacterImport] {id} cloth chains={type.GetProperty("ChainCount").GetValue(springs)} capsules={type.GetProperty("CapsuleCount").GetValue(springs)} settled");
        }

        static void Shoot(Camera cam, RenderTexture rt, Bounds bounds, string id, float[] yaws, string[] names)
        {
            for (int i = 0; i < yaws.Length; i++)
            {
                // Orbit the camera around the model; yaw 0 looks at the model's face (+Z forward).
                var dir = Quaternion.Euler(0f, yaws[i], 0f) * Vector3.forward;
                cam.transform.position = bounds.center + dir * 10f;
                cam.transform.LookAt(bounds.center);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"Logs/acg-{id}-{names[i]}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            RenderTexture.active = null;
        }
    }
}
