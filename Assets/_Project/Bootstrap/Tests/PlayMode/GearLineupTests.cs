using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cosmimera.Crafting;
using Cosmimera.GameData;
using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/gear-looks.md — test-bench screenshots of the placeholder gear (Logs/gear-*.png) for the user to pick from.
    public sealed class GearLineupTests
    {
        private GameObject _stage;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_stage != null) Object.Destroy(_stage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_gear_lineup_when_requested()
        {
            if (Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            _stage = new GameObject("gear-lineup");
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(_stage.transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.7f, 0.85f) * 0.65f;
            Material material = PlanetLook.SceneryMaterial(scenery, Color.white, Color.white, 0f, 0f, 0f, 0f);

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(_stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.74f, 0.84f);
            camera.fieldOfView = 30f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");

            // One sheet per era: columns are kinds, rows are three seeds (pristine crafted, settlement, ruin-worn).
            GearKind[] kinds = (GearKind[])Enum.GetValues(typeof(GearKind));
            ArchEra[] eras = (ArchEra[])Enum.GetValues(typeof(ArchEra));
            const float columnM = 1.6f, rowM = 2.6f;
            for (int e = 0; e < eras.Length; e++)
            {
                var sheet = new GameObject("era-" + eras[e]);
                sheet.transform.SetParent(_stage.transform, false);
                sheet.transform.localPosition = new Vector3(0f, 0f, e * 100f);
                ArchMix[] sources =
                {
                    GearLooks.Crafted((ArchRegion)(e % 6), eras[e]),
                    new ArchMix(ArchRegion.Crystal, ArchEra.Industrial, 0.4f, 0.4f, 0.15f),
                    new ArchMix(ArchRegion.Storm, ArchEra.Wasteland, 0.3f, 0.85f, 0.75f),
                };
                for (int row = 0; row < sources.Length; row++)
                {
                    for (int k = 0; k < kinds.Length; k++)
                    {
                        GearLookSpec spec = GearLooks.Sample(kinds[k], (GearContext)((k + row) % 5), (ArchRegion)((e + row) % 6), eras[e],
                            sources[row], (ulong)(e * 997 + row * 131 + k + 1));
                        Mesh mesh = GearMeshes.Build(spec);
                        // Normalised to ~1.4 m so a charm reads next to a spear; life size is in the EditMode bounds test.
                        float scale = Mathf.Clamp(1.4f / GearMeshes.NominalLength(kinds[k]), 0.6f, 8f);
                        var go = new GameObject(kinds[k] + "_" + row);
                        go.transform.SetParent(sheet.transform, false);
                        go.transform.localPosition = new Vector3((k - (kinds.Length - 1) * 0.5f) * columnM, -row * rowM, 0f);
                        go.transform.localRotation = Quaternion.Euler(0f, 205f, 0f); // front faces (+Z) toward the camera
                        go.transform.localScale = Vector3.one * scale;
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = material;
                    }
                }

                yield return null;
                // Three crops of seven kinds each (melee, ranged and focus, armour and ornaments): a whole row is too small to read.
                for (int group = 0; group < 3; group++)
                {
                    float cx = (group * 7 + 3 - (kinds.Length - 1) * 0.5f) * columnM;
                    camera.transform.position = new Vector3(cx, -rowM + 2.2f, e * 100f - 16f);
                    camera.transform.LookAt(new Vector3(cx, -rowM + 0.7f, e * 100f));
                    ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "gear-" + eras[e] + "-" + group + ".png"));
                }
            }
        }

        private static object Field(object obj, string name) => obj.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        [UnityTest]
        public IEnumerator Capture_paperdoll_gear_when_requested()
        {
            if (Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            var models = (CharacterModelTable)Field(game, "_characterModels");
            var starters = (List<CreatureDef>)Field(game, "_starters");
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");

            // Two origins so the sheet shows how the planet changes the same item.
            var origins = new[] { new GearOrigin("lava_planet", 11), new GearOrigin("ice_planet", 5) };
            for (int o = 0; o < origins.Length; o++)
            {
                PaperdollGear Gear(string slot, GearKind kind, ulong seed) =>
                    new PaperdollGear(slot, slot + kind + seed, GearLooks.ForCopy(kind, new ItemLook(origins[o], seed)));
                var worn = new[]
                {
                    Gear(EquipmentSlots.MainHand, GearKind.Sword, 1), Gear(EquipmentSlots.OffHand, GearKind.Shield, 2),
                    Gear(EquipmentSlots.Upper, GearKind.Upper, 3), Gear(EquipmentSlots.Lower, GearKind.Lower, 4),
                    Gear(EquipmentSlots.Accessory, GearKind.Accessory, 5),
                };
                using (var preview = new PaperdollPreview((Shader)Field(game, "_toonShader"), (Shader)Field(game, "_sceneryShader")))
                {
                    CharacterModelEntry entry = models.Entries[0];
                    preview.SetPlayerTarget(entry.prefab, entry.animatorController, entry.id, worn);
                    // Front three-quarter, then back three-quarter (the preview starts at yaw 180).
                    foreach (float turn in new[] { 205f, 180f })
                    {
                        preview.Rotate(turn);
                        preview.SyncPose();
                        for (int f = 0; f < 3; f++) yield return null;
                        SaveTexture(preview.Texture, System.IO.Path.Combine(dir,
                            "paperdoll-player-" + origins[o].PlanetTypeId + "-" + Mathf.RoundToInt(preview.Yaw) + ".png"));
                    }

                    preview.SetCreatureTarget(starters[o % starters.Count], 1, worn);
                    preview.Rotate(180f); // back to the front three-quarter
                    preview.SyncPose();
                    for (int f = 0; f < 3; f++) yield return null;
                    SaveTexture(preview.Texture, System.IO.Path.Combine(dir, "paperdoll-creature-" + origins[o].PlanetTypeId + ".png"));
                }
            }
        }

        private static void SaveTexture(Texture texture, string path)
        {
            var rt = (RenderTexture)texture;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
        }
    }
}
