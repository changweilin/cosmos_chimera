using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// The brush-stroke texture the ground shaders paint with, generated in code.
    /// REQ: docs/design/vision.md#美術投資 (貼圖政策：允許程式生成的筆觸貼圖；地形材質混合).
    /// </summary>
    /// <remarks>
    /// Three independent tileable layers, one per channel, each built by painting opaque elliptical marks over one
    /// another (later marks cover earlier ones, like paint), so the result has flat tones and edges instead of the
    /// blur a sum of blobs gives:
    /// R = short dabs at any angle (turf, snow drifts) · G = long streaks along +u (rock chisel marks, sand wind lines) ·
    /// B = fine grain (cracks, speckle). Every channel is recentred to a mean of 0.5 so a stroke never changes a
    /// material's average colour (CosmiBiome.hlsl reads each as an offset from 0.5).
    /// Presentation only: fixed seed, same pixels on every client, nothing in the rule layer reads it.
    /// </remarks>
    public static class StrokeTexture
    {
        /// <summary>Edge length (px) of <see cref="Shared"/>.</summary>
        /// <remarks>Pre: none. Post: constant power of two. Side effects: none.</remarks>
        public const int Size = 256;

        private static Texture2D _shared;

        /// <summary>The stroke texture every ground material samples.</summary>
        /// <remarks>
        /// Pre: main thread. Post: Size × Size, linear RGBA32, repeating, mip-mapped; the same instance on every call
        ///      while it is alive. Side effects: creates the texture on first use (kept for the process lifetime:
        ///      one 256² texture shared by all runs).
        /// </remarks>
        public static Texture2D Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
                    {
                        name = "CosmiStroke",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Trilinear,
                        anisoLevel = 4,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    _shared.SetPixels32(BuildPixels(Size));
                    _shared.Apply(true, true);
                }

                return _shared;
            }
        }

        /// <summary>Paints the stroke layers.</summary>
        /// <remarks>
        /// Pre: size is a power of two in [32, 1024].
        /// Post: size² pixels, row-major; tileable in both axes; each of r, g, b averages 127.5 ± 2; a = 255;
        ///       the same pixels for the same size on every call (fixed seed).
        /// Side effects: allocates the array.
        /// </remarks>
        public static Color32[] BuildPixels(int size)
        {
            Contract.Requires(size >= 32 && size <= 1024 && (size & (size - 1)) == 0, "size must be a power of two in [32, 1024]");
            float unit = size / 256f; // mark sizes are authored for 256 px
            float[] dabs = Paint(size, 0xD4B5ul, 520, 10f * unit, 24f * unit, 0.28f, 0.5f, math.PI);
            float[] streaks = Paint(size, 0x57E4ul, 520, 24f * unit, 60f * unit, 0.06f, 0.12f, 0.2f);
            float[] grain = Paint(size, 0x6A17ul, 1500, 2f * unit, 5f * unit, 0.5f, 0.9f, math.PI);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(ToByte(dabs[i]), ToByte(streaks[i]), ToByte(grain[i]), 255);
            }

            return pixels;
        }

        private static byte ToByte(float v) => (byte)math.clamp((int)math.round(v * 255f), 0, 255);

        // Marks are ellipses with semi-major axis in [minLen, maxLen] px, aspect (minor/major) in [minAspect, maxAspect]
        // and a major axis within ±maxAngle of +u.
        private static float[] Paint(int size, ulong stream, int marks, float minLen, float maxLen, float minAspect,
            float maxAspect, float maxAngle)
        {
            var rng = new Pcg32(0xC05311E2A57201ul, stream);
            var layer = new float[size * size];
            for (int i = 0; i < layer.Length; i++)
            {
                layer[i] = 0.5f;
            }

            for (int m = 0; m < marks; m++)
            {
                float cx = rng.NextFloat() * size;
                float cy = rng.NextFloat() * size;
                float major = math.lerp(minLen, maxLen, rng.NextFloat());
                float minor = major * math.lerp(minAspect, maxAspect, rng.NextFloat());
                float angle = (rng.NextFloat() * 2f - 1f) * maxAngle;
                float tone = rng.NextFloat();
                math.sincos(angle, out float sin, out float cos);
                int reach = (int)math.ceil(major) + 1;
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        float along = (dx * cos + dy * sin) / major;
                        float across = (dy * cos - dx * sin) / minor;
                        float d = along * along + across * across;
                        if (d >= 1f)
                        {
                            continue;
                        }

                        // Opaque core with a short soft rim: enough to anti-alias the edge, not enough to blur it.
                        float cover = math.saturate((1f - d) * 4f);
                        int x = ((int)math.floor(cx) + dx) & (size - 1);
                        int y = ((int)math.floor(cy) + dy) & (size - 1);
                        int index = y * size + x;
                        layer[index] = math.lerp(layer[index], tone, cover);
                    }
                }
            }

            double sum = 0d;
            foreach (float v in layer)
            {
                sum += v;
            }

            float shift = 0.5f - (float)(sum / layer.Length);
            for (int i = 0; i < layer.Length; i++)
            {
                layer[i] = math.saturate(layer[i] + shift);
            }

            return layer;
        }
    }
}
