namespace ModHearth.Utilities
{
    /// <summary>
    /// Thread-safe, size-bounded least-recently-used cache.
    /// </summary>
    /// <remarks>
    /// Eviction only drops the cache's own reference to a value; it never disposes it by default. Cached
    /// brushes and images can still be bound to live controls, so disposing on eviction would break the UI.
    /// Callers that own disposable values can opt into lifetime management by supplying an
    /// <c>onRemoved</c> callback, which is invoked for evicted, replaced and cleared values after the lock
    /// has been released (so the callback may safely call back into the cache).
    /// </remarks>
    public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
    {
        private sealed class Entry(TValue value, LinkedListNode<TKey> node)
        {
            public TValue Value { get; } = value;
            public LinkedListNode<TKey> Node { get; } = node;
        }

        private readonly object gate = new();
        private readonly int capacity;
        private readonly Dictionary<TKey, Entry> entries;
        private readonly LinkedList<TKey> order = new();
        private readonly Action<TKey, TValue>? onRemoved;

        public BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null, Action<TKey, TValue>? onRemoved = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            this.capacity = capacity;
            this.onRemoved = onRemoved;
            entries = new Dictionary<TKey, Entry>(capacity, comparer);
        }

        public int Count
        {
            get
            {
                lock (gate)
                    return entries.Count;
            }
        }

        /// <summary>
        /// Returns the cached value for <paramref name="key"/>, creating it with <paramref name="factory"/>
        /// when it is not present. The factory runs while the cache lock is held so concurrent callers never
        /// build the same expensive value more than once.
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(factory);

            List<KeyValuePair<TKey, TValue>>? removed;
            TValue value;
            lock (gate)
            {
                if (entries.TryGetValue(key, out Entry? existing))
                {
                    Touch(existing);
                    return existing.Value;
                }

                value = factory(key);
                entries[key] = new Entry(value, order.AddFirst(key));
                removed = Trim();
            }
            NotifyRemoved(removed);
            return value;
        }

        /// <summary>Inserts or replaces. A replaced, different value is reported to <c>onRemoved</c>.</summary>
        public void Set(TKey key, TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);

            List<KeyValuePair<TKey, TValue>>? removed = null;
            lock (gate)
            {
                if (entries.TryGetValue(key, out Entry? existing))
                {
                    order.Remove(existing.Node);
                    if (onRemoved != null && !ReferenceEquals(existing.Value, value))
                        (removed ??= []).Add(new(key, existing.Value));
                }

                entries[key] = new Entry(value, order.AddFirst(key));
                List<KeyValuePair<TKey, TValue>>? evicted = Trim();
                if (evicted != null)
                    (removed ??= []).AddRange(evicted);
            }
            NotifyRemoved(removed);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);

            lock (gate)
            {
                if (entries.TryGetValue(key, out Entry? entry))
                {
                    Touch(entry);
                    value = entry.Value;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        /// <summary>Returns true when any cached value satisfies <paramref name="predicate"/>.</summary>
        public bool Any(Func<TValue, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            lock (gate)
            {
                foreach (Entry entry in entries.Values)
                {
                    if (predicate(entry.Value))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true when the cache currently holds <paramref name="value"/>.
        /// Used to avoid disposing values that are shared with other consumers.
        /// </summary>
        public bool ContainsValue(TValue value, IEqualityComparer<TValue>? comparer = null)
            => Any(v => comparer != null ? comparer.Equals(v, value) : EqualityComparer<TValue>.Default.Equals(v, value));

        public void Clear()
        {
            List<KeyValuePair<TKey, TValue>>? removed = null;
            lock (gate)
            {
                if (onRemoved != null)
                    removed = [.. entries.Select(kv => new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value))];
                entries.Clear();
                order.Clear();
            }
            NotifyRemoved(removed);
        }

        private void Touch(Entry entry)
        {
            order.Remove(entry.Node);
            order.AddFirst(entry.Node);
        }

        // Caller holds the lock.
        private List<KeyValuePair<TKey, TValue>>? Trim()
        {
            List<KeyValuePair<TKey, TValue>>? removed = null;
            while (entries.Count > capacity && order.Last is LinkedListNode<TKey> last)
            {
                order.RemoveLast();
                if (entries.Remove(last.Value, out Entry? entry) && onRemoved != null)
                    (removed ??= []).Add(new(last.Value, entry.Value));
            }
            return removed;
        }

        private void NotifyRemoved(List<KeyValuePair<TKey, TValue>>? removed)
        {
            if (removed == null || onRemoved == null)
                return;

            foreach ((TKey key, TValue value) in removed)
            {
                try { onRemoved(key, value); }
                catch (Exception ex) { AppLogging.LogException("BoundedLruCache removal callback failed", ex); }
            }
        }
    }
}
