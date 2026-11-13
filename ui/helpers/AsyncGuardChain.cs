namespace ModHearth.UI.Helpers
{
    /// <summary>
    /// Ordered collection of asynchronous veto checks. Guards run one at a time and may show their
    /// own UI; the first guard that returns false stops the chain. Used to gate destructive actions
    /// (such as restarting the application) so new preconditions are added in one place instead of
    /// at every call site.
    /// </summary>
    public sealed class AsyncGuardChain
    {
        private readonly Lock gate = new();
        private readonly List<Func<Task<bool>>> guards = [];

        /// <summary>
        /// Appends a guard. Guards run in registration order and must return true to let the action
        /// continue.
        /// </summary>
        public void Register(Func<Task<bool>> guard)
        {
            ArgumentNullException.ThrowIfNull(guard);

            lock (gate)
                guards.Add(guard);
        }

        /// <summary>
        /// Runs every registered guard. Returns false as soon as one vetoes the action.
        /// </summary>
        public async Task<bool> RunAsync()
        {
            Func<Task<bool>>[] snapshot;
            lock (gate)
                snapshot = [.. guards];

            foreach (Func<Task<bool>> guard in snapshot)
            {
                if (!await guard())
                    return false;
            }

            return true;
        }
    }
}
