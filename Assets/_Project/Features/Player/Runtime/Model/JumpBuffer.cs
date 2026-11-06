using Cosmimera.Core.Contracts;

namespace Cosmimera.Player
{
    /// <summary>Remembers a jump press for a short window so a press just before landing still jumps.</summary>
    /// <remarks>
    /// Pre: times passed to one instance are from one monotonic clock (seconds).
    /// Post: a press is pending from its time t through t + <see cref="WindowSeconds"/> inclusive, until consumed.
    /// Side effects: none (mutable value; mutating methods change only this value).
    /// REQ: docs/reviews/P1-05-sol.md#F1 — the glue used to hand an expired press to the motor before expiring it,
    /// and cleared it on "left the ground", which missed a jump started on the landing step.
    /// </remarks>
    internal struct JumpBuffer
    {
        /// <summary>How long a press stays pending, seconds.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// Covers a press up to ~9 fixed steps before landing; longer and a mashed jump fires again on touchdown.
        /// </remarks>
        public const float WindowSeconds = 0.15f;

        private bool _pressed;
        private float _pressedAt;

        /// <summary>Records a press at <paramref name="time"/>; a later press restarts the window.</summary>
        /// <remarks>Pre: time finite. Post: IsPending(time). Side effects: mutates this value.</remarks>
        public void Press(float time)
        {
            Contract.Requires(!float.IsNaN(time) && !float.IsInfinity(time), "time must be finite");
            _pressed = true;
            _pressedAt = time;
        }

        /// <summary>True when a press is waiting and is no older than the window at <paramref name="time"/>.</summary>
        /// <remarks>Pre: time finite. Post: false after Consume until the next Press. Side effects: none.</remarks>
        public readonly bool IsPending(float time)
        {
            Contract.Requires(!float.IsNaN(time) && !float.IsInfinity(time), "time must be finite");
            return _pressed && time - _pressedAt <= WindowSeconds;
        }

        /// <summary>Drops the pending press (call when a jump actually started).</summary>
        /// <remarks>Pre: none. Post: !IsPending(any time) until the next Press. Side effects: mutates this value.</remarks>
        public void Consume()
        {
            _pressed = false;
        }
    }
}
