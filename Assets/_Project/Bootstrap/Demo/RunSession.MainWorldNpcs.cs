using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Capture;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using Cosmimera.Planet;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private sealed class MainWorldNpcInstance
        {
            public SettlementNpcPlacement Placement;
            public GameObject Root;
            public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        }

        private readonly List<MainWorldNpcInstance> _mainWorldNpcs = new List<MainWorldNpcInstance>();
        private int _nearestMainWorldNpcIndex = -1;
        private GameOverlayView _serviceOverlay;
        private bool _serviceOverlayOpen;
        private bool _craftShowLocked;
        private string _npcFeedback = string.Empty;

        internal int MainWorldNpcCountForTests => _mainWorldNpcs.Count;
        internal bool ServiceOverlayOpenForTests => _serviceOverlayOpen;

        internal bool OpenMainWorldNpcForTests(int index)
        {
            if (index < 0 || index >= _mainWorldNpcs.Count)
            {
                return false;
            }

            OpenMainWorldNpcOverlay(_mainWorldNpcs[index].Placement);
            return true;
        }

        private void ConfigureMainWorldNpcs(Shader toonShader)
        {
            DisposeMainWorldNpcs();
            if (!_setup.IsLobbyPlanet || _layout == null || _layout.Settlements.Count == 0)
            {
                return;
            }

            var centers = new List<float3>(_layout.Settlements.Count);
            for (int i = 0; i < _layout.Settlements.Count; i++)
            {
                centers.Add(_layout.Settlements[i].Center);
            }

            List<SettlementNpcPlacement> placements = MainWorldNpcRules.BuildSettlementNpcs(
                centers, _rig.Config.Radius, _setup.Seed);
            for (int i = 0; i < placements.Count; i++)
            {
                SettlementNpcPlacement p = placements[i];
                var inst = new MainWorldNpcInstance { Placement = p };
                if (toonShader != null)
                {
                    BuildMainWorldNpcView(inst, toonShader, i);
                }
                _mainWorldNpcs.Add(inst);
            }
        }

        private void BuildMainWorldNpcView(MainWorldNpcInstance inst, Shader toonShader, int index)
        {
            SettlementNpcPlacement p = inst.Placement;
            NpcSpec spec = NpcMeshes.SpecForTrialMaster(_setup.Seed ^ unchecked((ulong)(index + 503)));
            var root = new GameObject($"MainWorldNpc_S{p.SettlementIndex + 1}_{p.Role}");
            Mesh mesh = NpcMeshes.Build(spec, root.transform, out ResidentStride stride);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = stride.Bones;
            renderer.rootBone = root.transform;
            Color primary = NpcRoleColor(p.Role);
            Material body = PlanetLook.CreatureMaterial(toonShader, primary, 0.02f);
            Material accent = PlanetLook.CreatureMaterial(toonShader, new Color(0.95f, 0.92f, 0.8f), 0f);
            renderer.sharedMaterials = new[] { body, accent };

            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(halo.GetComponent<Collider>());
            halo.transform.SetParent(root.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            halo.transform.localScale = new Vector3(1.4f, 0.01f, 1.4f);
            Material haloMat = PlanetLook.CreatureMaterial(toonShader, primary, 0f);
            halo.GetComponent<Renderer>().sharedMaterial = haloMat;

            float3 world = _rig.Config.ToWorld(p.Direction, _surface.GroundAltitude(p.Direction) - 0.03f);
            root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                Quaternion.LookRotation((Vector3)p.Facing, (Vector3)p.Direction));

            inst.Owned.Add(mesh);
            inst.Owned.Add(body);
            inst.Owned.Add(accent);
            inst.Owned.Add(haloMat);
            inst.Root = root;
        }

        private static Color NpcRoleColor(SettlementNpcRole role) => role switch
        {
            SettlementNpcRole.Merchant => new Color(0.92f, 0.74f, 0.22f),
            SettlementNpcRole.Craftsman => new Color(0.88f, 0.45f, 0.20f),
            SettlementNpcRole.Hatchery => new Color(0.92f, 0.60f, 0.72f),
            SettlementNpcRole.VesselConverter => new Color(0.64f, 0.42f, 0.92f),
            SettlementNpcRole.QuestGuide => new Color(0.30f, 0.72f, 0.92f),
            SettlementNpcRole.Healer => new Color(0.28f, 0.84f, 0.52f),
            _ => Color.white,
        };

        private void DisposeMainWorldNpcs()
        {
            for (int i = 0; i < _mainWorldNpcs.Count; i++)
            {
                MainWorldNpcInstance inst = _mainWorldNpcs[i];
                if (inst.Root != null)
                {
                    Destroy(inst.Root);
                }

                for (int o = 0; o < inst.Owned.Count; o++)
                {
                    if (inst.Owned[o] != null) Destroy(inst.Owned[o]);
                }
            }
            _mainWorldNpcs.Clear();
            _nearestMainWorldNpcIndex = -1;
        }

        private void StepMainWorldNpcs(float3 player)
        {
            _nearestMainWorldNpcIndex = -1;
            if (!_setup.IsLobbyPlanet || _mainWorldNpcs.Count == 0 ||
                (_team != null && _team.InCombat) || _encounter.HasActiveTrainerBattle || _activeEncounterTrainerId >= 0)
            {
                return;
            }

            float3 playerDir = _rig.Config.ToDirection(player);
            float radius = _rig.Config.Radius;
            float bestDist = MainWorldNpcRules.InteractRadiusM;
            for (int i = 0; i < _mainWorldNpcs.Count; i++)
            {
                SettlementNpcPlacement p = _mainWorldNpcs[i].Placement;
                if (!_discoveredSettlements.Contains(p.SettlementIndex))
                {
                    continue;
                }

                float dist = math.distance(playerDir, p.Direction) * radius;
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    _nearestMainWorldNpcIndex = i;
                }
            }
        }

        private bool TryMainWorldNpcInteract()
        {
            if (_nearestMainWorldNpcIndex < 0 || _nearestMainWorldNpcIndex >= _mainWorldNpcs.Count)
            {
                return false;
            }

            _npcFeedback = string.Empty;
            OpenMainWorldNpcOverlay(_mainWorldNpcs[_nearestMainWorldNpcIndex].Placement);
            return true;
        }

        private string NpcRoleTitle(SettlementNpcRole role) =>
            _copy.Ui("ui.npc." + MainWorldNpcRules.RoleKeyOf(role));

        private void OpenServiceOverlay()
        {
            _serviceOverlay ??= new GameOverlayView(transform);
            if (!_serviceOverlayOpen)
            {
                _previousTimeScale = Time.timeScale;
                _previousPlayerInput = _rig != null && _rig.Input != null && _rig.Input.enabled;
                _previousCursorLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                if (_rig?.Input != null) _rig.Input.enabled = false;
                _rig?.Character?.SetInput(Vector2.zero, _rig.Character.Facing, false, false);
                _rollRequested = false;
                _overloadRequested = false;
                _runInput?.Disable();
                _menuInput?.Enable();
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _serviceOverlayOpen = true;
            }
        }

        private void CloseServiceOverlay()
        {
            if (!_serviceOverlayOpen)
            {
                return;
            }

            _serviceOverlayOpen = false;
            _serviceOverlay?.Hide();
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _menuInput?.Disable();
            if (_rig?.Input != null) _rig.Input.enabled = _previousPlayerInput;
            if (isActiveAndEnabled && !_ended) _runInput?.Enable();
        }

        private void OpenMainWorldNpcOverlay(SettlementNpcPlacement placement)
        {
            OpenServiceOverlay();
            string title = $"{NpcRoleTitle(placement.Role)}（聚落 #{placement.SettlementIndex + 1}）";
            switch (placement.Role)
            {
                case SettlementNpcRole.Merchant:
                    BuildMerchantOverlay(title, placement);
                    break;
                case SettlementNpcRole.Craftsman:
                    BuildCraftsmanOverlay(title, placement);
                    break;
                case SettlementNpcRole.Hatchery:
                    BuildHatcheryOverlay(title, placement);
                    break;
                case SettlementNpcRole.VesselConverter:
                    BuildVesselConverterOverlay(title, placement);
                    break;
                case SettlementNpcRole.QuestGuide:
                    BuildQuestGuideOverlay(title);
                    break;
                case SettlementNpcRole.Healer:
                    BuildHealerOverlay(title, placement);
                    break;
            }
        }

        private void SyncBagIntoCandidate(LobbySave candidate)
        {
            candidate.CraftStock.CopyFrom(_craftInventory);
        }

        private bool CommitNpcSave(LobbySave candidate, out string error)
        {
            if (_setup.Services == null)
            {
                error = "服務尚未就緒";
                return false;
            }

            if (!_setup.Services.CommitSave(candidate, out error))
            {
                return false;
            }

            _craftInventory.CopyFrom(candidate.CraftStock);
            return true;
        }

        private void BuildMerchantOverlay(string title, SettlementNpcPlacement placement)
        {
            LobbySave save = _setup.Services?.Save;
            if (save == null)
            {
                _serviceOverlay.Show(title, "存檔尚未載入");
                _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
                return;
            }

            string subtitle = string.IsNullOrEmpty(_npcFeedback)
                ? $"持有金幣：{save.Gold} G"
                : $"{_npcFeedback}\n持有金幣：{save.Gold} G";
            _serviceOverlay.Show(title, subtitle);

            bool moonUnlocked = ShopAndRepairRules.CanAccessTier1EquipmentShop(
                StarSelectRules.MaxUnlockedSize(save.TutorialDone, save.CompletedTrialsSnapshot().Count) >= PlanetSize.Moon ||
                save.FoodQuestDone);

            if (!moonUnlocked)
            {
                _serviceOverlay.AddCard("1★ 武器裝備商店", "完成 2 號部落「食物系列主線」解鎖矮行星後開放購買。");
            }
            else
            {
                foreach (CraftItem item in _craftCatalog.Items)
                {
                    if (item.Equipment == null) continue;
                    bool isTwoHanded = item.Equipment.Kind == EquipmentKind.TwoHanded;
                    int price = ShopAndRepairRules.Tier1EquipmentGoldPrice(isTwoHanded);
                    string itemId = item.Id;
                    _serviceOverlay.AddActionCard(
                        _copy.Ui(itemId),
                        $"類型：{_copy.Ui("ui.craft.kind." + item.Equipment.Kind)} · 價格：{price} G",
                        ($"購買 ({price} G)", () =>
                        {
                            LobbySave candidate = _setup.Services.Save.Clone();
                            SyncBagIntoCandidate(candidate);
                            if (ShopAndRepairRules.TryBuyTier1Equipment(candidate.CraftStock, itemId, _setup.Services.Save.LastPlanet,
                                    price, candidate.Gold, out int rem) &&
                                candidate.TrySpendGold(candidate.Gold - rem) &&
                                CommitNpcSave(candidate, out string err))
                            {
                                _npcFeedback = $"已購買 {_copy.Ui(itemId)}！";
                            }
                            else
                            {
                                _npcFeedback = "購買失敗（金幣不足或背包已滿）";
                            }
                            OpenMainWorldNpcOverlay(placement);
                        }, save.Gold >= price, true));
                }
            }

            bool anySellable = false;
            foreach (CraftStack stack in _craftInventory.Snapshot())
            {
                if (!_craftCatalog.TryGetItem(stack.Id, out CraftItem item) || item.Equipment != null) continue;
                anySellable = true;
                int unitPrice = ShopAndRepairRules.MaterialSellPrice(CraftMaterialTier.Tier1, item.Category == "processed");
                string stackId = stack.Id;
                _serviceOverlay.AddActionCard(
                    $"{_copy.Ui(stackId)} ×{stack.Quantity}",
                    $"單價：{unitPrice} G",
                    ($"出售 ×1 (+{unitPrice} G)", () =>
                    {
                        LobbySave candidate = _setup.Services.Save.Clone();
                        SyncBagIntoCandidate(candidate);
                        if (ShopAndRepairRules.TrySellMaterial(candidate.CraftStock, stackId, 1, unitPrice, candidate.Gold, out int newGold))
                        {
                            candidate.AddGold(newGold - candidate.Gold);
                            if (CommitNpcSave(candidate, out _))
                            {
                                _npcFeedback = $"已出售 {_copy.Ui(stackId)} ×1 (+{unitPrice} G)";
                            }
                        }
                        OpenMainWorldNpcOverlay(placement);
                    }, stack.Quantity >= 1, false));
            }

            if (!anySellable)
            {
                _serviceOverlay.AddCard("出售素材", "目前背包內沒有可出售的素材。");
            }

            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void BuildCraftsmanOverlay(string title, SettlementNpcPlacement placement)
        {
            LobbySave save = _setup.Services?.Save;
            if (save == null)
            {
                _serviceOverlay.Show(title, "存檔尚未載入");
                _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
                return;
            }

            string sub = string.IsNullOrEmpty(_npcFeedback) ? "使用隊伍生活技能與背包材料進行製作" : _npcFeedback;
            _serviceOverlay.Show(title, sub, _craftShowLocked ? "隱藏未解鎖配方" : "顯示未解鎖配方", () =>
            {
                _craftShowLocked = !_craftShowLocked;
                OpenMainWorldNpcOverlay(placement);
            });

            bool any = false;
            foreach (CraftRecipe recipe in _craftCatalog.Recipes)
            {
                CraftResult preview = CraftRules.CanCraft(recipe, _utilities, _craftInventory);
                if (preview == CraftResult.MissingSkill && !_craftShowLocked) continue;
                any = true;
                var inputsText = new System.Text.StringBuilder();
                for (int i = 0; i < recipe.Costs.Count; i++)
                {
                    if (i > 0) inputsText.Append(" + ");
                    CraftStack st = recipe.Costs[i];
                    inputsText.Append(_copy.Ui(st.Id)).Append("×").Append(st.Quantity)
                        .Append(" (持有 ").Append(_craftInventory.Count(st.Id)).Append(')');
                }
                CraftStack primaryGain = recipe.Gains[0];
                CraftRecipe captured = recipe;
                _serviceOverlay.AddActionCard(
                    $"{_copy.Ui(primaryGain.Id)} ×{primaryGain.Quantity}",
                    $"材料：{inputsText}\n狀態：{_copy.Ui("craft.result." + preview)}",
                    ("製作", () =>
                    {
                        LobbySave candidate = _setup.Services.Save.Clone();
                        SyncBagIntoCandidate(candidate);
                        CraftResult res = CraftRules.TryCraft(captured, _utilities, candidate.CraftStock, _craftCatalog,
                            _setup.Services.Save.LastPlanet);
                        if (res == CraftResult.Success && CommitNpcSave(candidate, out string err))
                        {
                            CraftStack craftedGain = captured.Gains[0];
                            _npcFeedback = $"製作完成：{_copy.Ui(craftedGain.Id)} ×{craftedGain.Quantity}";
                        }
                        else
                        {
                            _npcFeedback = _copy.Ui("craft.result." + res);
                        }
                        OpenMainWorldNpcOverlay(placement);
                    }, preview == CraftResult.Success, true));
            }

            if (!any)
            {
                _serviceOverlay.AddCard("製作配方", "目前隊伍生活技能尚未解鎖任何可用配方。");
            }

            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void BuildHatcheryOverlay(string title, SettlementNpcPlacement placement)
        {
            LobbySave save = _setup.Services?.Save;
            if (save == null)
            {
                _serviceOverlay.Show(title, "存檔尚未載入");
                _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
                return;
            }

            string sub = string.IsNullOrEmpty(_npcFeedback)
                ? $"孵化槽位：{save.Eggs.Count}/{HatchConfig.DefaultCapacity}（透過探索步行、戰鬥與 BOSS 戰累積進度）"
                : _npcFeedback;
            _serviceOverlay.Show(title, sub);

            if (save.Eggs.Count == 0)
            {
                _serviceOverlay.AddCard("孵蛋所空置中", "目前沒有正在孵化的宙獸蛋；探索星球並擊倒 BOSS 有機會取得新蛋。");
            }
            else
            {
                for (int i = 0; i < save.Eggs.Count; i++)
                {
                    IncubatingEgg egg = save.Eggs[i];
                    int eggIndex = i;
                    int pct = (int)(egg.ProgressPpm * 100L / HatchConfig.FullProgressPpm);
                    VesselKind eggVessel = Enum.IsDefined(typeof(VesselKind), egg.Vessel)
                        ? (VesselKind)egg.Vessel : VesselKind.GlassBead;
                    string vesselName = _copy.Ui("ui.vessel." + VesselKey(eggVessel));
                    _serviceOverlay.AddActionCard(
                        $"宙獸蛋 #{i + 1} ({egg.EggId})",
                        $"親代器皿：{vesselName} · 孵化進度：{pct}%",
                        (egg.IsReady ? "孵化！" : "尚未完成", () =>
                        {
                            HatchEggAt(eggIndex);
                            OpenMainWorldNpcOverlay(placement);
                        }, egg.IsReady, true));
                }
            }

            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void HatchEggAt(int eggIndex)
        {
            LobbySave candidate = _setup.Services.Save.Clone();
            if (eggIndex < 0 || eggIndex >= candidate.Eggs.Count || !candidate.Eggs[eggIndex].IsReady)
            {
                return;
            }

            IncubatingEgg egg = candidate.Eggs[eggIndex];
            string speciesId = ResolveHatchedSpeciesId(egg.EggId, eggIndex);
            if (string.IsNullOrEmpty(speciesId))
            {
                _npcFeedback = "找不到可孵化的幼年物種資料";
                return;
            }

            var remaining = new List<IncubatingEgg>(candidate.Eggs);
            remaining.RemoveAt(eggIndex);
            candidate.SetEggs(remaining);
            int exp = _progression.XpThreshold(_progression.StarterLevel);
            VesselKind eggVessel = Enum.IsDefined(typeof(VesselKind), egg.Vessel)
                ? (VesselKind)egg.Vessel : VesselKind.GlassBead;
            candidate.Add(speciesId, eggVessel, exp);
            if (CommitNpcSave(candidate, out string err))
            {
                _npcFeedback = $"恭喜！孵化出了 {_copy.Name(speciesId)}！";
            }
            else
            {
                _npcFeedback = err;
            }
        }

        private string ResolveHatchedSpeciesId(string eggId, int index)
        {
            if (_wildPool != null && _wildPool.Count > 0)
            {
                var rng = new Pcg32(_setup.Seed ^ unchecked((ulong)(_nowMs + index * 97)), 'E');
                for (int attempt = 0; attempt < _wildPool.Count; attempt++)
                {
                    CreatureDef candidate = _wildPool[(int)rng.NextUInt((uint)_wildPool.Count)];
                    if (candidate.Stage == CreatureStage.Juvenile)
                    {
                        return candidate.Id;
                    }
                }
                return _wildPool[0].Id;
            }
            return string.Empty;
        }

        private void BuildVesselConverterOverlay(string title, SettlementNpcPlacement placement)
        {
            LobbySave save = _setup.Services?.Save;
            if (save == null)
            {
                _serviceOverlay.Show(title, "存檔尚未載入");
                _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
                return;
            }

            bool unlocked = MainWorldNpcRules.CanConvertVessel(
                StarSelectRules.MaxUnlockedSize(save.TutorialDone, save.CompletedTrialsSnapshot().Count) >= PlanetSize.Moon ||
                save.FoodQuestDone);

            string sub = string.IsNullOrEmpty(_npcFeedback)
                ? "轉換器皿僅改變作為副手時提供的功能（外觀維持原始收服樣式）"
                : _npcFeedback;
            _serviceOverlay.Show(title, sub);

            if (!unlocked)
            {
                _serviceOverlay.AddCard("尚未解鎖", "需完成 2 號部落「食物系列主線」解鎖矮行星後，方可進行機能器皿轉換。");
                _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
                return;
            }

            foreach (OwnedCreature c in save.Owned)
            {
                Element specialty = Element.Wood;
                if (_setup.Database != null && _setup.Database.TryGet(c.SpeciesId, out CreatureDef def) && def.Mastery.Count > 0)
                {
                    specialty = def.Mastery[0].Element;
                }

                MaterialStack cost = MainWorldNpcRules.VesselConvertCost(specialty);
                int held = save.Materials.Count(cost.Key);
                bool canPay = held >= cost.Qty;

                var buttons = new List<(string, Action, bool, bool)>(2);
                foreach (VesselKind targetKind in Enum.GetValues(typeof(VesselKind)))
                {
                    if (targetKind == c.Vessel) continue;
                    int creatureId = c.InstanceId;
                    VesselKind nextVessel = targetKind;
                    buttons.Add((
                        $"轉為 {_copy.Ui("ui.vessel." + VesselKey(nextVessel))}",
                        () =>
                        {
                            LobbySave candidate = _setup.Services.Save.Clone();
                            if (candidate.Materials.TryConsume(new[] { cost }))
                            {
                                candidate.SetVessel(creatureId, nextVessel);
                                if (CommitNpcSave(candidate, out string err))
                                {
                                    _npcFeedback = $"已將 #{creatureId} {_copy.Name(c.SpeciesId)} 轉換為 {_copy.Ui("ui.vessel." + VesselKey(nextVessel))}！";
                                }
                                else
                                {
                                    _npcFeedback = err;
                                }
                            }
                            OpenMainWorldNpcOverlay(placement);
                        },
                        canPay,
                        false));
                }

                _serviceOverlay.AddActionCard(
                    $"#{c.InstanceId} {_copy.Name(c.SpeciesId)} LV{_progression.LevelOf(c.Exp)}",
                    $"目前功能器皿：{_copy.Ui("ui.vessel." + VesselKey(c.Vessel))}\n轉換消耗：{_copy.Material(cost.Key)} ×{cost.Qty}（持有 {held}）",
                    buttons.ToArray());
            }

            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void BuildQuestGuideOverlay(string title)
        {
            LobbySave save = _setup.Services?.Save;
            int trials = save?.CompletedTrialsSnapshot().Count ?? 0;
            _serviceOverlay.Show(title, $"主世界主線進度 · 已完成地形試煉：{trials} / 10");
            List<MainStoryTrack> tracks = BeginnerQuestRules.NextMainStoryTracks(
                save?.Codex, save?.CompletedTrialsSnapshot());
            for (int i = 0; i < tracks.Count; i++)
            {
                MainStoryTrack t = tracks[i];
                _serviceOverlay.AddCard(t.Title, t.Summary);
            }
            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void BuildHealerOverlay(string title, SettlementNpcPlacement placement)
        {
            string sub = string.IsNullOrEmpty(_npcFeedback)
                ? "治療玩家與出戰宙獸體力，或休息一夜以刷新主世界星隙結晶。"
                : _npcFeedback;
            _serviceOverlay.Show(title, sub);
            _serviceOverlay.AddActionCard(
                "治療與休息站",
                "回復全員生命值並解除異常狀態；休息會推動天體日夜交替並重新生成各地星隙。",
                ("治療全隊", () =>
                {
                    RestoreAllPartyVitals();
                    _npcFeedback = "全隊與角色體力已完全回復！";
                    OpenMainWorldNpcOverlay(placement);
                }, true, true),
                ("休息並刷新星隙", () =>
                {
                    RestoreAllPartyVitals();
                    RefreshStarRifts();
                    CloseServiceOverlay();
                    Toast("休息完畢！主世界已刷新一輪新的星隙結晶。");
                }, true, false));
            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, true);
        }

        private void RestoreAllPartyVitals() => RestoreBeginnerParty();
    }
}
