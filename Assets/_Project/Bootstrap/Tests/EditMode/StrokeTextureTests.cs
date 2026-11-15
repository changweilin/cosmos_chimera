using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: vision.md#美術投資 — the ground's brush strokes are generated in code, identically on every client.
    public sealed class StrokeTextureTests
    {
        private static byte Channel(Color32 c, int channel) => channel == 0 ? c.r : channel == 1 ? c.g : c.b;

        [Test]
        public void Same_size_gives_the_same_pixels()
        {
            Assert.That(StrokeTexture.BuildPixels(64), Is.EqualTo(StrokeTexture.BuildPixels(64)));
        }

        [Test]
        public void Every_channel_is_centred_and_has_contrast()
        {
            Color32[] pixels = StrokeTexture.BuildPixels(StrokeTexture.Size);
            Assert.That(pixels.Length, Is.EqualTo(StrokeTexture.Size * StrokeTexture.Size));
            for (int channel = 0; channel < 3; channel++)
            {
                double sum = 0d, squares = 0d;
                foreach (Color32 p in pixels)
                {
                    Assert.That(p.a, Is.EqualTo(255));
                    double v = Channel(p, channel);
                    sum += v;
                    squares += v * v;
                }

                double mean = sum / pixels.Length;
                double deviation = System.Math.Sqrt(squares / pixels.Length - mean * mean);
                // A stroke must not shift a material's average colour, and a flat layer would paint nothing.
                Assert.That(mean, Is.EqualTo(127.5).Within(2.0), "channel " + channel);
                Assert.That(deviation, Is.GreaterThan(30.0), "channel " + channel);
            }
        }

        [Test]
        public void Layers_tile_without_a_seam()
        {
            // A seam would show as a jump across the wrap edge larger than the jumps inside the texture.
            const int n = StrokeTexture.Size;
            Color32[] pixels = StrokeTexture.BuildPixels(n);
            for (int channel = 0; channel < 3; channel++)
            {
                // Each wrap edge against an interior edge of the same axis: the streak layer changes far more along v.
                double innerX = 0d, innerY = 0d, wrapX = 0d, wrapY = 0d;
                for (int k = 0; k < n; k++)
                {
                    innerX += Mathf.Abs(Channel(pixels[k * n + n / 2], channel) - Channel(pixels[k * n + n / 2 - 1], channel));
                    innerY += Mathf.Abs(Channel(pixels[n / 2 * n + k], channel) - Channel(pixels[(n / 2 - 1) * n + k], channel));
                    wrapX += Mathf.Abs(Channel(pixels[k * n], channel) - Channel(pixels[k * n + n - 1], channel));
                    wrapY += Mathf.Abs(Channel(pixels[k], channel) - Channel(pixels[(n - 1) * n + k], channel));
                }

                Assert.That(wrapX, Is.LessThan(innerX * 2.5 + n), "u seam, channel " + channel);
                Assert.That(wrapY, Is.LessThan(innerY * 2.5 + n), "v seam, channel " + channel);
            }
        }

        [Test]
        public void Streaks_run_along_u()
        {
            // The rock and sand terms rely on the green channel being directional: it must change far less along u.
            const int n = StrokeTexture.Size;
            Color32[] pixels = StrokeTexture.BuildPixels(n);
            double alongU = 0d, alongV = 0d;
            for (int y = 0; y < n - 1; y++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    alongU += Mathf.Abs(pixels[y * n + x + 1].g - pixels[y * n + x].g);
                    alongV += Mathf.Abs(pixels[(y + 1) * n + x].g - pixels[y * n + x].g);
                }
            }

            Assert.That(alongV, Is.GreaterThan(alongU * 2.0));
        }

        [Test]
        public void Shared_texture_is_repeating_linear_and_reused()
        {
            Texture2D shared = StrokeTexture.Shared;
            Assert.That(shared, Is.SameAs(StrokeTexture.Shared));
            Assert.That(shared.width, Is.EqualTo(StrokeTexture.Size));
            Assert.That(shared.height, Is.EqualTo(StrokeTexture.Size));
            Assert.That(shared.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(shared.mipmapCount, Is.GreaterThan(1));
        }

        [Test]
        public void Size_must_be_a_power_of_two_in_range()
        {
            Assert.Throws<ContractException>(() => StrokeTexture.BuildPixels(16));
            Assert.Throws<ContractException>(() => StrokeTexture.BuildPixels(100));
            Assert.Throws<ContractException>(() => StrokeTexture.BuildPixels(2048));
        }
    }
}
