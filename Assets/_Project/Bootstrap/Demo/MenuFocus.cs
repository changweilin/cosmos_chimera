namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Tracks a wrapping focus in a changing menu.</summary>
    /// <remarks>Pre: none. Post: empty menus have index -1. Side effects: none outside this instance.</remarks>
    public sealed class MenuFocus
    {
        private int _count;
        private int _index = -1;

        /// <summary>Current focus, or -1 when empty.</summary>
        /// <remarks>Pre: none. Post: -1 or a valid index. Side effects: none.</remarks>
        public int Index => _index;

        /// <summary>Updates the number of selectable buttons.</summary>
        /// <remarks>Pre: count >= 0. Post: focus is clamped, or -1 when empty. Side effects: changes focus.</remarks>
        public void SetCount(int count)
        {
            Cosmimera.Core.Contracts.Contract.Requires(count >= 0, "count must be nonnegative");
            _count = count;
            _index = count == 0 ? -1 : System.Math.Min(System.Math.Max(_index, 0), count - 1);
        }

        /// <summary>Moves the focus with wraparound.</summary>
        /// <remarks>Pre: none. Post: focus wraps by delta, or stays -1 when empty. Side effects: changes focus.</remarks>
        public void Move(int delta)
        {
            if (_count > 0)
            {
                _index = (int)(((_index + (long)delta) % _count + _count) % _count);
            }
        }
    }
}
