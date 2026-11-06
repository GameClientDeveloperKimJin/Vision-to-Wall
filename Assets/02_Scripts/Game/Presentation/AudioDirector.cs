using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>The four sound groups of AR-02. A cue with no clip is skipped.</summary>
    public enum AudioCue { Awaken, LaserFire, Parry, BossHit }

    /// <summary>
    /// BGM fade and a fixed pool of SFX sources. Every clip is optional, so the game plays silently until the
    /// sounds are imported. Runs on unscaled time so result slow motion does not stretch the fades.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        [Header("Clips (optional)")]
        [SerializeField] private AudioClip bgm;
        [SerializeField] private AudioClip awaken;
        [SerializeField] private AudioClip laserFire;
        [SerializeField] private AudioClip parry;
        [SerializeField] private AudioClip bossHit;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Min(0.01f)] private float bgmFadeInSec = 1f;
        [SerializeField, Min(0.01f), Tooltip("Matches the 5 s return to blue.")]
        private float bgmFadeOutSec = 5f;
        [SerializeField, Range(1, 16)] private int sfxSources = 6;

        private AudioSource _bgmSource;
        private AudioSource[] _sfx;
        private int _nextSfx;
        private bool _bgmWanted;

        public bool BgmPlaying => _bgmSource != null && _bgmSource.isPlaying;
        public float BgmVolume => _bgmSource != null ? _bgmSource.volume : 0f;

        private void Awake()
        {
            _bgmSource = CreateSource("Bgm");
            _bgmSource.loop = true;
            _bgmSource.clip = bgm;

            _sfx = new AudioSource[sfxSources];
            for (int i = 0; i < sfxSources; i++) _sfx[i] = CreateSource("Sfx");
        }

        private AudioSource CreateSource(string objectName)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        public void PlayBgm()
        {
            _bgmWanted = true;
            if (_bgmSource == null || bgm == null || _bgmSource.isPlaying) return;

            _bgmSource.volume = 0f;
            _bgmSource.Play();
        }

        public void StopBgm() => _bgmWanted = false;

        public void Play(AudioCue cue)
        {
            AudioClip clip = ClipOf(cue);
            if (clip == null || _sfx == null || _sfx.Length == 0) return;

            AudioSource source = _sfx[_nextSfx];
            _nextSfx = (_nextSfx + 1) % _sfx.Length;
            source.PlayOneShot(clip, sfxVolume);
        }

        private AudioClip ClipOf(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.Awaken: return awaken;
                case AudioCue.LaserFire: return laserFire;
                case AudioCue.Parry: return parry;
                case AudioCue.BossHit: return bossHit;
                default: return null;
            }
        }

        private void Update()
        {
            if (_bgmSource == null || !_bgmSource.isPlaying) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            float target = _bgmWanted ? bgmVolume : 0f;
            float fadeSec = _bgmWanted ? bgmFadeInSec : bgmFadeOutSec;
            _bgmSource.volume = Mathf.MoveTowards(_bgmSource.volume, target, bgmVolume * deltaTime / fadeSec);

            if (!_bgmWanted && _bgmSource.volume <= 0f) _bgmSource.Stop();
        }
    }
}
