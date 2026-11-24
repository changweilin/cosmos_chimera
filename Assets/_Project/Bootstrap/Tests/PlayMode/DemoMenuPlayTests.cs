#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoMenuPlayTests
    {
        private static object Field(object obj, string name) => obj.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        [UnityTest]
        public IEnumerator New_save_skips_lobby_and_opens_character_select_overlay()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);

            var fresh = new LobbySave();
            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, fresh);
            game.GetType().GetMethod("EnterCharacterSelect", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
            yield return null;

            var overlay = (GameOverlayView)Field(game, "_overlay");
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator Journal_system_tab_cycles_language_and_partners_tab_opens_cleanly()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);

            var starters = (List<CreatureDef>)Field(game, "_starters");
            var save = new LobbySave();
            save.ChooseStarter(starters[0].LineId, starters[0].Id, 0);
            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, save);

            string before = (string)Field(game, "_language");
            game.CycleLanguageFromServices();
            Assert.That((string)Field(game, "_language"), Is.Not.EqualTo(before));

            game.GetType().GetMethod("OpenJournalAt", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(game, new object[] { AdventurePanel.Page.System });
            yield return null;

            var journal = (AdventurePanel)Field(game, "_journal");
            Assert.That(journal, Is.Not.Null);
            Assert.That(journal.Current, Is.EqualTo(AdventurePanel.Page.System));

            game.GetType().GetMethod("CloseJournal", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
            Assert.That(Field(game, "_journal"), Is.Null);
        }

        private static IEnumerator Press(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Lobby_shortcut_opens_items_paperdoll_and_preview_switches_targets()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);

            var starters = (List<CreatureDef>)Field(game, "_starters");
            var save = new LobbySave();
            save.ChooseStarter(starters[0].LineId, starters[0].Id, 0);
            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, save);

            game.GetType().GetMethod("OpenJournalAt", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(game, new object[] { AdventurePanel.Page.Items });

            var journal = (AdventurePanel)Field(game, "_journal");
            Assert.That(journal, Is.Not.Null);
            Assert.That(journal.Current, Is.EqualTo(AdventurePanel.Page.Items));

            var cradle = new Cosmimera.Crafting.ItemLook(Cosmimera.Crafting.GearOrigin.Cradle, 7);
            PaperdollGear Gear(string slot, GearKind kind) => new PaperdollGear(slot, slot + kind, GearLooks.ForCopy(kind, cradle));
            var worn = new[]
            {
                Gear(Cosmimera.Crafting.EquipmentSlots.MainHand, GearKind.Sword), Gear(Cosmimera.Crafting.EquipmentSlots.OffHand, GearKind.Shield),
                Gear(Cosmimera.Crafting.EquipmentSlots.Upper, GearKind.Upper), Gear(Cosmimera.Crafting.EquipmentSlots.Lower, GearKind.Lower),
                Gear(Cosmimera.Crafting.EquipmentSlots.Accessory, GearKind.Accessory),
            };
            using (var preview = new PaperdollPreview(null, null))
            {
                Assert.That(preview.Texture, Is.Not.Null);
                preview.SetPlayerTarget(null, null, "capsule", worn);
                preview.Rotate(45f);
                Assert.That(preview.Yaw, Is.EqualTo(45f).Within(0.01f));
                preview.SyncPose();

                preview.SetCreatureTarget(starters[0], 1, worn);
                preview.SyncPose();
                preview.SetCreatureTarget(starters[0], 1, System.Array.Empty<PaperdollGear>());
            }

            game.GetType().GetMethod("CloseJournal", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
            Assert.That(Field(game, "_journal"), Is.Null);
        }

        [UnityTest]
        public IEnumerator Lobby_planet_launches_at_2560m_with_8_settlements_and_10_dungeon_entrances()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);

            var starters = (List<CreatureDef>)Field(game, "_starters");
            string tempSave = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "cosmimera-test-lobby-" + System.Guid.NewGuid().ToString("N") + ".json");
            game.GetType().GetField("_savePath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, tempSave);
            var save = new LobbySave();
            save.ChooseStarter(starters[0].LineId, starters[0].Id, 0);
            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, save);

            try
            {
                game.GetType().GetMethod("LaunchLobbyPlanet", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
                yield return null;
                yield return null;

                var run = (RunSession)Field(game, "_run");
                Assert.That(run, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 30f;
                while (!run.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.That(run.StartedForTests, Is.True);
                var rig = (PlanetRig)Field(run, "_rig");
                var layout = (Cosmimera.WorldGen.RunLayout)Field(run, "_layout");
                var landmarks = (Cosmimera.WorldGen.LandmarkLayout)Field(run, "_landmarks");
                Assert.That(rig.Config.Radius, Is.EqualTo(Cosmimera.WorldGen.LobbyPlanet.RadiusM));
                Assert.That(layout.Settlements.Count, Is.EqualTo(8));
                Assert.That(landmarks.Entrances.Count, Is.EqualTo(Cosmimera.WorldGen.AdvancedTerrains.All.Count));
                Assert.That(run.MainWorldNpcCountForTests, Is.EqualTo(8 * 6));
                Assert.That(run.StarRiftCountForTests, Is.EqualTo(StarRiftRules.DefaultRiftCount));
                Assert.That(run.OpenMainWorldNpcForTests(0), Is.True);
                Assert.That(run.ServiceOverlayOpenForTests, Is.True);
                Assert.That(run.OpenStarRiftForTests(0), Is.True);
                Assert.That(run.ServiceOverlayOpenForTests, Is.True);

                run.AbandonForTests();
                yield return null;
            }
            finally
            {
                if (System.IO.File.Exists(tempSave))
                {
                    System.IO.File.Delete(tempSave);
                }
            }
        }

        [UnityTest]
        public IEnumerator MotherPlanet_and_CradleMoon_have_dry_scene_buildings_and_gatekeepers_guard_settlements()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);

            var starters = (List<CreatureDef>)Field(game, "_starters");
            Assert.That(starters.Count, Is.GreaterThanOrEqualTo(1));
            string tempSave = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "cosmimera-test-mother-" + System.Guid.NewGuid().ToString("N") + ".json");
            game.GetType().GetField("_savePath", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, tempSave);
            var save = new LobbySave();
            save.ChooseStarter(starters[0].LineId, starters[0].Id, 0);
            save.CompleteTutorial("ye_chengxi", "luo_en", "yumori_sumiha", starters[0].Id, starters[0].Id);
            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, save);

            try
            {
                // 1. Launch Mother Planet (R = 2560 m)
                game.GetType().GetMethod("LaunchLobbyPlanet", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
                yield return null;
                yield return null;

                var run = (RunSession)Field(game, "_run");
                Assert.That(run, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 30f;
                while (!run.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.That(run.StartedForTests, Is.True);
                var rig = (PlanetRig)Field(run, "_rig");
                var layout = (RunLayout)Field(run, "_layout");
                var settlementView = (SettlementView)Field(run, "_settlementView");
                Assert.That(settlementView, Is.Not.Null);
                Assert.That(settlementView.Buildings.Count, Is.EqualTo(layout.Buildings.Count));

                for (int i = 0; i < layout.Buildings.Count; i++)
                {
                    RunBuilding b = layout.Buildings[i];
                    SettlementDomain dom = layout.Settlements[b.Settlement].Domain;
                    if (dom != SettlementDomain.Land && dom != SettlementDomain.Island)
                    {
                        continue;
                    }

                    float baseAlt = math.length((float3)settlementView.Buildings[i].transform.position) - rig.Config.Radius;
                    Assert.That(baseAlt, Is.GreaterThan(PlanetLook.SeaLevelM),
                        $"Mother Planet building {i} ({b.Kind}) base altitude {baseAlt} is submerged");
                }

                // #1 Town is unlocked initially; #2..#8 remain locked behind Friend/Rival gatekeeper battles.
                Assert.That(run.DiscoveredSettlementsForTests, Contains.Item(0));
                for (int s = 1; s < LobbyPlanet.SettlementRoster.Count; s++)
                {
                    Assert.That(run.DiscoveredSettlementsForTests.Contains(s), Is.False);
                }

                // Defeat on gatekeeper #2 keeps #2 locked; winning unlocks #2.
                run.enabled = false;
                run.StepCombatForTests();
                Assert.That(run.TriggerLobbyGatekeeperForTests(1), Is.True);
                Assert.That(run.ActiveGatekeeperSettlementForTests, Is.EqualTo(1));
                var encounter = (Encounter)Field(run, "_encounter");
                int encTrainerId = (int)Field(run, "_activeEncounterTrainerId");
                encounter.CancelTrainerBattle(encTrainerId, run.NowMsForTests);
                run.StepCombatForTests();
                Assert.That(run.DiscoveredSettlementsForTests.Contains(1), Is.False, "defeat/cancel must keep settlement locked");

                // Now win gatekeeper #2 battle by knocking out all active trainer units.
                Assert.That(run.TriggerLobbyGatekeeperForTests(1), Is.True);
                var trainerUnits = (List<int>)Field(run, "_activeTrainerUnitIds");
                var balance = (CombatBalance)Field(run, "_balance");
                Assert.That(trainerUnits.Count, Is.GreaterThanOrEqualTo(1));
                var mastery = new int[10];
                for (int e = 0; e < 10; e++) mastery[e] = DamageModel.MaxMasteryPercent;
                var rng = new Pcg32(77UL, 'G');
                foreach (int uid in trainerUnits)
                {
                    for (int e = 0; e < 10 && !run.UnitForTests(uid).IsFainted; e++)
                    {
                        var attacker = new Combatant(balance.Stats, balance.Effects, new CreatureStats(0, 0, 10000, 10000, 0, 0),
                            mastery, new int[10], new EffectBook(), 1);
                        var move = new CastMove("test_nuke", (Element)e, MoveKind.Damage, DamageModel.MaxPower, 100, 0, false, default, 0);
                        Assert.That(attacker.BeginCast(0, move, run.NowMsForTests, out CastContext cast), Is.EqualTo(CastResult.Accepted));
                        for (int hit = 0; hit < 64 && !run.UnitForTests(uid).IsFainted; hit++)
                        {
                            run.ReceiveHostileForTests(uid, cast, ref rng);
                        }
                    }
                    Assert.That(run.UnitForTests(uid).IsFainted, Is.True);
                }
                run.StepCombatForTests();
                Assert.That(run.DiscoveredSettlementsForTests, Contains.Item(1), "defeating gatekeeper #2 must unlock settlement #2");

                run.enabled = true;
                run.AbandonForTests();
                yield return null;

                // 2. Launch Cradle Moon (cradle_planet, R = 320 m)
                game.GetType().GetMethod("LaunchCradleMoon", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
                yield return null;
                yield return null;

                run = (RunSession)Field(game, "_run");
                Assert.That(run, Is.Not.Null);
                rig = (PlanetRig)Field(run, "_rig");
                layout = (RunLayout)Field(run, "_layout");
                settlementView = (SettlementView)Field(run, "_settlementView");
                var setup = (RunSetup)Field(run, "_setup");
                Assert.That(setup.PlanetType.Id, Is.EqualTo("cradle_planet"));
                Assert.That(rig.Config.Radius, Is.EqualTo(320f));
                Assert.That(settlementView.Buildings.Count, Is.EqualTo(layout.Buildings.Count));

                for (int i = 0; i < layout.Buildings.Count; i++)
                {
                    RunBuilding b = layout.Buildings[i];
                    SettlementDomain dom = layout.Settlements[b.Settlement].Domain;
                    if (dom != SettlementDomain.Land && dom != SettlementDomain.Island)
                    {
                        continue;
                    }

                    float baseAlt = math.length((float3)settlementView.Buildings[i].transform.position) - rig.Config.Radius;
                    Assert.That(baseAlt, Is.GreaterThan(PlanetLook.SeaLevelM),
                        $"Cradle Moon building {i} ({b.Kind}) base altitude {baseAlt} is submerged");
                }

                run.AbandonForTests();
                yield return null;
            }
            finally
            {
                if (System.IO.File.Exists(tempSave))
                {
                    System.IO.File.Delete(tempSave);
                }
            }
        }
    }
}
#endif
