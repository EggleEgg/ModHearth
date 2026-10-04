using System.Collections.Concurrent;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Helper to batch delete mods and their notifications into 1
    /// </summary>
    public static class ModDeletionNotifier
    {
        public static void NotifyDeleted(Action<string, string> sender, string itemPath, string sourceFolder)
        {
            var scope = ModDeletionBatchScope.Current;
            if (scope != null)
            {
                scope.RegisterDeletion(itemPath, sourceFolder);
            }
            else
            {
                string fileName = Path.GetFileName(itemPath);
                sender($"Deleted mod folder: {fileName}", "trashIcon.svg");
            }
        }
    }
    public sealed class ModDeletionBatchScope : IDisposable
    {
        private static readonly AsyncLocal<ModDeletionBatchScope?> _currentScope = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _deletionsBySource = new(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, string> _notificationSender;
        private readonly ModDeletionBatchScope? _parentScope;

        public static ModDeletionBatchScope? Current => _currentScope.Value;

        public ModDeletionBatchScope(Action<string, string> notificationSender)
        {
            _notificationSender = notificationSender ?? throw new ArgumentNullException(nameof(notificationSender));
            _parentScope = _currentScope.Value;
            _currentScope.Value = this;
        }

        public void RegisterDeletion(string itemPath, string sourceFolder)
        {
            string src = string.IsNullOrWhiteSpace(sourceFolder) ? "Unknown" : sourceFolder;
            var bag = _deletionsBySource.GetOrAdd(src, _ => new ConcurrentBag<string>());
            bag.Add(itemPath);
        }

        public void Dispose()
        {
            _currentScope.Value = _parentScope;

            if (_parentScope != null)
            {
                foreach (var kvp in _deletionsBySource)
                {
                    foreach (var path in kvp.Value)
                    {
                        _parentScope.RegisterDeletion(path, kvp.Key);
                    }
                }
                return;
            }

            foreach (var kvp in _deletionsBySource)
            {
                string sourceFolder = kvp.Key;
                List<string> paths = [.. kvp.Value];
                if (paths.Count == 0)
                    continue;

                string sourceFolderName = Path.GetFileName(sourceFolder);
                if (string.IsNullOrWhiteSpace(sourceFolderName))
                    sourceFolderName = sourceFolder;

                if (paths.Count == 1)
                {
                    string fileName = Path.GetFileName(paths[0]);
                    _notificationSender($"Deleted mod folder: {fileName}", "trashIcon.svg");
                }
                else
                {
                    _notificationSender($"Deleted {paths.Count} mod folders from {sourceFolderName}", "trashIcon.svg");
                }
            }
        }
    }
}
