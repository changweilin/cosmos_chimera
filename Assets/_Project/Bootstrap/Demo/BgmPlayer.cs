using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Plays background music with seamless looping and cross-fading between tracks.
    /// REQ: docs/roadmap/phase-7.md#P7-03.
    /// </summary>
    /// <remarks>
    /// Pre: initialized with an optional BgmTable. Post: controls audio playback without interrupting gameplay.
    /// Side effects: manages two AudioSource components for cross-fading.
    /// </remarks>
    public sealed class BgmPlayer : MonoBehaviour
    {
        private AudioSource _sourceA;
        private AudioSource _sourceB;
        private bool _usingA = true;
        private BgmTable _table;
        private string _currentTrackId;
        private float _fadeDuration = 1.5f;
        private float _fadeTimer;
        private bool _isFading;
        private float _masterVolume = 0.6f;

        /// <summary>Active singleton player instance.</summary>
        /// <remarks>Pre: none. Post: valid instance or null. Side effects: none.</remarks>
        public static BgmPlayer Instance { get; private set; }

        /// <summary>Currently playing track id.</summary>
        /// <remarks>Pre: none. Post: track id or null. Side effects: none.</remarks>
        public string CurrentTrackId => _currentTrackId;

        /// <summary>Initializes audio sources and data table.</summary>
        /// <remarks>Pre: none. Post: AudioSources attached and configured. Side effects: adds components.</remarks>
        public void Initialize(BgmTable table, float volume = 0.6f)
        {
            _table = table;
            _masterVolume = Mathf.Clamp01(volume);
            if (_sourceA == null)
            {
                _sourceA = gameObject.AddComponent<AudioSource>();
                _sourceA.loop = true;
                _sourceA.playOnAwake = false;
            }
            if (_sourceB == null)
            {
                _sourceB = gameObject.AddComponent<AudioSource>();
                _sourceB.loop = true;
                _sourceB.playOnAwake = false;
            }
            Instance = this;
        }

        /// <summary>Transitions playback to <paramref name="trackId"/> with cross-fading.</summary>
        /// <remarks>Pre: none. Post: new track playing or stopped if not found. Side effects: changes audio output.</remarks>
        public void Play(string trackId, float fadeDuration = 1.5f)
        {
            if (string.IsNullOrEmpty(trackId) || trackId == _currentTrackId)
            {
                return;
            }

            AudioClip nextClip = _table != null ? _table.Find(trackId) : null;
            if (nextClip == null)
            {
                Stop(fadeDuration);
                _currentTrackId = trackId;
                return;
            }

            _currentTrackId = trackId;
            _fadeDuration = Mathf.Max(0.1f, fadeDuration);
            _fadeTimer = 0f;
            _isFading = true;

            AudioSource incoming = _usingA ? _sourceB : _sourceA;
            if (incoming != null)
            {
                incoming.clip = nextClip;
                incoming.volume = 0f;
                incoming.Play();
            }
            _usingA = !_usingA;
        }

        /// <summary>Fades out active music.</summary>
        /// <remarks>Pre: none. Post: audio fading to 0. Side effects: stops audio when faded.</remarks>
        public void Stop(float fadeDuration = 1.5f)
        {
            _currentTrackId = null;
            _fadeDuration = Mathf.Max(0.1f, fadeDuration);
            _fadeTimer = 0f;
            _isFading = true;
        }

        private void Update()
        {
            if (!_isFading)
            {
                return;
            }

            _fadeTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_fadeTimer / _fadeDuration);

            AudioSource outgoing = _usingA ? _sourceB : _sourceA;
            AudioSource incoming = _usingA ? _sourceA : _sourceB;

            if (outgoing != null && outgoing.isPlaying)
            {
                outgoing.volume = Mathf.Lerp(_masterVolume, 0f, progress);
                if (progress >= 1f)
                {
                    outgoing.Stop();
                }
            }

            if (incoming != null && incoming.isPlaying)
            {
                incoming.volume = Mathf.Lerp(0f, _masterVolume, progress);
            }

            if (progress >= 1f)
            {
                _isFading = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
