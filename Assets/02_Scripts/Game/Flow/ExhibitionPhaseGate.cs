using System;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Decorates the exhibition state for the wall and the panel: passes it through in MediaWall and pins it to
    /// Active in every other phase, so the attract text never appears during the game.
    /// </summary>
    public sealed class ExhibitionPhaseGate : IExhibitionStateSource, IDisposable
    {
        private readonly IExhibitionStateSource _inner;
        private readonly IGamePhaseSource _phases;

        public ExhibitionPhaseGate(IExhibitionStateSource inner, IGamePhaseSource phases)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _phases = phases ?? throw new ArgumentNullException(nameof(phases));

            Current = Evaluate();
            _inner.StateChanged += OnInnerChanged;
            _phases.PhaseChanged += OnPhaseChanged;
        }

        public ExhibitionState Current { get; private set; }

        /// <summary>Raised only when <see cref="Current"/> actually changes.</summary>
        public event Action<ExhibitionState> StateChanged;

        public void Dispose()
        {
            _inner.StateChanged -= OnInnerChanged;
            _phases.PhaseChanged -= OnPhaseChanged;
        }

        private void OnInnerChanged(ExhibitionState _) => Refresh();

        private void OnPhaseChanged(GamePhase _) => Refresh();

        private void Refresh()
        {
            ExhibitionState next = Evaluate();
            if (next == Current) return;

            Current = next;
            StateChanged?.Invoke(next);
        }

        private ExhibitionState Evaluate() =>
            _phases.Phase == GamePhase.MediaWall ? _inner.Current : ExhibitionState.Active;
    }
}
