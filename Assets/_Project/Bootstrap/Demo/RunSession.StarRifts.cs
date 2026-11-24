using System;
using System.Collections.Generic;
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
        private readonly List<StarRiftEntry> _starRifts = new List<StarRiftEntry>();
        private readonly List<GameObject> _starRiftObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _starRiftOwned = new List<UnityEngine.Object>();
        private int _nearestStarRiftIndex = -1;
        private int _riftRefreshEpoch;
        private int _lastObservedRiftDay = -1;
        private string _customSeedInput = "20261124";
        private int _customPlanetTypeIndex;
        private string _riftError = string.Empty;

        internal int StarRiftCountForTests => _starRifts.Count;

        internal StarRiftEntry GetStarRiftForTests(int index) => _starRifts[index];

        internal bool OpenStarRiftForTests(int index)
        {
            if (index < 0 || index >= _starRifts.Count || _starRifts[index].Cleared)
            {
                return false;
            }

            _riftError = string.Empty;
            OpenStarRiftOverlay(_starRifts[index]);
            return true;
        }

        private int CurrentWorldDayIndex() => (int)(_nowMs / SolarClock.DayLengthMs);

        private void ConfigureStarRifts(Shader toonShader)
        {
            _riftRefreshEpoch = _setup.RiftRefreshEpoch;
            _lastObservedRiftDay = CurrentWorldDayIndex();
            RebuildStarRifts(toonShader);
        }

        private void RefreshStarRifts()
        {
            _riftRefreshEpoch++;
            RebuildStarRifts(_toonShader);
        }

        private void RebuildStarRifts(Shader toonShader)
        {
            DisposeStarRifts();
            if (!_setup.IsLobbyPlanet || _layout == null)
            {
                return;
            }

            List<PlanetTypeDef> types = _setup.Services != null
                ? _setup.Services.SelectablePlanetTypes()
                : BuildFallbackSelectableTypes();
            if (types.Count == 0)
            {
                return;
            }

            LobbySave save = _setup.Services?.Save;
            PlanetSize maxUnlocked = ResolveUnlockedPlanetSize(save);
            CreatureStage storyCap = StarSelectRules.MaxBossStageForSize(maxUnlocked);
            CreatureStage levelCap = _progression.MaxBossStage(_progression.PlayerLevelOf(_playerExp));
            CreatureStage maxBossStage = (int)storyCap < (int)levelCap ? storyCap : levelCap;

            var avoidDirs = new List<float3>(_layout.Settlements.Count);
            for (int i = 0; i < _layout.Settlements.Count; i++)
            {
                avoidDirs.Add(_layout.Settlements[i].Center);
            }

            List<StarRiftEntry> generated = StarRiftRules.GenerateRifts(
                LobbyPlanet.Seed,
                _riftRefreshEpoch,
                maxUnlocked,
                maxBossStage,
                types,
                avoidDirs,
                _rig.Config.Radius);

            for (int i = 0; i < generated.Count; i++)
            {
                StarRiftEntry entry = generated[i];
                bool cleared = _riftRefreshEpoch == _setup.RiftRefreshEpoch &&
                               _setup.ClearedRiftIndices != null &&
                               ContainsInt(_setup.ClearedRiftIndices, entry.RiftIndex);
                entry = entry.WithCleared(cleared);
                _starRifts.Add(entry);
                if (!entry.Cleared && toonShader != null)
                {
                    BuildStarRiftVisual(entry, toonShader);
                }
            }
        }

        private static bool ContainsInt(IReadOnlyCollection<int> set, int target)
        {
            foreach (int v in set)
            {
                if (v == target) return true;
            }
            return false;
        }

        private PlanetSize ResolveUnlockedPlanetSize(LobbySave save)
        {
            if (save == null)
            {
                return StarSelectRules.MaxUnlockedSize(_setup.TutorialDone, _setup.TrialBadges?.Count ?? 0);
            }

            int trials = save.CompletedTrialsSnapshot().Count;
            PlanetSize byTrials = StarSelectRules.MaxUnlockedSize(save.TutorialDone, trials);
            PlanetSize byStory = StarSelectRules.MaxUnlockedSize(
                save.TutorialDone, save.FoodQuestDone, trials, save.MigratoryReturnDone, save.RivalQuestDone);
            return (int)byStory > (int)byTrials ? byStory : byTrials;
        }

        private List<PlanetTypeDef> BuildFallbackSelectableTypes()
        {
            var list = new List<PlanetTypeDef>();
            if (_setup.Database != null)
            {
                foreach (GameDef def in _setup.Database.All)
                {
                    if (def is PlanetTypeDef t && t.Id != "cradle_planet")
                    {
                        list.Add(t);
                    }
                }
            }

            if (list.Count == 0 && _setup.PlanetType != null && _setup.PlanetType.Id != "cradle_planet")
            {
                list.Add(_setup.PlanetType);
            }

            return list;
        }

        private void BuildStarRiftVisual(StarRiftEntry entry, Shader toonShader)
        {
            PlanetFrame frame = PlanetFrame.FromUp(entry.Direction);
            Quaternion orientation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up);
            float groundAlt = _surface.GroundAltitude(entry.Direction);
            float3 baseWorld = _rig.Config.ToWorld(entry.Direction, groundAlt);

            var root = new GameObject($"StarRift_{entry.RiftIndex}_{entry.MainElement}");
            root.transform.SetPositionAndRotation(new Vector3(baseWorld.x, baseWorld.y, baseWorld.z), orientation);

            Color elementColor = StarRiftElementColor(entry.MainElement);
            Material crystalMat = PlanetLook.CreatureMaterial(toonShader, elementColor, 0.02f);
            Material pillarMat = PlanetLook.CreatureMaterial(toonShader,
                new Color(elementColor.r, elementColor.g, elementColor.b, 0.85f), 0f);

            // Crystal base
            GameObject crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(crystal.GetComponent<Collider>());
            crystal.transform.SetParent(root.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            crystal.transform.localRotation = Quaternion.Euler(35f, 45f, 35f);
            crystal.transform.localScale = new Vector3(1.5f, 2.2f, 1.5f);
            crystal.GetComponent<Renderer>().sharedMaterial = crystalMat;

            // High-visibility Tera-raid style beam aligned with local sphere Up
            int stars = StarRiftRules.StarRating(entry.Size);
            float beamHeight = 16f + 6f * stars;
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = new Vector3(0f, beamHeight * 0.5f, 0f);
            beam.transform.localScale = new Vector3(0.45f, beamHeight * 0.5f, 0.45f);
            beam.GetComponent<Renderer>().sharedMaterial = pillarMat;

            _starRiftOwned.Add(crystalMat);
            _starRiftOwned.Add(pillarMat);
            _starRiftObjects.Add(root);
        }

        private static Color StarRiftElementColor(Element e) => e switch
        {
            Element.Metal => new Color(0.75f, 0.78f, 0.85f),
            Element.Wood => new Color(0.24f, 0.78f, 0.36f),
            Element.Water => new Color(0.22f, 0.58f, 0.94f),
            Element.Fire => new Color(0.95f, 0.34f, 0.20f),
            Element.Earth => new Color(0.74f, 0.56f, 0.30f),
            Element.Wind => new Color(0.42f, 0.90f, 0.78f),
            Element.Ice => new Color(0.62f, 0.88f, 0.98f),
            Element.Thunder => new Color(0.96f, 0.84f, 0.20f),
            Element.Light => new Color(0.98f, 0.94f, 0.64f),
            Element.Shadow => new Color(0.56f, 0.28f, 0.82f),
            _ => Color.white,
        };

        private void DisposeStarRifts()
        {
            for (int i = 0; i < _starRiftObjects.Count; i++)
            {
                if (_starRiftObjects[i] != null) Destroy(_starRiftObjects[i]);
            }
            _starRiftObjects.Clear();

            for (int i = 0; i < _starRiftOwned.Count; i++)
            {
                if (_starRiftOwned[i] != null) Destroy(_starRiftOwned[i]);
            }
            _starRiftOwned.Clear();
            _starRifts.Clear();
            _nearestStarRiftIndex = -1;
        }

        private void StepStarRifts(float3 player)
        {
            _nearestStarRiftIndex = -1;
            if (!_setup.IsLobbyPlanet || _starRifts.Count == 0 ||
                (_team != null && _team.InCombat) || _encounter.HasActiveTrainerBattle || _activeEncounterTrainerId >= 0)
            {
                return;
            }

            int day = CurrentWorldDayIndex();
            if (_lastObservedRiftDay >= 0 && day != _lastObservedRiftDay)
            {
                _lastObservedRiftDay = day;
                RefreshStarRifts();
                Toast("主世界經過一天，星隙結晶已刷新！");
                return;
            }

            float3 playerDir = _rig.Config.ToDirection(player);
            float radius = _rig.Config.Radius;
            float bestDist = StarRiftRules.InteractRadiusM;
            for (int i = 0; i < _starRifts.Count; i++)
            {
                if (_starRifts[i].Cleared) continue;
                float dist = math.distance(playerDir, _starRifts[i].Direction) * radius;
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    _nearestStarRiftIndex = i;
                }
            }
        }

        private bool TryStarRiftInteract()
        {
            if (_nearestStarRiftIndex < 0 || _nearestStarRiftIndex >= _starRifts.Count)
            {
                return false;
            }

            _riftError = string.Empty;
            OpenStarRiftOverlay(_starRifts[_nearestStarRiftIndex]);
            return true;
        }

        private void OpenStarRiftOverlay(StarRiftEntry rift)
        {
            OpenServiceOverlay();
            LobbySave save = _setup.Services?.Save;
            int stars = StarRiftRules.StarRating(rift.Size);
            string starsStr = new string('★', stars);
            string sizeLabel = _copy.Ui("ui.star_select.size_" + rift.Size.ToString().ToLowerInvariant());
            string bossLabel = _copy.Ui("ui.stage." + rift.BossStage.ToString().ToLowerInvariant());
            string planetName = _copy.PlanetName(rift.PlanetTypeId, rift.Seed);
            MaterialStack cost = StarSelectRules.LaunchCost(rift.Size, rift.MainElement);
            int held = save?.Materials.Count(cost.Key) ?? 0;

            string subtitle = string.IsNullOrEmpty(_riftError)
                ? $"主世界星隙刷新輪次 #{_riftRefreshEpoch + 1}"
                : _riftError;
            _serviceOverlay.Show($"{_copy.Ui("ui.star_rift.title")} ({starsStr})", subtitle);

            var elemDesc = new System.Text.StringBuilder();
            for (int i = 0; i < rift.Elements.Shares.Count; i++)
            {
                if (i > 0) elemDesc.Append(" / ");
                PlanetElementShare sh = rift.Elements.Shares[i];
                elemDesc.Append(_copy.Element(sh.Element.ToString())).Append(' ').Append(sh.Percent).Append('%');
            }

            _serviceOverlay.AddActionCard(
                $"{starsStr} {planetName}（{_copy.Planet(rift.PlanetTypeId)}）",
                $"規模：{sizeLabel} · BOSS 階段：{bossLabel}\n" +
                $"屬性分佈：{elemDesc}\n" +
                $"所需元素素材：{_copy.Material(cost.Key)} ×{cost.Qty}（持有：{held}）\n" +
                $"Seed：{rift.Seed}",
                (_copy.Ui("ui.star_rift.launch"), () =>
                {
                    PlanetTypeDef planetType = _setup.Services?.FindPlanetType(rift.PlanetTypeId);
                    if (planetType == null || _setup.Services == null)
                    {
                        _riftError = "無法解析目標星球類型";
                        OpenStarRiftOverlay(rift);
                        return;
                    }

                    if (!_setup.Services.TryPayForStarRift(rift, planetType, out string err))
                    {
                        _riftError = err;
                        OpenStarRiftOverlay(rift);
                        return;
                    }

                    _outcome.PendingStarRiftLaunch = rift;
                    _outcome.PendingStarRiftPlanetType = planetType;
                    _outcome.NewRiftRefreshEpoch = _riftRefreshEpoch;
                    CloseServiceOverlay();
                    End(true, false);
                }, held >= cost.Qty, true));

            List<PlanetTypeDef> selectable = _setup.Services != null
                ? _setup.Services.SelectablePlanetTypes()
                : BuildFallbackSelectableTypes();
            if (selectable.Count > 0)
            {
                _customPlanetTypeIndex = Mathf.Clamp(_customPlanetTypeIndex, 0, selectable.Count - 1);
                PlanetTypeDef customType = selectable[_customPlanetTypeIndex];
                _serviceOverlay.AddSeedInputCard(
                    _copy.Ui("ui.star_rift.custom_seed"),
                    $"輸入自訂 Seed 並選擇星球類型（目前：{_copy.Planet(customType.Id)}）；出發仍依實際尺寸與主屬性消耗元素素材。",
                    _customSeedInput,
                    val => _customSeedInput = val,
                    $"切換類型：{_copy.Planet(customType.Id)}",
                    () =>
                    {
                        _customPlanetTypeIndex = (_customPlanetTypeIndex + 1) % selectable.Count;
                        OpenStarRiftOverlay(rift);
                    },
                    "以此 Seed 啟動星隙",
                    () =>
                    {
                        if (!ulong.TryParse((_customSeedInput ?? string.Empty).Trim(), out ulong parsedSeed) || parsedSeed == 0UL)
                        {
                            _riftError = "請輸入大於 0 的有效整數 Seed。";
                            OpenStarRiftOverlay(rift);
                            return;
                        }

                        PlanetSize maxUnlocked = ResolveUnlockedPlanetSize(save);
                        CreatureStage storyCap = StarSelectRules.MaxBossStageForSize(maxUnlocked);
                        CreatureStage levelCap = _progression.MaxBossStage(_progression.PlayerLevelOf(_playerExp));
                        CreatureStage maxBossStage = (int)storyCap < (int)levelCap ? storyCap : levelCap;

                        if (!StarRiftRules.TryBuildCustomSeedRift(parsedSeed, customType, maxUnlocked, maxBossStage,
                                out StarRiftEntry customRift))
                        {
                            _riftError = "此 Seed 或星球類型無效。";
                            OpenStarRiftOverlay(rift);
                            return;
                        }

                        if (_setup.Services == null)
                        {
                            _riftError = "服務尚未就緒";
                            OpenStarRiftOverlay(rift);
                            return;
                        }

                        if (!_setup.Services.TryPayForStarRift(customRift, customType, out string err))
                        {
                            _riftError = err;
                            OpenStarRiftOverlay(rift);
                            return;
                        }

                        _outcome.PendingStarRiftLaunch = customRift;
                        _outcome.PendingStarRiftPlanetType = customType;
                        _outcome.NewRiftRefreshEpoch = _riftRefreshEpoch;
                        CloseServiceOverlay();
                        End(true, false);
                    });
            }

            _serviceOverlay.AddFooterButton(_copy.Ui("ui.journal.close"), CloseServiceOverlay, false);
        }
    }
}
