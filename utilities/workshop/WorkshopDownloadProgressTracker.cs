namespace ModHearth.Utilities.Workshop
{
    /// <summary>
    /// Aggregates workshop download progress across every download path (the downloader queue,
    /// Steam-client redownloads, ...). Each logical run opens its own
    /// <see cref="WorkshopDownloadSession"/> so a finished run never leaks into the next one.
    /// Thread-safe: report from any thread.
    /// </summary>
    public sealed class WorkshopDownloadProgressTracker
    {
        private sealed class Entry(long sessionId)
        {
            public long SessionId { get; } = sessionId;
            public double Progress { get; set; }
            public bool Completed { get; set; }
        }

        private readonly object gate = new();
        private readonly Dictionary<long, Entry> entries = [];
        private long nextEntryId;
        private long nextSessionId;

        /// <summary>Raised whenever the aggregate values change. May be raised from any thread.</summary>
        public event EventHandler? Changed;

        /// <summary>Number of tracked downloads that have not completed yet.</summary>
        public int ActiveDownloadCount { get; private set; }

        /// <summary>Overall progress (0-100) across every tracked download in the current run(s).</summary>
        public double OverallProgress { get; private set; }

        /// <summary>Short human-readable summary shown next to the aggregate progress bar.</summary>
        public string StatusText { get; private set; } = string.Empty;

        public bool IsActive => ActiveDownloadCount > 0;

        /// <summary>Starts a logical run; all of its entries are dropped together on dispose.</summary>
        public WorkshopDownloadSession BeginSession()
        {
            lock (gate)
            {
                nextSessionId++;
                return new WorkshopDownloadSession(this, nextSessionId);
            }
        }

        internal long Track(long sessionId, double initialProgress = 0)
        {
            bool changed;
            long entryId;
            lock (gate)
            {
                entryId = ++nextEntryId;
                entries[entryId] = new Entry(sessionId) { Progress = Math.Clamp(initialProgress, 0, 100) };
                changed = RecalculateLocked();
            }

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
            return entryId;
        }

        internal void Report(long entryId, double percentage)
        {
            bool changed = false;
            lock (gate)
            {
                if (entries.TryGetValue(entryId, out Entry? entry) && !entry.Completed)
                {
                    double clamped = Math.Clamp(percentage, 0, 100);
                    if (Math.Abs(entry.Progress - clamped) >= 0.05)
                    {
                        entry.Progress = clamped;
                        changed = RecalculateLocked();
                    }
                }
            }

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void Complete(long entryId)
        {
            bool changed = false;
            lock (gate)
            {
                if (entries.TryGetValue(entryId, out Entry? entry) && !entry.Completed)
                {
                    entry.Completed = true;
                    entry.Progress = 100;
                    changed = RecalculateLocked();
                }
            }

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void Drop(long entryId)
        {
            bool changed;
            lock (gate)
                changed = entries.Remove(entryId) && RecalculateLocked();

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void EndSession(long sessionId)
        {
            bool changed = false;
            lock (gate)
            {
                List<long> owned = [.. entries.Where(kv => kv.Value.SessionId == sessionId).Select(kv => kv.Key)];
                if (owned.Count > 0)
                {
                    foreach (long entryId in owned)
                        _ = entries.Remove(entryId);
                    changed = RecalculateLocked();
                }
            }

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private bool RecalculateLocked()
        {
            int active = 0;
            int counted = 0;
            double sum = 0;
            foreach (Entry entry in entries.Values)
            {
                if (!entry.Completed)
                    active++;
                sum += entry.Completed ? 100 : entry.Progress;
                counted++;
            }

            double overall = active > 0 && counted > 0 ? sum / counted : 0;
            string modLabel = active == 1 ? "mod" : "mods";
            string status = active > 0
                ? $"{active} workshop {modLabel} · {overall:F0}%"
                : string.Empty;

            if (ActiveDownloadCount == active
                && Math.Abs(OverallProgress - overall) < 0.05
                && string.Equals(StatusText, status, StringComparison.Ordinal))
            {
                return false;
            }

            ActiveDownloadCount = active;
            OverallProgress = overall;
            StatusText = status;
            return true;
        }
    }

    /// <summary>One logical download run. Disposing it drops every entry it tracked.</summary>
    public sealed class WorkshopDownloadSession : IDisposable
    {
        private readonly WorkshopDownloadProgressTracker tracker;
        private readonly long sessionId;
        private bool disposed;

        internal WorkshopDownloadSession(WorkshopDownloadProgressTracker tracker, long sessionId)
        {
            this.tracker = tracker;
            this.sessionId = sessionId;
        }

        public long Track(double initialProgress = 0) => tracker.Track(sessionId, initialProgress);

        public void Report(long entryId, double percentage) => tracker.Report(entryId, percentage);

        public void Complete(long entryId) => tracker.Complete(entryId);

        public void Drop(long entryId) => tracker.Drop(entryId);

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            tracker.EndSession(sessionId);
        }
    }
}
