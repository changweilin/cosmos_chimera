using System;
using Cosmimera.Core.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Local-only hit feel: hitstop holds, camera shake, gamepad rumble, knockout slow motion and the player-hit vignette
    /// (combat-feel.md). Never touches rule state; the fixed combat tick still advances one TickMs per step.
    /// </summary>
    /// <remarks>
    /// Pre: main thread; <see cref="Dispose"/> on teardown.
    /// Post: after Dispose the camera, rumble and Time.timeScale are back to what this class found.
    /// Side effects: subscribes to RenderPipelineManager camera callbacks; may set Time.timeScale and gamepad motors.
    /// </remarks>
    internal sealed class HitFeelDirector : IDisposable
    {
        private const float ShakeDecayPerSecond = 9f;
        private const float RumbleSeconds = 0.12f;
        private const float VignetteSeconds = 0.35f;

        private readonly Camera _camera;
        private readonly HitFeelTuning _tuning;
        private readonly HitstopLedger _ledger;

        private float _nowMs;
        private float _shakeM;
        private Vector3 _appliedOffset;
        private bool _offsetApplied;
        private float _rumbleLeft;
        private bool _rumbling;
        private float _slowMoLeft;
        private bool _slowMoOwned;
        private float _vignetteLeft;
        private uint _jitterState = 0x2545F491u;
        private Texture2D _vignetteTex;
        private bool _disposed;

        /// <summary>Hooks camera rendering for shake.</summary>
        /// <remarks>Pre: camera != null. Post: idle (no shake, no freeze). Side effects: subscribes to RenderPipelineManager.</remarks>
        public HitFeelDirector(Camera camera, in HitFeelTuning tuning)
        {
            Contract.Requires(camera != null, "camera required");
            _camera = camera;
            _tuning = tuning;
            _ledger = new HitstopLedger(tuning.BudgetMsPerSecond);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        /// <summary>Tuning in use.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public HitFeelTuning Tuning => _tuning;

        /// <summary>Plays one cast's feedback: a single hitstop for the attacker and every hit target (highest tier wins).</summary>
        /// <remarks>
        /// Pre: not disposed; attackerId >= 0; targets non-null with ids >= 0 (player character excluded by the caller).
        /// Post: each listed unit frozen for its budget-limited share of the tier's hitstop; shake raised to at least the
        ///       distance-scaled amplitude; rumble for Heavy and above.
        /// Side effects: ledger, shake, rumble state.
        /// </remarks>
        public void PlayCast(int attackerId, int[] targets, int targetCount, HitTier tier, bool boss, float distanceToPlayerM)
        {
            if (_disposed)
            {
                return;
            }

            Contract.Requires(attackerId >= 0, "attackerId must be >= 0");
            Contract.Requires(targets != null && targetCount >= 0 && targetCount <= targets.Length, "targets out of range");
            int ms = _tuning.HitstopMs(tier, boss);
            if (ms > 0)
            {
                _ledger.Request(attackerId, _nowMs, ms);
                for (int i = 0; i < targetCount; i++)
                {
                    _ledger.Request(targets[i], _nowMs, ms);
                }
            }

            _shakeM = Mathf.Max(_shakeM, _tuning.ShakeM(tier, boss, distanceToPlayerM));
            if (tier >= HitTier.Heavy && distanceToPlayerM < _tuning.ShakeFarM)
            {
                Rumble(tier == HitTier.Knockout ? 0.8f : 0.5f);
            }
        }

        /// <summary>Player character took a hit: red edge flash and rumble, no hitstop (keeps roll input responsive).</summary>
        /// <remarks>Pre: none. Post: vignette restarted. Side effects: rumble.</remarks>
        public void PlayPlayerHit()
        {
            if (_disposed)
            {
                return;
            }

            _vignetteLeft = VignetteSeconds;
            Rumble(0.6f);
        }

        /// <summary>Starts knockout slow motion (single-player only; the demo has no netcode).</summary>
        /// <remarks>
        /// Pre: none. Post: Time.timeScale == SlowMoTimeScale until the slow-mo ends, unless something else (the journal
        ///       pause) owns timeScale, in which case nothing changes.
        /// Side effects: Time.timeScale.
        /// </remarks>
        public void PlaySlowMo(bool boss)
        {
            if (_disposed)
            {
                return;
            }

            // The journal pauses with timeScale 0 and restores its saved value; never fight it.
            if (!_slowMoOwned && !Mathf.Approximately(Time.timeScale, 1f))
            {
                return;
            }

            _slowMoLeft = Mathf.Max(_slowMoLeft, (boss ? _tuning.BossFinishSlowMoMs : _tuning.KnockoutSlowMoMs) / 1000f);
            Time.timeScale = _tuning.SlowMoTimeScale;
            _slowMoOwned = true;
        }

        /// <summary>Whether <paramref name="unitId"/>'s view should hold its pose this frame.</summary>
        /// <remarks>Pre: none. Post: true while its hitstop lasts. Side effects: none.</remarks>
        public bool IsFrozen(int unitId) => _ledger.IsFrozen(unitId, _nowMs);

        /// <summary>Small deterministic-looking jitter for a frozen hit target (presentation only).</summary>
        /// <remarks>Pre: none. Post: |result| &lt;= amplitude per axis. Side effects: advances a private xorshift.</remarks>
        public Vector3 Jitter(float amplitude)
        {
            return new Vector3(NextSigned(), NextSigned() * 0.5f, NextSigned()) * amplitude;
        }

        /// <summary>Advances timers with unscaled time so slow motion and the journal pause cannot stall them.</summary>
        /// <remarks>Pre: unscaledDt >= 0. Post: expired effects stopped. Side effects: timeScale, rumble.</remarks>
        public void Update(float unscaledDt)
        {
            if (_disposed)
            {
                return;
            }

            _nowMs += unscaledDt * 1000f;
            _shakeM = Mathf.Max(0f, _shakeM - _shakeM * ShakeDecayPerSecond * unscaledDt - 0.002f * unscaledDt * 60f);
            _vignetteLeft = Mathf.Max(0f, _vignetteLeft - unscaledDt);

            if (_rumbling)
            {
                _rumbleLeft -= unscaledDt;
                if (_rumbleLeft <= 0f)
                {
                    StopRumble();
                }
            }

            if (_slowMoOwned)
            {
                _slowMoLeft -= unscaledDt;
                if (_slowMoLeft <= 0f)
                {
                    if (Mathf.Approximately(Time.timeScale, _tuning.SlowMoTimeScale))
                    {
                        Time.timeScale = 1f;
                        _slowMoOwned = false;
                    }
                    else if (Time.timeScale != 0f)
                    {
                        _slowMoOwned = false;
                    }

                    // timeScale 0: the journal opened mid slow-mo and restores SlowMoTimeScale on close; stay owner to end it then.
                }
            }
        }

        /// <summary>Draws the player-hit red edge.</summary>
        /// <remarks>Pre: called from OnGUI. Post: none. Side effects: GUI draw; lazily creates one texture.</remarks>
        public void DrawOverlay()
        {
            if (_disposed || _vignetteLeft <= 0f)
            {
                return;
            }

            _vignetteTex ??= BuildVignette();
            Color prev = GUI.color;
            GUI.color = new Color(0.9f, 0.08f, 0.05f, 0.55f * (_vignetteLeft / VignetteSeconds));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _vignetteTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        /// <summary>Unhooks callbacks and restores camera, rumble and timeScale.</summary>
        /// <remarks>Pre: main thread. Post: disposed; idempotent. Side effects: as listed.</remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestoreCamera();
            StopRumble();
            if (_slowMoOwned && Mathf.Approximately(Time.timeScale, _tuning.SlowMoTimeScale))
            {
                Time.timeScale = 1f;
            }

            _slowMoOwned = false;
            if (_vignetteTex != null)
            {
                UnityEngine.Object.Destroy(_vignetteTex);
            }
        }

        // Offset only for the render, then undo: Cinemachine rewrites the camera pose every LateUpdate, and leaving the
        // offset in the transform would accumulate drift and leak into anything reading the camera position.
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _camera || _shakeM <= 1e-4f)
            {
                return;
            }

            _appliedOffset = Jitter(_shakeM);
            _camera.transform.position += _appliedOffset;
            _offsetApplied = true;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == _camera)
            {
                RestoreCamera();
            }
        }

        private void RestoreCamera()
        {
            if (!_offsetApplied || _camera == null)
            {
                return;
            }

            _camera.transform.position -= _appliedOffset;
            _offsetApplied = false;
        }

        private void Rumble(float strength)
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            pad.SetMotorSpeeds(strength * 0.6f, strength);
            _rumbleLeft = RumbleSeconds;
            _rumbling = true;
        }

        private void StopRumble()
        {
            if (!_rumbling)
            {
                return;
            }

            _rumbling = false;
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        private float NextSigned()
        {
            _jitterState ^= _jitterState << 13;
            _jitterState ^= _jitterState >> 17;
            _jitterState ^= _jitterState << 5;
            return (_jitterState / (float)uint.MaxValue) * 2f - 1f;
        }

        private static Texture2D BuildVignette()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float edge = Mathf.Clamp01((Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) - 0.6f) / 0.4f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(edge * edge * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
