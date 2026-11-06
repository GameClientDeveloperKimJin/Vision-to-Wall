using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Combat events to effects and sounds. Only listens; nothing here feeds back into the rules. Effects use a
    /// fixed particle budget, and the boss is hidden on defeat and shown again when the wall returns.
    /// </summary>
    public sealed class CombatPresenter : MonoBehaviour
    {
        [SerializeField] private ShieldView shield;
        [SerializeField] private PlayerAvatarView avatar;
        [SerializeField] private PlayerTrail trail;
        [SerializeField] private AimReticle reticle;
        [SerializeField] private AudioDirector audioDirector;
        [SerializeField, Tooltip("World-space particles on the overlay layer, with emission off.")]
        private ParticleSystem sparks;
        [SerializeField, Min(0)] private int parrySparks = 80;
        [SerializeField, Min(0)] private int hitSparks = 50;
        [SerializeField, Min(0)] private int explosionSparks = 900;
        [SerializeField, Min(0f)] private float parrySpeed = 6f;
        [SerializeField, Min(0f)] private float hitSpeed = 3f;
        [SerializeField, Min(0f)] private float explosionSpeed = 9f;
        [SerializeField] private Color parryColor = new Color(1f, 0.8f, 0.35f, 1f);
        [SerializeField] private Color hitColor = new Color(1f, 0.15f, 0.2f, 1f);
        [SerializeField] private Color explosionColorA = new Color(1f, 0.2f, 0.6f, 1f);
        [SerializeField] private Color explosionColorB = new Color(0.2f, 0.9f, 1f, 1f);

        private BossCombatController _combat;
        private IGamePhaseSource _phase;
        private Transform _boss;
        private Renderer[] _bossRenderers;
        private bool[] _bossRendererOn;
        private Action<CombatEvent> _onCombatEvent;
        private Action<GamePhase> _onPhaseChanged;
        private ParticleSystem.EmitParams _emit;
        private bool _bossHidden;

        public bool BossHidden => _bossHidden;

        public void Initialize(IGamePhaseSource phase, BossCombatController combat, float hitRadius)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (combat == null) throw new ArgumentNullException(nameof(combat));

            Unsubscribe();

            _phase = phase;
            _combat = combat;
            var look = combat.GetComponentInChildren<BossLookAt>(true);
            _boss = look != null ? look.transform : combat.transform;
            _bossRenderers = _boss.GetComponentsInChildren<Renderer>(true);
            _bossRendererOn = new bool[_bossRenderers.Length];
            _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            if (shield != null) shield.Bind(combat, hitRadius);
            if (avatar != null) avatar.Bind(combat, hitRadius);
            if (trail != null) trail.Bind(combat);
            if (reticle != null) reticle.Bind(combat, hitRadius);

            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            ShowBoss();
        }

        private void Subscribe()
        {
            if (_combat == null || _onCombatEvent != null) return;

            _onCombatEvent = OnCombatEvent;
            _onPhaseChanged = OnPhaseChanged;
            _combat.CombatEventRaised += _onCombatEvent;
            _phase.PhaseChanged += _onPhaseChanged;
        }

        private void Unsubscribe()
        {
            if (_combat == null || _onCombatEvent == null) return;

            _combat.CombatEventRaised -= _onCombatEvent;
            _phase.PhaseChanged -= _onPhaseChanged;
            _onCombatEvent = null;
            _onPhaseChanged = null;
        }

        private void OnPhaseChanged(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Combat:
                    if (shield != null) shield.Clear();
                    if (avatar != null) avatar.SetVisible(true);
                    if (trail != null) trail.SetActive(true);
                    break;
                case GamePhase.Result:
                    if (trail != null) trail.SetActive(false);
                    if (shield != null) shield.Lower();
                    break;
                case GamePhase.MediaWall:
                    if (avatar != null) avatar.SetVisible(false);
                    if (trail != null) trail.SetActive(false);
                    if (shield != null) shield.Clear();
                    ShowBoss();
                    break;
            }
        }

        private void OnCombatEvent(CombatEvent e)
        {
            switch (e.type)
            {
                case CombatEventType.Fired:
                    Play(AudioCue.LaserFire);
                    break;
                case CombatEventType.ShieldRaised:
                    if (shield != null) shield.Raise();
                    break;
                case CombatEventType.ShieldLowered:
                    if (shield != null) shield.Lower();
                    break;
                case CombatEventType.Parried:
                    Vector3 parryPoint = PlaneWorld(e.position);
                    if (shield != null) shield.Strike(parryPoint);
                    Burst(parryPoint, parrySparks, parrySpeed, parryColor, parryColor);
                    Play(AudioCue.Parry);
                    break;
                case CombatEventType.PlayerHit:
                    if (avatar != null) avatar.Flash();
                    Burst(PlaneWorld(e.position), hitSparks, hitSpeed, hitColor, hitColor);
                    Play(AudioCue.BossHit);
                    break;
                case CombatEventType.BossDefeated:
                    Burst(_boss.position, explosionSparks, explosionSpeed, explosionColorA, explosionColorB);
                    HideBoss();
                    Play(AudioCue.BossHit);
                    break;
            }
        }

        private Vector3 PlaneWorld(Vector2 normalized) =>
            WallSpace.ToWorld(_combat.OverlayCamera, normalized, _combat.PlayerPlaneDistance);

        private void Play(AudioCue cue)
        {
            if (audioDirector != null) audioDirector.Play(cue);
        }

        private void Burst(Vector3 center, int count, float speed, Color colorA, Color colorB)
        {
            if (sparks == null) return;

            for (int i = 0; i < count; i++)
            {
                _emit.position = center;
                _emit.velocity = UnityEngine.Random.onUnitSphere * (speed * UnityEngine.Random.Range(0.2f, 1f));
                _emit.startLifetime = UnityEngine.Random.Range(0.5f, 1.4f);
                _emit.startSize = UnityEngine.Random.Range(0.02f, 0.08f);
                _emit.startColor = Color.Lerp(colorA, colorB, UnityEngine.Random.value);
                sparks.Emit(_emit, 1);
            }
        }

        private void HideBoss()
        {
            if (_bossHidden || _bossRenderers == null) return;

            _bossHidden = true;
            for (int i = 0; i < _bossRenderers.Length; i++)
            {
                Renderer item = _bossRenderers[i];
                if (item == null) continue;
                _bossRendererOn[i] = item.enabled;
                item.enabled = false;
            }
        }

        private void ShowBoss()
        {
            if (!_bossHidden) return;

            _bossHidden = false;
            for (int i = 0; i < _bossRenderers.Length; i++)
            {
                if (_bossRenderers[i] != null) _bossRenderers[i].enabled = _bossRendererOn[i];
            }
        }
    }
}
