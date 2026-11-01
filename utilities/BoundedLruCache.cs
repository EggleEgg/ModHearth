namespace ModHearth.Utilities;

/// <summary>
/// Thread-safe, size-bounded least-recently-used cache.
/// </summary>
/// <remarks>
/// Eviction only drops the cache's own reference to a value; it never disposes it. Cached brushes and
/// images can still be bound to live controls, so disposing on eviction would break the UI. Callers that
/// own disposable values are responsible for their lifetime.
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

    public BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
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

        lock (gate)
        {
            if (entries.TryGetValue(key, out Entry? existing))
            {
                Touch(existing);
                return existing.Value;
            }

            TValue value = factory(key);
            entries[key] = new Entry(value, order.AddFirst(key));
            Trim();
            return value;
        }
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

    /// <summary>
    /// Returns true when the cache currently holds <paramref name="value"/>.
    /// Used to avoid disposing values that are shared with other consumers.
    /// </summary>
    public bool ContainsValue(TValue value, IEqualityComparer<TValue>? comparer = null)
    {
        lock (gate)
        {
            foreach (Entry entry in entries.Values)
            {
                bool match = comparer != null
                    ? comparer.Equals(entry.Value, value)
                    : EqualityComparer<TValue>.Default.Equals(entry.Value, value);
                if (match)
                    return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            order.Clear();
        }
    }

    private void Touch(Entry entry)
    {
        order.Remove(entry.Node);
        order.AddFirst(entry.Node);
    }

    private void Trim()
    {
        while (entries.Count > capacity && order.Last is LinkedListNode<TKey> last)
        {
            order.RemoveLast();
            _ = entries.Remove(last.Value);
        }
    }
}
