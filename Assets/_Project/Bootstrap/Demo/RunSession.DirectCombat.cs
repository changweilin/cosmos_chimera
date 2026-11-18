using System;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Demo
{
    public sealed partial class RunSession
    {
        private const float CharacterHeightM = 1.7f;
        private const float CharacterJumpM = 1.2f;
        private const float CreatureJumpGravity = 22f;
        private const float CharacterFollowDistanceM = 2.6f;

        private bool IsDirectControlling =>
            _team != null && _team.InCombat && !_returnedToPlayer &&
            _mainId >= 0 && _mainId < _encounter.Units.Count && _encounter.Units[_mainId].IsActive;

        private int CurrentDirectTargetId() =>
            _lockedObjectId >= 0 ? Encounter.ObjectTargetId
            : _lockedTarget >= 0 ? _lockedTarget
            : CombatAi.NoTarget;

        private void UpdateDirectControlState()
        {
            bool direct = IsDirectControlling;
            _encounter.SetDirectControl(direct);
            if (_rig != null && _rig.Input != null)
            {
                _rig.Input.SuppressCharacterInput = direct;
            }

            if (direct)
            {
                _combatInput?.Enable();
                if (_rig != null && _rig.CameraRig != null && _views.TryGetValue(_mainId, out ICreatureView view))
                {
                    float eyeHeight = math.max(0.6f, _encounter.Units[_mainId].BodyRadiusM * 1.7f);
                    _rig.CameraRig.SetTarget(view.Root, eyeHeight);
                }
            }
            else
            {
                _combatInput?.Disable();
                if (_mainId >= 0 && _mainId < _encounter.Units.Count)
                {
                    _encounter.SetBlockHeld(_mainId, false, _nowMs);
                }

                if (_rig != null && _rig.CameraRig != null && _rig.Character != null)
                {
                    _rig.CameraRig.SetTarget(_rig.Character.transform);
                }
            }
        }

        private float3 ComputeDirectMoveTangent(float3 up)
        {
            if (_rig == null || _rig.Input == null || _rig.CameraRig == null)
            {
                return float3.zero;
            }

            Vector2 move = _rig.Input.MoveInput;
            if (move.sqrMagnitude <= 1e-4f)
            {
                return float3.zero;
            }

            float3 heading = _rig.CameraRig.Heading;
            heading = heading - up * math.dot(up, heading);
            if (math.lengthsq(heading) < 1e-6f)
            {
                return float3.zero;
            }

            heading = math.normalize(heading);
            float3 right = math.normalize(math.cross(up, heading));
            float3 combined = heading * move.y + right * move.x;
            float len = math.length(combined);
            return len > 1f ? combined / len : combined;
        }

        private void UpdateCharacterAutoFollow(float3 playerPos, float3 mainPos)
        {
            if (_rig == null || _rig.Character == null || _rig.CameraRig == null)
            {
                return;
            }

            float3 delta = mainPos - playerPos;
            float3 up = _rig.Config.ToDirection(playerPos);
            float3 tangent = delta - up * math.dot(up, delta);
            float dist = math.length(tangent);
            if (dist <= CharacterFollowDistanceM)
            {
                _rig.Character.SetInput(Vector2.zero, _rig.CameraRig.Heading, false, false);
                return;
            }

            float3 dir = tangent / dist;
            float3 heading = _rig.CameraRig.Heading;
            heading = math.normalize(heading - up * math.dot(up, heading));
            float3 right = math.normalize(math.cross(up, heading));
            Vector2 move2D = new Vector2(math.dot(dir, right), math.dot(dir, heading));
            _rig.Character.SetInput(move2D, heading, dist > CharacterFollowDistanceM * 2f, false);
        }

        private void StepMainJump(float dt)
        {
            if (_rig != null && _rig.Input != null && _rig.Input.JumpPressedThisFrame &&
                _mainJumpOffsetM <= 0.001f && _mainId >= 0 && _mainId < _encounter.Units.Count)
            {
                // REQ: combat.md#完美閃避 (Q-C58) — creature jump height scales with height / 1.7 m.
                float heightM = _encounter.Units[_mainId].BodyRadiusM * 2f;
                float jumpM = CharacterJumpM * (heightM / CharacterHeightM);
                _mainJumpVelMs = math.sqrt(2f * CreatureJumpGravity * math.max(0.2f, jumpM));
            }

            if (_mainJumpOffsetM > 0f || _mainJumpVelMs > 0f)
            {
                _mainJumpOffsetM += _mainJumpVelMs * dt;
                _mainJumpVelMs -= CreatureJumpGravity * dt;
                if (_mainJumpOffsetM <= 0f)
                {
                    _mainJumpOffsetM = 0f;
                    _mainJumpVelMs = 0f;
                }
            }
        }

        private void ReadDirectCombatInput()
        {
            if (_quickWheelOpen)
            {
                ReadQuickWheelInput();
            }

            if (_selectFirstAction.WasPressedThisFrame() && _team.Active.Count > 0)
            {
                _mainId = _team.Active[0];
                UpdateDirectControlState();
            }

            if (_selectSecondAction.WasPressedThisFrame() && _team.Active.Count > 1)
            {
                _mainId = _team.Active[1];
                UpdateDirectControlState();
            }

            if (_cycleTargetAction.WasPressedThisFrame())
            {
                CycleTarget();
            }

            if (_rollAction.WasPressedThisFrame())
            {
                _rollRequested = true;
            }

            if (_overloadAction.WasPressedThisFrame())
            {
                _overloadRequested = true;
            }

            if (_abandonAction.WasPressedThisFrame())
            {
                RequestAbandon();
            }

            bool blockHeld = _blockAction != null && _blockAction.IsPressed() &&
                (!(_blockAction.activeControl?.device is Gamepad) || !_commandModifierAction.IsPressed());
            _encounter.SetBlockHeld(_mainId, blockHeld, _nowMs);

            if (!_quickWheelOpen && _castActions != null)
            {
                int targetId = CurrentDirectTargetId();
                if (PressedWithoutCommand(_castActions[0]))
                {
                    _encounter.TryDirectCast(_mainId, 0, targetId, _nowMs);
                }

                for (int s = 1; s < _castActions.Length; s++)
                {
                    if (_castActions[s].WasPressedThisFrame())
                    {
                        _encounter.TryDirectCast(_mainId, s, targetId, _nowMs);
                    }
                }
            }

            if (_supportModeAction != null && _supportModeAction.WasPressedThisFrame())
            {
                CycleSupportMode();
            }

            if (_quickWheelAction != null && _quickWheelAction.WasPressedThisFrame())
            {
                _quickWheelOpen = !_quickWheelOpen;
                _quickWheelIndex = 0;
            }

            if (_returnToPlayerAction != null && _returnToPlayerAction.WasPressedThisFrame())
            {
                ReturnToPlayerInCombat();
            }
        }

        private void CycleSupportMode()
        {
            SupportMode next = (SupportMode)(((int)_encounter.SupportMode + 1) % 4);
            _encounter.SetSupportMode(next);
            string label = next switch
            {
                SupportMode.Free => "自由",
                SupportMode.Focus => "集火",
                SupportMode.FreeAllOut => "自由(猛攻)",
                SupportMode.FocusAllOut => "集火(猛攻)",
                _ => next.ToString(),
            };
            Toast($"支援模式：{label}");
        }

        private void ReturnToPlayerInCombat()
        {
            _quickWheelOpen = false;
            for (int i = _team.Active.Count - 1; i >= 0; i--)
            {
                int id = _team.Active[i];
                _encounter.Unfield(id);
                _team.Recall(id, _nowMs);
            }

            _returnedToPlayer = true;
            UpdateDirectControlState();
            Toast("已回到角色（可自由行動，投擲收服需等脫戰）");
        }

        private void ReadQuickWheelInput()
        {
            if (_menuCancel.WasPressedThisFrame())
            {
                _quickWheelOpen = false;
                return;
            }

            int reserveCount = 0;
            foreach (int id in _team.Pool)
            {
                if (!In(_team.Active, id) && !_encounter.Units[id].Combatant.IsFainted && !_team.IsRetired(id))
                {
                    reserveCount++;
                }
            }

            if (reserveCount == 0)
            {
                return;
            }

            if (_menuUp.WasPressedThisFrame())
            {
                _quickWheelIndex = (_quickWheelIndex - 1 + reserveCount) % reserveCount;
            }

            if (_menuDown.WasPressedThisFrame())
            {
                _quickWheelIndex = (_quickWheelIndex + 1) % reserveCount;
            }

            if (_menuSubmit.WasPressedThisFrame())
            {
                int idx = 0;
                foreach (int id in _team.Pool)
                {
                    if (In(_team.Active, id) || _encounter.Units[id].Combatant.IsFainted || _team.IsRetired(id))
                    {
                        continue;
                    }

                    if (idx == _quickWheelIndex)
                    {
                        if (_team.Switch(_mainId, id, _nowMs) == TeamResult.Ok)
                        {
                            float3 at = _encounter.Units[_mainId].Position;
                            _encounter.Unfield(_mainId);
                            _encounter.Field(id, _rig.Config.ToDirection(at), true, _nowMs);
                            _mainId = id;
                            UpdateDirectControlState();
                        }
                        else
                        {
                            Toast(_copy.Ui("ui.hud.switch_cooldown"));
                        }

                        _quickWheelOpen = false;
                        break;
                    }

                    idx++;
                }
            }
        }

        private void DrawBossTopBar()
        {
            int bossId = BossUnit();
            if (bossId < 0 || !_wild.TryGetValue(bossId, out WildInfo info) || info.Gone)
            {
                return;
            }

            EncounterUnit boss = _encounter.Units[bossId];
            if (_region != _plan.BossRegion && !boss.Engaged)
            {
                return;
            }

            float w = 480f;
            float h = 54f;
            var box = new Rect((Screen.width - w) * 0.5f, 12f, w, h);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 12f, box.y + 4f, box.width - 24f, box.height - 8f));
            string status = boss.Combatant.IsFainted ? " [倒地]"
                : boss.IsStaggered(_nowMs) ? " [失衡!]"
                : _balance.Enrage.StageFor(boss.Combatant.Hp, boss.Combatant.MaxHp) > 0 ? " [狂暴]"
                : string.Empty;
            string windup = boss.WindupSlot != EncounterUnit.NoWindup && boss.WindupSlot < boss.Moves.Count
                ? $"  ⚡ {_copy.Name(boss.Moves[boss.WindupSlot].Id)}"
                : string.Empty;
            GUILayout.Label($"<b>BOSS · {_copy.Name(boss.Def.Id)}</b> Lv.{info.Level}{status}{windup}",
                new GUIStyle(_label) { fontSize = 15, richText = true });

            float hpFrac = boss.Combatant.MaxHp > 0 ? math.clamp((float)boss.Combatant.Hp / boss.Combatant.MaxHp, 0f, 1f) : 0f;
            Rect bar = GUILayoutUtility.GetRect(box.width - 24f, 12f);
            GUI.color = new Color(0.2f, 0.05f, 0.05f, 0.9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * hpFrac, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.EndArea();
        }

        private void DrawDirectCombatHud()
        {
            if (!IsDirectControlling)
            {
                return;
            }

            EncounterUnit main = _encounter.Units[_mainId];
            float w = 460f;
            float h = 92f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 14f, w, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12));
            GUILayout.Label($"<b>主控：{_copy.Name(main.Def.Id)}</b>  HP {main.Combatant.Hp}/{main.Combatant.MaxHp}  " +
                $"架勢 {main.StanceGauge}/{_balance.Block.CapacityHp(main.Combatant.MaxHp)}  支援：{_encounter.SupportMode}",
                new GUIStyle(_label) { fontSize = 14, richText = true });

            string[] keys = { "LMB/E", "R", "F", "T", "G" };
            string movesLine = string.Empty;
            for (int i = 0; i < main.Moves.Count && i < keys.Length; i++)
            {
                CastMove m = main.Moves[i];
                long cd = math.max(0L, main.Combatant.Cooldowns.ReadyAtMs(i) - _nowMs);
                string cdText = cd > 0 ? $"({cd / 1000f:0.0}s)" : "就緒";
                movesLine += $"[{keys[i]}]{_copy.Name(m.Id)}:{cdText}  ";
            }

            GUILayout.Label(movesLine, new GUIStyle(_label) { fontSize = 13 });
            GUILayout.Label("[RMB/B]格擋  [Space/Y]翻滾  [X]支援模式  [C]快捷輪盤  [H]回到角色",
                new GUIStyle(_label) { fontSize = 12 });
            GUILayout.EndArea();

            if (_quickWheelOpen)
            {
                DrawQuickWheel();
            }
        }

        private void DrawQuickWheel()
        {
            var box = new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.5f - 100f, 280f, 200f);
            GUI.Box(box, "快捷輪盤（更換備援）");
            GUILayout.BeginArea(new Rect(box.x + 12, box.y + 28, box.width - 24, box.height - 36));
            int idx = 0;
            foreach (int id in _team.Pool)
            {
                EncounterUnit u = _encounter.Units[id];
                if (In(_team.Active, id) || u.Combatant.IsFainted || _team.IsRetired(id))
                {
                    continue;
                }

                string prefix = idx == _quickWheelIndex ? "▶ " : "   ";
                GUILayout.Label($"{prefix}{_copy.Name(u.Def.Id)} HP {u.Combatant.Hp}/{u.Combatant.MaxHp}",
                    new GUIStyle(_label) { fontSize = 14 });
                idx++;
            }

            if (idx == 0)
            {
                GUILayout.Label("無可用備援宙獸", new GUIStyle(_label) { fontSize = 14 });
            }

            GUILayout.EndArea();
        }
    }
}
