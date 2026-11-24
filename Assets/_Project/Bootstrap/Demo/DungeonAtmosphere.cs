using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Per-terrain look of an interior dungeon (combat-feel.md#地城氛圍; every value 暫定).</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    internal readonly struct DungeonMood
    {
        /// <summary>Flat ambient replacing the sky ambient.</summary>
        public readonly Color Ambient;

        /// <summary>Fog and camera clear colour (no ceiling yet, so this is also what shows above the walls).</summary>
        public readonly Color Fog;

        /// <summary>Colour of the room emitters and their point lights.</summary>
        public readonly Color Light;

        /// <summary>Fog start / end, m.</summary>
        public readonly float FogStartM;
        public readonly float FogEndM;

        /// <summary>URP ColorAdjustments saturation (−100…100).</summary>
        public readonly float Saturation;

        /// <summary>Indoors: ambient, sun, sky and clear colour are pulled dark. Surface mazes keep the sky and time of day.</summary>
        public readonly bool Darken;

        private DungeonMood(Color ambient, Color fog, Color light, float fogStartM, float fogEndM, float saturation,
            bool darken = true)
        {
            Darken = darken;
            Ambient = ambient;
            Fog = fog;
            Light = light;
            FogStartM = fogStartM;
            FogEndM = fogEndM;
            Saturation = saturation;
        }

        /// <summary>Mood for an interior advanced terrain; unknown ids get a neutral cold mood.</summary>
        /// <remarks>Pre: none. Post: FogStartM &lt; FogEndM. Side effects: none.</remarks>
        // Light colours follow combat-feel.md's "火把／晶簇／符文": warm torches underground, cold lamps in machine spaces,
        // bioluminescence at depth, rune gold in the temple. Fog stays short so rooms read one at a time (推門 → 遇敵).
        public static DungeonMood For(string terrainId) => terrainId switch
        {
            "laboratory" => new DungeonMood(new Color(0.16f, 0.2f, 0.24f), new Color(0.04f, 0.07f, 0.09f),
                new Color(0.45f, 0.9f, 1f), 10f, 70f, -30f),
            "deep_sea" => new DungeonMood(new Color(0.08f, 0.16f, 0.22f), new Color(0.01f, 0.06f, 0.1f),
                new Color(0.3f, 1f, 0.85f), 6f, 50f, -20f),
            "cave" => new DungeonMood(new Color(0.18f, 0.15f, 0.12f), new Color(0.06f, 0.04f, 0.03f),
                new Color(1f, 0.62f, 0.28f), 8f, 55f, -25f),
            "tower" => new DungeonMood(new Color(0.18f, 0.18f, 0.22f), new Color(0.06f, 0.06f, 0.1f),
                new Color(1f, 0.95f, 0.55f), 12f, 80f, -20f),
            "space" => new DungeonMood(new Color(0.16f, 0.17f, 0.24f), new Color(0.01f, 0.01f, 0.04f),
                new Color(0.75f, 0.85f, 1f), 14f, 90f, -25f),
            "temple" => new DungeonMood(new Color(0.22f, 0.19f, 0.15f), new Color(0.08f, 0.06f, 0.04f),
                new Color(1f, 0.82f, 0.45f), 10f, 70f, -15f),
            // Surface mazes (combat-feel.md#地城氛圍): no darkening; the haze above the walls takes the terrain's tint.
            // A haze, not a wall of fog: the maze has to stay readable from one end of a corridor to the other.
            "rainforest" => new DungeonMood(default, new Color(0.46f, 0.58f, 0.45f), default, 30f, 240f, -15f, false),
            "volcano" => new DungeonMood(default, new Color(0.45f, 0.33f, 0.29f), default, 35f, 260f, -10f, false),
            "ice_cap" => new DungeonMood(default, new Color(0.82f, 0.9f, 0.96f), default, 40f, 300f, -15f, false),
            "labyrinth" => new DungeonMood(default, new Color(0.24f, 0.2f, 0.32f), default, 25f, 200f, -20f, false),
            _ => new DungeonMood(new Color(0.17f, 0.17f, 0.2f), new Color(0.05f, 0.05f, 0.07f),
                new Color(0.9f, 0.9f, 1f), 10f, 70f, -25f),
        };
    }

    /// <summary>
    /// Advanced-terrain dungeon atmosphere (combat-feel.md#地城氛圍): short terrain-tinted fog, desaturation and vignette;
    /// indoors also darkened ambient, sun and sky and one lamp with a point light per room. Presentation only; never
    /// touches rule state.
    /// </summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> once before use; the surface AtmosphereDriver (if any) writes in its own LateUpdate,
    ///      which this runs after (execution order), so the override wins while inside.
    /// Post: while weight is 0 nothing is overridden; after a full fade-out the fog distances it changed are restored.
    /// Side effects: RenderSettings, sun intensity, sky shader globals, a global Volume, point lights.
    /// </remarks>
    [DefaultExecutionOrder(1000)]
    internal sealed class DungeonAtmosphere : MonoBehaviour
    {
        private const float FadeSeconds = 0.6f;
        private const float SunScale = 0.2f;
        private const float VignetteIntensity = 0.38f;
        private const float LightIntensity = 3f;
        // Forward (Steam Deck) caps additional lights at 4 per object; only the nearest rooms' lights stay on.
        private const int MaxLiveLights = 4;

        private static readonly int SkyHorizonId = Shader.PropertyToID("_CosmiSkyHorizon");
        private static readonly int SkyZenithId = Shader.PropertyToID("_CosmiSkyZenith");
        private static readonly int SunColorId = Shader.PropertyToID("_CosmiSunColor");

        private readonly List<Light> _lights = new List<Light>();
        private readonly List<Object> _owned = new List<Object>();
        private float[] _lightDistSq = new float[0];

        private Camera _camera;
        private Light _sun;
        private Transform _player;
        private DungeonMood _mood;
        private Volume _volume;
        private VolumeProfile _profile;
        private float _weight;
        private bool _inside;
        private bool _fogTouched;
        private float _baseFogStart;
        private float _baseFogEnd;
        private float _writtenFogStart = float.NaN;
        private float _writtenFogEnd = float.NaN;
        private float _liveRadiusM;

        /// <summary>Current blend, 0 (surface) … 1 (fully inside).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Weight => _weight;

        /// <summary>Lamps built, and how many of their point lights are enabled now.</summary>
        /// <remarks>Pre: none. Post: lit &lt;= MaxLiveLights and lit &lt;= lamps. Side effects: none.</remarks>
        public (int lamps, int lit) LampCounts
        {
            get
            {
                int lit = 0;
                foreach (Light l in _lights)
                {
                    lit += l.enabled ? 1 : 0;
                }

                return (_lights.Count, lit);
            }
        }

        /// <summary>Binds the camera, sun, player and mood, and creates the post-process volume.</summary>
        /// <remarks>
        /// Pre: camera, sun and player non-null; called once.
        /// Post: weight 0; volume present with weight 0; camera post-processing on.
        /// Side effects: creates a root Volume GameObject and a runtime VolumeProfile, destroyed with this component.
        /// </remarks>
        public void Configure(Camera camera, Light sun, Transform player, in DungeonMood mood)
        {
            Contract.Requires(camera != null && sun != null && player != null, "camera, sun and player required");
            Contract.Requires(_camera == null, "configure once");
            _camera = camera;
            _sun = sun;
            _player = player;
            _mood = mood;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Vignette vignette = _profile.Add<Vignette>(true);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            ColorAdjustments adjust = _profile.Add<ColorAdjustments>(true);
            adjust.saturation.Override(mood.Saturation);
            // Own root, not a child: entering the dungeon hides every child of the run object, and ScatterField treats
            // them as its roots.
            var volumeGo = new GameObject("DungeonVolume");
            _owned.Add(volumeGo);
            _volume = volumeGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            // Above the run's base profile, so its own vignette/saturation are replaced rather than summed.
            _volume.priority = 10f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;
            UniversalAdditionalCameraData camData = camera.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
            }
        }

        /// <summary>Places one lamp with a point light in every room of <paramref name="geometry"/>.</summary>
        /// <remarks>
        /// Pre: Configured; geometry non-null; groundAltitude returns the floor altitude for a unit direction; lamp is null
        ///      (lights without a drawn lamp) or a DungeonDressing lamp mesh with one material per sub-mesh in materials;
        ///      lightHeightM &gt; 0; parent non-null. Call once per dungeon.
        /// Post: one light per room (four in the boss arena), all disabled until the player is near; no colliders.
        /// Side effects: creates GameObjects under parent (the caller owns the mesh and materials).
        /// </remarks>
        public void BuildRoomLights(RoomDungeonGeometry geometry, System.Func<float3, float> groundAltitude,
            in PlanetConfig config, Mesh lamp, Material[] materials, float lightHeightM, Transform parent)
        {
            Contract.Requires(_camera != null, "configure first");
            Contract.Requires(geometry != null && groundAltitude != null && parent != null,
                "geometry, groundAltitude and parent required");
            Contract.Requires(lamp == null || (materials != null && materials.Length == lamp.subMeshCount),
                "one material per lamp sub-mesh");
            Contract.Requires(lightHeightM > 0f, "lightHeightM must be > 0");
            RoomDungeon dungeon = geometry.Dungeon;
            float cell = dungeon.CellSizeM;
            // Clear of the wall panels (DungeonDressing.PanelFootHalfM) but off the room's middle, where fights and
            // spawns happen.
            float inset = cell * 0.5f - DungeonDressing.PanelFootHalfM - 1.8f;
            _liveRadiusM = cell * 1.6f;
            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                float3 center = geometry.RoomCenter(r);
                PlanetFrame frame = PlanetFrame.FromUp(center);
                if (r == dungeon.BossRoom)
                {
                    float ring = geometry.ArenaRadiusM - DungeonDressing.PanelFootHalfM - 1.5f;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * math.PI * 0.5f + math.PI * 0.25f;
                        float3 offset = (frame.East * math.cos(a) + frame.North * math.sin(a)) * ring;
                        AddLamp(math.normalize(center * geometry.Radius + offset), groundAltitude, config, lamp, materials,
                            lightHeightM, parent, cell);
                    }

                    continue;
                }

                // Alternate corners by room index so neighbouring rooms do not line their lights up along one axis.
                float sx = (r & 1) == 0 ? 1f : -1f;
                float sy = (r & 2) == 0 ? 1f : -1f;
                float3 corner = (frame.East * sx + frame.North * sy) * inset;
                AddLamp(math.normalize(center * geometry.Radius + corner), groundAltitude, config, lamp, materials,
                    lightHeightM, parent, cell);
            }

            _lightDistSq = new float[_lights.Count];
        }

        /// <summary>Starts fading toward the dungeon look (true) or back to the surface look (false).</summary>
        /// <remarks>Pre: Configured. Post: target set; the blend moves over FadeSeconds. Side effects: none until LateUpdate.</remarks>
        public void SetInside(bool inside)
        {
            _inside = inside;
        }

        private void AddLamp(float3 dir, System.Func<float3, float> groundAltitude, in PlanetConfig config, Mesh lamp,
            Material[] materials, float lightHeightM, Transform parent, float cell)
        {
            float3 at = config.ToWorld(dir, groundAltitude(dir));
            var go = new GameObject("DungeonLamp");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation((Vector3)at, Quaternion.LookRotation((Vector3)PlanetFrame.FromUp(dir).North,
                (Vector3)dir));
            if (lamp != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = lamp;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, lightHeightM, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _mood.Light;
            light.range = cell * 0.75f;
            light.intensity = LightIntensity;
            light.shadows = LightShadows.None;
            light.enabled = false;
            _lights.Add(light);
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            float step = Time.unscaledDeltaTime / FadeSeconds;
            _weight = math.saturate(_weight + (_inside ? step : -step));
            _volume.weight = _weight;
            UpdateLights();
            if (_weight <= 0f)
            {
                RestoreFog();
                return;
            }

            float w = _weight;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, _mood.Fog, w);
            ApplyFog(w);
            if (!_mood.Darken)
            {
                return;
            }

            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, _mood.Ambient, w);
            _camera.backgroundColor = Color.Lerp(_camera.backgroundColor, _mood.Fog, w);
            _sun.intensity *= math.lerp(1f, SunScale, w);
            Shader.SetGlobalColor(SkyHorizonId, Color.Lerp(Shader.GetGlobalColor(SkyHorizonId), _mood.Fog, w));
            Shader.SetGlobalColor(SkyZenithId, Color.Lerp(Shader.GetGlobalColor(SkyZenithId), _mood.Fog, w));
            Shader.SetGlobalColor(SunColorId, Shader.GetGlobalColor(SunColorId) * math.lerp(1f, SunScale, w));
        }

        // Weather (AtmosphereDriver.SetVisibilityRadius) may rewrite the fog distances while inside; a value that is not
        // the one written here last is a new surface base, so the fade always returns to what the surface wants now.
        private void ApplyFog(float w)
        {
            if (!_fogTouched || RenderSettings.fogStartDistance != _writtenFogStart
                || RenderSettings.fogEndDistance != _writtenFogEnd)
            {
                _baseFogStart = RenderSettings.fogStartDistance;
                _baseFogEnd = RenderSettings.fogEndDistance;
                _fogTouched = true;
            }

            _writtenFogStart = math.lerp(_baseFogStart, _mood.FogStartM, w);
            _writtenFogEnd = math.lerp(_baseFogEnd, _mood.FogEndM, w);
            RenderSettings.fogStartDistance = _writtenFogStart;
            RenderSettings.fogEndDistance = _writtenFogEnd;
        }

        private void RestoreFog()
        {
            if (!_fogTouched)
            {
                return;
            }

            if (RenderSettings.fogStartDistance == _writtenFogStart && RenderSettings.fogEndDistance == _writtenFogEnd)
            {
                RenderSettings.fogStartDistance = _baseFogStart;
                RenderSettings.fogEndDistance = _baseFogEnd;
            }

            _fogTouched = false;
        }

        // Keeps at most MaxLiveLights on: the nearest ones within reach of the player. Selection over ≤ 20 lights,
        // allocation-free.
        private void UpdateLights()
        {
            int n = _lights.Count;
            if (n == 0)
            {
                return;
            }

            Vector3 p = _player.position;
            float reachSq = _liveRadiusM * _liveRadiusM;
            for (int i = 0; i < n; i++)
            {
                _lightDistSq[i] = _weight > 0f ? (_lights[i].transform.position - p).sqrMagnitude : float.MaxValue;
            }

            for (int i = 0; i < n; i++)
            {
                float d = _lightDistSq[i];
                int closer = 0;
                for (int j = 0; j < n && closer < MaxLiveLights; j++)
                {
                    if (_lightDistSq[j] < d || (_lightDistSq[j] == d && j < i))
                    {
                        closer++;
                    }
                }

                bool on = d <= reachSq && closer < MaxLiveLights;
                if (_lights[i].enabled != on)
                {
                    _lights[i].enabled = on;
                }

                if (on)
                {
                    _lights[i].intensity = LightIntensity * _weight;
                }
            }
        }

        private void OnDestroy()
        {
            RestoreFog();
            foreach (Object o in _owned)
            {
                if (o != null)
                {
                    Destroy(o);
                }
            }

            if (_profile != null)
            {
                Destroy(_profile);
            }
        }
    }
}
