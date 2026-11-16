using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Presentation-only combat feedback for D0: floating MISS/damage/heal text, procedural SFX,
    /// dodge afterimages, cast shape pulses, and active ground field rings.
    /// </summary>
    /// <remarks>
    /// Pre: created on the main thread; <see cref="Dispose"/> called on teardown.
    /// Post: all runtime-created clips, materials and pooled GameObjects are destroyed on <see cref="Dispose"/>.
    /// Side effects: creates an <see cref="AudioSource"/>, procedural <see cref="AudioClip"/>s and pooled VFX primitives.
    /// </remarks>
    internal sealed class CombatFeedback : IDisposable
    {
        private struct FloatingText
        {
            public float3 WorldPos;
            public float3 LocalUp;
            public string Text;
            public Color Color;
            public float Age;
            public float Duration;
            public bool Large;
        }

        private sealed class PulseVfx
        {
            public GameObject Go;
            public Renderer Renderer;
            public Material Material;
            public Vector3 BaseScale;
            public float Age;
            public float Duration;
            public Color Color;
            public bool Expand;
        }

        private sealed class FieldVfx
        {
            public GameObject Go;
            public Material Material;
        }

        private const int MaxFloatingTexts = 48;
        private const int MaxPulsePool = 24;

        private readonly Transform _parent;
        private readonly AudioSource _audio;
        private readonly AudioClip _missClip;
        private readonly AudioClip _hitClip;
        private readonly AudioClip _healClip;
        private readonly AudioClip _overloadClip;
        private readonly AudioClip _castClip;
        private readonly Shader _vfxShader;

        private readonly List<FloatingText> _texts = new List<FloatingText>(MaxFloatingTexts);
        private readonly List<PulseVfx> _pulses = new List<PulseVfx>(MaxPulsePool);
        private readonly List<FieldVfx> _fieldRings = new List<FieldVfx>(8);

        private GUIStyle _textStyle;
        private GUIStyle _largeStyle;
        private bool _disposed;

        /// <summary>Initializes audio clips, audio source and VFX shader lookup.</summary>
        /// <remarks>
        /// Pre: host != null on the main thread.
        /// Post: ready to emit feedback and draw floating texts.
        /// Side effects: adds an AudioSource component to <paramref name="host"/> and allocates 5 AudioClips.
        /// </remarks>
        public CombatFeedback(GameObject host)
        {
            Contract.Requires(host != null, "host GameObject required");
            _parent = host.transform;
            _audio = host.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.45f;

            _missClip = BuildNoiseWhooshClip("SfxMiss", 0.16f, 950f, 380f);
            _hitClip = BuildToneClip("SfxHit", 0.12f, 160f, 65f, 0.55f);
            _healClip = BuildChimeClip("SfxHeal", 0.24f, 523.25f, 659.25f);
            _overloadClip = BuildChimeClip("SfxOverload", 0.36f, 392.0f, 587.33f);
            _castClip = BuildToneClip("SfxCast", 0.10f, 340f, 220f, 0.25f);

            _vfxShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        }

        /// <summary>Records a MISS popup, dodge afterimage and miss SFX.</summary>
        /// <remarks>Pre: not disposed. Post: floating text and VFX queued. Side effects: plays SFX and spawns pooled VFX.</remarks>
        public void ShowMiss(float3 worldPos, float3 forward, float3 localUp, float radiusM)
        {
            if (_disposed)
            {
                return;
            }

            AddText(worldPos + localUp * (radiusM + 0.6f), localUp, "MISS", new Color(0.55f, 0.92f, 1f, 1f), 0.95f, true);
            SpawnPulse(PrimitiveType.Capsule, worldPos + localUp * (radiusM * 0.5f), forward, localUp,
                new Vector3(radiusM * 1.1f, radiusM * 0.8f, radiusM * 1.1f),
                new Color(0.5f, 0.95f, 1f, 0.55f), 0.32f, expand: true);
            Play(_missClip, 0.55f);
        }

        /// <summary>Records a damage popup, hit flash and impact SFX scaled by <paramref name="tier"/> (combat-feel.md#命中回饋分級).</summary>
        /// <remarks>
        /// Pre: not disposed; amount >= 0; tier is Immune..Knockout.
        /// Post: feedback queued; Immune shows a grey "×0", Heavy/Knockout use large text, weakness hits use the weakness colour.
        /// Side effects: plays SFX and spawns pooled VFX.
        /// </remarks>
        public void ShowHit(float3 worldPos, float3 forward, float3 localUp, float radiusM, int amount, Color elementColor,
            HitTier tier, bool weakness)
        {
            if (_disposed)
            {
                return;
            }

            float3 textPos = worldPos + localUp * (radiusM + 0.5f);
            if (tier == HitTier.Immune)
            {
                // Language-neutral on purpose: strings.json is generated by tools/demo-data and has no immune key yet.
                AddText(textPos, localUp, "×0", new Color(0.7f, 0.72f, 0.75f, 1f), 0.8f, false);
                SpawnPulse(PrimitiveType.Sphere, worldPos + localUp * (radiusM * 0.5f), forward, localUp,
                    Vector3.one * math.max(0.5f, radiusM * 1.1f), new Color(0.75f, 0.78f, 0.82f, 0.4f), 0.16f, expand: false);
                Play(_hitClip, 0.25f);
                return;
            }

            bool big = tier >= HitTier.Heavy;
            if (amount > 0)
            {
                Color col = weakness ? new Color(1f, 0.82f, 0.18f, 1f) : new Color(1f, 0.42f, 0.28f, 1f);
                AddText(textPos, localUp, big ? $"-{amount}!" : $"-{amount}", col, big ? 1.05f : 0.85f, big);
            }

            float scale = tier switch
            {
                HitTier.Knockout => 2.1f,
                HitTier.Heavy => 1.7f,
                HitTier.Medium => 1.3f,
                _ => 1f,
            };
            SpawnPulse(PrimitiveType.Sphere, worldPos + localUp * (radiusM * 0.5f), forward, localUp,
                Vector3.one * math.max(0.6f, radiusM * 1.4f) * scale,
                new Color(elementColor.r, elementColor.g, elementColor.b, 0.55f), 0.22f, expand: true);
            if (tier >= HitTier.Medium)
            {
                // White core flash: the "閃白" beat, read as impact rather than as the element tint.
                SpawnPulse(PrimitiveType.Sphere, worldPos + localUp * (radiusM * 0.5f), forward, localUp,
                    Vector3.one * math.max(0.4f, radiusM * 0.9f) * scale, new Color(1f, 1f, 1f, 0.8f),
                    tier >= HitTier.Heavy ? 0.1f : 0.08f, expand: false);
            }

            Play(_hitClip, big ? 0.85f : tier == HitTier.Medium ? 0.65f : 0.5f);
        }

        /// <summary>Boss crossed an HP phase line: shockwave ring and roar-like SFX (presentation only, combat-feel.md#BOSS-戰加強).</summary>
        /// <remarks>Pre: not disposed. Post: VFX queued. Side effects: plays SFX and spawns pooled VFX.</remarks>
        public void ShowBossPhase(float3 worldPos, float3 forward, float3 localUp, float radiusM, Color elementColor)
        {
            if (_disposed)
            {
                return;
            }

            SpawnPulse(PrimitiveType.Cylinder, worldPos + localUp * 0.1f, forward, localUp,
                new Vector3(radiusM * 9f, 0.06f, radiusM * 9f), new Color(elementColor.r, elementColor.g, elementColor.b, 0.6f),
                0.6f, expand: true);
            SpawnPulse(PrimitiveType.Sphere, worldPos + localUp * (radiusM * 0.6f), forward, localUp,
                Vector3.one * radiusM * 3.2f, new Color(1f, 1f, 1f, 0.5f), 0.3f, expand: true);
            Play(_overloadClip, 0.8f);
        }

        /// <summary>Records a heal/support popup and chime SFX.</summary>
        /// <remarks>Pre: not disposed. Post: feedback queued. Side effects: plays SFX and spawns pooled VFX.</remarks>
        public void ShowSupport(float3 worldPos, float3 forward, float3 localUp, float radiusM, int healedAmount)
        {
            if (_disposed)
            {
                return;
            }

            string text = healedAmount > 0 ? $"+{healedAmount}" : "BUFF";
            Color col = healedAmount > 0 ? new Color(0.35f, 0.96f, 0.52f, 1f) : new Color(0.95f, 0.9f, 0.45f, 1f);
            AddText(worldPos + localUp * (radiusM + 0.5f), localUp, text, col, 0.85f, false);
            SpawnPulse(PrimitiveType.Cylinder, worldPos + localUp * 0.1f, forward, localUp,
                new Vector3(radiusM * 2.2f, 0.08f, radiusM * 2.2f),
                new Color(col.r, col.g, col.b, 0.45f), 0.30f, expand: true);
            Play(_healClip, 0.45f);
        }

        /// <summary>Records a cast shape pulse and subtle cast SFX.</summary>
        /// <remarks>Pre: not disposed; move != null. Post: cast VFX queued. Side effects: plays SFX and spawns pooled VFX.</remarks>
        public void ShowCast(float3 casterPos, float3 forward, float3 localUp, MoveDef move, Color elementColor)
        {
            if (_disposed || move == null)
            {
                return;
            }

            float rangeM = math.clamp(move.BaseRangeMm * 0.001f, 1.2f, 18f);
            Vector3 scale;
            float3 center;
            if (move.Shape == MoveShape.Line || move.Shape == MoveShape.Front)
            {
                float len = math.min(rangeM, 10f);
                scale = new Vector3(1.1f, 0.15f, len);
                center = casterPos + forward * (len * 0.5f) + localUp * 0.35f;
            }
            else if (move.Shape == MoveShape.Burst || move.Shape == MoveShape.Field)
            {
                float d = math.min(rangeM * 2f, 12f);
                scale = new Vector3(d, 0.12f, d);
                center = casterPos + localUp * 0.15f;
            }
            else
            {
                float d = math.min(rangeM * 0.9f, 7f);
                scale = new Vector3(d, 0.15f, d);
                center = casterPos + forward * (d * 0.35f) + localUp * 0.3f;
            }

            SpawnPulse(PrimitiveType.Cube, center, forward, localUp, scale,
                new Color(elementColor.r, elementColor.g, elementColor.b, 0.42f), 0.24f, expand: false);
            Play(_castClip, 0.28f);
        }

        /// <summary>Records an overload activation banner, aura burst and power-up SFX.</summary>
        /// <remarks>Pre: not disposed. Post: overload feedback queued. Side effects: plays SFX and spawns pooled VFX.</remarks>
        public void ShowOverload(float3 worldPos, float3 forward, float3 localUp, float radiusM, string label)
        {
            if (_disposed)
            {
                return;
            }

            AddText(worldPos + localUp * (radiusM + 1.0f), localUp, label, new Color(1f, 0.85f, 0.2f, 1f), 1.15f, true);
            SpawnPulse(PrimitiveType.Sphere, worldPos + localUp * (radiusM * 0.6f), forward, localUp,
                Vector3.one * math.max(1.5f, radiusM * 2.8f),
                new Color(1f, 0.82f, 0.18f, 0.6f), 0.45f, expand: true);
            Play(_overloadClip, 0.6f);
        }

        /// <summary>Advances floating text and pulse VFX timers, and updates active ground field rings.</summary>
        /// <remarks>
        /// Pre: not disposed; dt >= 0; activeFields non-null.
        /// Post: expired entries deactivated/removed; ground rings match <paramref name="activeFields"/>.
        /// Side effects: updates pooled GameObject transforms and material alpha.
        /// </remarks>
        public void Update(float dt, IReadOnlyList<ActiveFieldSnapshot> activeFields, PlanetConfig planet)
        {
            if (_disposed)
            {
                return;
            }

            for (int i = _texts.Count - 1; i >= 0; i--)
            {
                FloatingText t = _texts[i];
                t.Age += dt;
                if (t.Age >= t.Duration)
                {
                    _texts.RemoveAt(i);
                }
                else
                {
                    t.WorldPos += t.LocalUp * (1.25f * dt);
                    _texts[i] = t;
                }
            }

            for (int i = 0; i < _pulses.Count; i++)
            {
                PulseVfx p = _pulses[i];
                if (!p.Go.activeSelf)
                {
                    continue;
                }

                p.Age += dt;
                if (p.Age >= p.Duration)
                {
                    p.Go.SetActive(false);
                    continue;
                }

                float k = p.Age / p.Duration;
                float s = p.Expand ? Mathf.Lerp(0.65f, 1.45f, k) : Mathf.Lerp(1f, 0.85f, k);
                p.Go.transform.localScale = p.BaseScale * s;
                Color c = p.Color;
                c.a = p.Color.a * (1f - k);
                SetMaterialColor(p.Material, c);
            }

            SyncFields(activeFields, planet);
        }

        /// <summary>Draws floating combat text projected onto the active camera.</summary>
        /// <remarks>Pre: called from OnGUI; cam may be null. Post: none. Side effects: draws GUI labels.</remarks>
        public void DrawFloatingTexts(Camera cam)
        {
            if (_disposed || cam == null || _texts.Count == 0)
            {
                return;
            }

            EnsureStyles();
            for (int i = 0; i < _texts.Count; i++)
            {
                FloatingText t = _texts[i];
                Vector3 screen = cam.WorldToScreenPoint((Vector3)t.WorldPos);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float alpha = 1f - Mathf.Clamp01(t.Age / t.Duration);
                GUIStyle style = t.Large ? _largeStyle : _textStyle;
                float w = t.Large ? 160f : 110f;
                float h = t.Large ? 30f : 24f;
                var rect = new Rect(screen.x - w * 0.5f, Screen.height - screen.y - h * 0.5f, w, h);

                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, alpha * 0.75f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), t.Text, style);
                GUI.color = new Color(t.Color.r, t.Color.g, t.Color.b, t.Color.a * alpha);
                GUI.Label(rect, t.Text, style);
                GUI.color = prev;
            }
        }

        /// <summary>Destroys all pooled GameObjects, materials and generated AudioClips.</summary>
        /// <remarks>Pre: main thread. Post: disposed; subsequent calls are no-ops. Side effects: Unity Object.Destroy calls.</remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = 0; i < _pulses.Count; i++)
            {
                if (_pulses[i].Go != null)
                {
                    UnityEngine.Object.Destroy(_pulses[i].Go);
                }

                if (_pulses[i].Material != null)
                {
                    UnityEngine.Object.Destroy(_pulses[i].Material);
                }
            }

            _pulses.Clear();
            for (int i = 0; i < _fieldRings.Count; i++)
            {
                if (_fieldRings[i].Go != null)
                {
                    UnityEngine.Object.Destroy(_fieldRings[i].Go);
                }

                if (_fieldRings[i].Material != null)
                {
                    UnityEngine.Object.Destroy(_fieldRings[i].Material);
                }
            }

            _fieldRings.Clear();
            _texts.Clear();

            DestroyClip(_missClip);
            DestroyClip(_hitClip);
            DestroyClip(_healClip);
            DestroyClip(_overloadClip);
            DestroyClip(_castClip);
        }

        private void AddText(float3 worldPos, float3 localUp, string text, Color color, float duration, bool large)
        {
            if (_texts.Count >= MaxFloatingTexts)
            {
                _texts.RemoveAt(0);
            }

            _texts.Add(new FloatingText
            {
                WorldPos = worldPos,
                LocalUp = localUp,
                Text = text,
                Color = color,
                Age = 0f,
                Duration = duration,
                Large = large,
            });
        }

        private void SpawnPulse(PrimitiveType shape, float3 worldPos, float3 forward, float3 localUp, Vector3 scale,
            Color color, float duration, bool expand)
        {
            PulseVfx slot = null;
            for (int i = 0; i < _pulses.Count; i++)
            {
                if (!_pulses[i].Go.activeSelf)
                {
                    slot = _pulses[i];
                    break;
                }
            }

            if (slot == null)
            {
                if (_pulses.Count >= MaxPulsePool)
                {
                    slot = _pulses[0];
                }
                else
                {
                    var go = GameObject.CreatePrimitive(shape);
                    go.name = "CombatPulseVfx";
                    go.transform.SetParent(_parent, false);
                    Collider col = go.GetComponent<Collider>();
                    if (col != null)
                    {
                        UnityEngine.Object.Destroy(col);
                    }

                    Renderer r = go.GetComponent<Renderer>();
                    Material mat = CreateTranslucentMaterial(color);
                    r.sharedMaterial = mat;
                    slot = new PulseVfx { Go = go, Renderer = r, Material = mat };
                    _pulses.Add(slot);
                }
            }

            Vector3 fwd = math.lengthsq(forward) > 1e-6f ? (Vector3)math.normalize(forward) : Vector3.forward;
            Vector3 up = math.lengthsq(localUp) > 1e-6f ? (Vector3)math.normalize(localUp) : Vector3.up;
            slot.Go.transform.SetPositionAndRotation((Vector3)worldPos, Quaternion.LookRotation(fwd, up));
            slot.BaseScale = scale;
            slot.Go.transform.localScale = scale;
            slot.Age = 0f;
            slot.Duration = duration;
            slot.Color = color;
            slot.Expand = expand;
            SetMaterialColor(slot.Material, color);
            slot.Go.SetActive(true);
        }

        private void SyncFields(IReadOnlyList<ActiveFieldSnapshot> activeFields, PlanetConfig planet)
        {
            if (activeFields == null)
            {
                return;
            }

            while (_fieldRings.Count < activeFields.Count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "CombatFieldRing";
                go.transform.SetParent(_parent, false);
                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    UnityEngine.Object.Destroy(col);
                }

                Material mat = CreateTranslucentMaterial(new Color(1f, 0.5f, 0.2f, 0.28f));
                go.GetComponent<Renderer>().sharedMaterial = mat;
                _fieldRings.Add(new FieldVfx { Go = go, Material = mat });
            }

            for (int i = 0; i < _fieldRings.Count; i++)
            {
                FieldVfx ring = _fieldRings[i];
                if (i >= activeFields.Count)
                {
                    if (ring.Go.activeSelf)
                    {
                        ring.Go.SetActive(false);
                    }

                    continue;
                }

                ActiveFieldSnapshot f = activeFields[i];
                float3 up = planet.ToDirection(f.Anchor);
                float3 fwd = PlanetFrame.FromUp(up).North;
                Color c = PlaceholderCreatureViewFactory.ColorOf(f.Element);
                float fade = f.DurationMs > 0 ? math.clamp((float)f.RemainingMs / f.DurationMs, 0.2f, 1f) : 1f;
                c.a = 0.26f * fade;
                SetMaterialColor(ring.Material, c);
                ring.Go.transform.SetPositionAndRotation((Vector3)(f.Anchor + up * 0.08f),
                    Quaternion.LookRotation((Vector3)fwd, (Vector3)up));
                float d = f.RadiusM * 2f;
                ring.Go.transform.localScale = new Vector3(d, 0.05f, d);
                if (!ring.Go.activeSelf)
                {
                    ring.Go.SetActive(true);
                }
            }
        }

        private Material CreateTranslucentMaterial(Color color)
        {
            var mat = _vfxShader != null ? new Material(_vfxShader) : new Material(Shader.Find("Standard"));
            // Configure URP Unlit / standard transparent surface mode cleanly without external asset dependencies.
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }

            if (mat.HasProperty("_SrcBlend"))
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetInt("_ZWrite", 0);
            }

            mat.renderQueue = 3000;
            SetMaterialColor(mat, color);
            return mat;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat == null)
            {
                return;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }

        private void EnsureStyles()
        {
            if (_textStyle != null)
            {
                return;
            }

            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _largeStyle = new GUIStyle(_textStyle)
            {
                fontSize = 21,
            };
        }

        private void Play(AudioClip clip, float volumeScale)
        {
            if (_audio != null && clip != null)
            {
                _audio.PlayOneShot(clip, volumeScale);
            }
        }

        private static void DestroyClip(AudioClip clip)
        {
            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }

        private static AudioClip BuildNoiseWhooshClip(string name, float seconds, float startHz, float endHz)
        {
            const int rate = 22050;
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * rate));
            var data = new float[samples];
            float phase = 0f;
            uint rng = 0x9E3779B9u;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float env = Mathf.Sin(t * Mathf.PI);
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += (2f * Mathf.PI * hz) / rate;
                rng = rng * 1664525u + 1013904223u;
                float noise = ((rng >> 16) / 32768f) - 1f;
                data[i] = (0.55f * Mathf.Sin(phase) + 0.45f * noise) * env * 0.4f;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildToneClip(string name, float seconds, float startHz, float endHz, float gain)
        {
            const int rate = 22050;
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * rate));
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float env = (1f - t) * (1f - t);
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += (2f * Mathf.PI * hz) / rate;
                data[i] = Mathf.Sin(phase) * env * gain;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildChimeClip(string name, float seconds, float hzA, float hzB)
        {
            const int rate = 22050;
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * rate));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float env = Mathf.Sin(Mathf.Min(1f, t * 4f) * Mathf.PI * 0.5f) * (1f - t);
                float hz = t < 0.45f ? hzA : hzB;
                float wave = Mathf.Sin(2f * Mathf.PI * hz * (i / (float)rate));
                data[i] = wave * env * 0.42f;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
