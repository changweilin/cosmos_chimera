using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 visual identity per planet type: toon terrain palette (P3-04 biome colours) and the atmosphere driver
    /// (P1-08 sky/day-night). REQ: planet-world.md#星球類型, vision.md. Colours are placeholders pending art review.
    /// </summary>
    /// <remarks>Pre: shaders non-null. Post: materials are new instances owned by the caller. Side effects: allocates.</remarks>
    public static class PlanetLook
    {
        private sealed class Palette
        {
            public Color Deep, Shallow, Shore, Low, Mid, High, Rock, Snow, Day, Dusk, Night;
            public float SnowLine = 0.8f;
            public float PolarLat = 0.94f; // sin(latitude) where polar snow starts (~70°)
        }

        private static readonly Dictionary<string, Palette> Palettes = new Dictionary<string, Palette>
        {
            ["verdant_planet"] = new Palette
            {
                Deep = Hex(0x1C3F73), Shallow = Hex(0x3C8DB0), Shore = Hex(0xD9CC98), Low = Hex(0x6DB04A), Mid = Hex(0x3F7F36),
                High = Hex(0x8A8060), Rock = Hex(0x6E6A62), Snow = Hex(0xF2F6FF), Day = Hex(0x8CC8F0), Dusk = Hex(0xF0A070),
                Night = Hex(0x0B1430),
            },
            ["lava_planet"] = new Palette
            {
                // "Sea" on a lava world reads as molten rock: the same height mask, hot colours.
                Deep = Hex(0xC23A0C), Shallow = Hex(0xFF8A1E), Shore = Hex(0x2A1E1C), Low = Hex(0x3A2B28), Mid = Hex(0x4A3530),
                High = Hex(0x5E4038), Rock = Hex(0x2E2422), Snow = Hex(0x8C8076), Day = Hex(0xD9825A), Dusk = Hex(0xB0402A),
                Night = Hex(0x1A0808), SnowLine = 1.1f,
            },
            ["ice_crystal_planet"] = new Palette
            {
                Deep = Hex(0x1B3D66), Shallow = Hex(0x5FA8C8), Shore = Hex(0xCFE3EE), Low = Hex(0xDCEBF2), Mid = Hex(0xB8D4E6),
                High = Hex(0xEEF6FF), Rock = Hex(0x7C8C99), Snow = Hex(0xFFFFFF), Day = Hex(0xBFE2FF), Dusk = Hex(0xE7B8D8),
                Night = Hex(0x0A1630), SnowLine = 0.45f, PolarLat = 0.7f,
            },
            ["storm_planet"] = new Palette
            {
                Deep = Hex(0x23304F), Shallow = Hex(0x47668A), Shore = Hex(0xA7A08A), Low = Hex(0x6E8A5A), Mid = Hex(0x5E7050),
                High = Hex(0x80786E), Rock = Hex(0x5C5A60), Snow = Hex(0xE6E8F0), Day = Hex(0x7E8EA8), Dusk = Hex(0x9A7FA8),
                Night = Hex(0x0E1020),
            },
            ["mineral_planet"] = new Palette
            {
                Deep = Hex(0x2E4E5E), Shallow = Hex(0x4E8A8A), Shore = Hex(0xD8B884), Low = Hex(0xC08850), Mid = Hex(0xA8643C),
                High = Hex(0x8C4E36), Rock = Hex(0x6A4434), Snow = Hex(0xE8D8C0), Day = Hex(0xE8C898), Dusk = Hex(0xE08858),
                Night = Hex(0x1A1020), SnowLine = 1.1f,
            },
            ["twilight_planet"] = new Palette
            {
                Deep = Hex(0x241A4A), Shallow = Hex(0x4A3E8A), Shore = Hex(0xC8B890), Low = Hex(0x5A7A6A), Mid = Hex(0x4A5A6A),
                High = Hex(0x7A6A8A), Rock = Hex(0x4E4658), Snow = Hex(0xF0E8FF), Day = Hex(0xF0C878), Dusk = Hex(0xC870A0),
                Night = Hex(0x120A28),
            },
        };

        /// <summary>Toon terrain material for a planet type.</summary>
        /// <remarks>
        /// Pre: shader non-null; reliefM > 0. Post: material with that type's palette (verdant when unknown).
        /// Side effects: allocates a Material.
        /// </remarks>
        public static Material TerrainMaterial(Shader shader, string planetTypeId, in PlanetConfig config, float reliefM)
        {
            Contract.Requires(shader != null && reliefM > 0f, "shader required; relief > 0");
            Palette p = PaletteFor(planetTypeId);
            var m = new Material(shader);
            m.SetVector("_PlanetCenter", (Vector3)config.Center);
            m.SetFloat("_Radius", config.Radius);
            m.SetFloat("_Relief", reliefM);
            m.SetColor("_DeepWater", p.Deep);
            m.SetColor("_ShallowWater", p.Shallow);
            m.SetColor("_Shore", p.Shore);
            m.SetColor("_Low", p.Low);
            m.SetColor("_Mid", p.Mid);
            m.SetColor("_High", p.High);
            m.SetColor("_Rock", p.Rock);
            m.SetColor("_Snow", p.Snow);
            m.SetFloat("_SnowLine", p.SnowLine);
            m.SetFloat("_PolarLat", p.PolarLat);
            return m;
        }

        /// <summary>Outlined toon material for a creature placeholder.</summary>
        /// <remarks>Pre: shader non-null. Post: new material. Side effects: allocates a Material.</remarks>
        public static Material CreatureMaterial(Shader shader, Color color, float outlineWidthM)
        {
            Contract.Requires(shader != null, "shader required");
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_OutlineWidth", outlineWidthM);
            return m;
        }

        /// <summary>Sky colours (day, dusk, night) for a planet type.</summary>
        /// <remarks>Pre: none. Post: three colours. Side effects: none.</remarks>
        public static (Color day, Color dusk, Color night) Sky(string planetTypeId)
        {
            Palette p = PaletteFor(planetTypeId);
            return (p.Day, p.Dusk, p.Night);
        }

        private static Palette PaletteFor(string id) => id != null && Palettes.TryGetValue(id, out Palette p) ? p : Palettes["verdant_planet"];

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }

    /// <summary>
    /// Sky and fog from the sun's elevation at the camera, plus a slow sun orbit (P1-08 day/night, D0 version).
    /// </summary>
    /// <remarks>
    /// A skybox's gradient assumes world +Y is up, which is wrong almost everywhere on a sphere; colouring the clear
    /// colour and fog by the local sun elevation is correct at every point and costs nothing.
    /// Pre: Configure once. Side effects: writes RenderSettings fog/ambient and the camera background every frame.
    /// </remarks>
    public sealed class AtmosphereDriver : MonoBehaviour
    {
        private Camera _camera;
        private Light _sun;
        private PlanetConfig _planet;
        private float3 _poleAxis;
        private Color _day;
        private Color _dusk;
        private Color _night;
        private float _dayLengthS;

        /// <summary>Starts driving the atmosphere.</summary>
        /// <remarks>
        /// Pre: camera, sun non-null; poleAxis unit; dayLengthS > 0.
        /// Post: fog enabled (linear, 40 m → horizon-scaled far distance). Side effects: RenderSettings.
        /// </remarks>
        public void Configure(Camera camera, Light sun, in PlanetConfig planet, float3 poleAxis, (Color day, Color dusk, Color night) sky,
            float dayLengthS)
        {
            Contract.Requires(camera != null && sun != null && dayLengthS > 0f, "camera, sun and day length required");
            _camera = camera;
            _sun = sun;
            _planet = planet;
            _poleAxis = poleAxis;
            (_day, _dusk, _night) = sky;
            _dayLengthS = dayLengthS;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // R ≈ 640: the eye-height horizon is ~47 m (CLAUDE.md 已知陷阱); fog well past it keeps distant
            // landmarks readable while hiding LOD pop at the far end.
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = math.max(250f, planet.Radius * 0.5f);
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            _sun.transform.Rotate((Vector3)_poleAxis, 360f * Time.deltaTime / _dayLengthS, Space.World);
            float3 cam = _camera.transform.position;
            if (math.distance(cam, _planet.Center) < _planet.Radius * 0.5f)
            {
                return; // camera not placed by Cinemachine yet (first frames): no local "up" to measure against
            }

            float3 up = _planet.ToDirection(cam);
            float e = math.dot(up, -(float3)_sun.transform.forward); // sin(sun elevation)
            Color sky = e > 0.15f ? Color.Lerp(_dusk, _day, math.saturate((e - 0.15f) / 0.35f))
                : Color.Lerp(_night, _dusk, math.saturate((e + 0.15f) / 0.3f));
            _camera.backgroundColor = sky;
            RenderSettings.fogColor = sky;
            RenderSettings.ambientLight = sky * 0.6f;
            _sun.intensity = math.saturate(e * 4f + 0.3f) * 1.2f;
        }
    }
}
