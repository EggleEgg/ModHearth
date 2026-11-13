using ModHearth.Models;

namespace ModHearth.UI.ModList
{
    /// <summary>
    /// Bounded undo/redo history for the active modlist. Snapshots only store references to the
    /// immutable <see cref="DfhMod"/> instances already owned by the mod manager, so keeping
    /// several states costs an array of references rather than copies of every mod.
    /// </summary>
    internal sealed class ModListHistory
    {
        /// <summary>Number of previous list states that can be restored with undo.</summary>
        public const int MaxUndoDepth = 5;

        // The current state plus up to MaxUndoDepth previous states.
        private const int MaxSnapshots = MaxUndoDepth + 1;

        private readonly List<DfhMod[]> snapshots = new(MaxSnapshots);
        private int cursor = -1;

        public bool CanUndo => cursor > 0;

        public bool CanRedo => cursor >= 0 && cursor + 1 < snapshots.Count;

        /// <summary>Clears the history and makes <paramref name="mods"/> the current state.</summary>
        public void Reset(IReadOnlyList<DfhMod> mods)
        {
            snapshots.Clear();
            snapshots.Add(Copy(mods));
            cursor = 0;
        }

        public void Clear()
        {
            snapshots.Clear();
            cursor = -1;
        }

        /// <summary>
        /// Records a new current state. Identical consecutive states are ignored so repeated
        /// save/sort passes do not consume undo slots or clear a valid redo branch.
        /// </summary>
        /// <returns><see langword="true"/> when a new snapshot was stored.</returns>
        public bool Record(IReadOnlyList<DfhMod> mods)
        {
            if (cursor < 0)
            {
                Reset(mods);
                return true;
            }

            if (SameMods(snapshots[cursor], mods))
                return false;

            int redoStart = cursor + 1;
            if (redoStart < snapshots.Count)
                snapshots.RemoveRange(redoStart, snapshots.Count - redoStart);

            snapshots.Add(Copy(mods));
            cursor = snapshots.Count - 1;

            if (snapshots.Count > MaxSnapshots)
            {
                snapshots.RemoveAt(0);
                cursor--;
            }

            return true;
        }

        /// <summary>Moves back one state and returns it, or null when no undo state remains.</summary>
        public IReadOnlyList<DfhMod>? Undo()
        {
            if (!CanUndo)
                return null;

            cursor--;
            return snapshots[cursor];
        }

        /// <summary>Moves forward one state and returns it, or null when no redo state remains.</summary>
        public IReadOnlyList<DfhMod>? Redo()
        {
            if (!CanRedo)
                return null;

            cursor++;
            return snapshots[cursor];
        }

        private static DfhMod[] Copy(IReadOnlyList<DfhMod> mods)
        {
            if (mods.Count == 0)
                return [];

            DfhMod[] copy = new DfhMod[mods.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = mods[i];

            return copy;
        }

        private static bool SameMods(DfhMod[] snapshot, IReadOnlyList<DfhMod> mods)
        {
            if (snapshot.Length != mods.Count)
                return false;

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != mods[i])
                    return false;
            }

            return true;
        }
    }
}
