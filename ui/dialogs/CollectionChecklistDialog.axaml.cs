using Avalonia.Controls;
using Avalonia.Media;
using ModHearth.UI.Theme;
using ModHearth.Utils;
using ModHearth.Utils.Workshop;

namespace ModHearth.UI
{
    /// <summary>
    /// Handles downloading steam workshop collections
    /// </summary>
    public class CollectionChecklistItem : ObservableObject
    {
        public WorkshopItemMetadata Metadata { get; set; } = null!;
        public string Title => Metadata.Title;
        public ModStatusClassification Classification { get; set; }

        public bool IsChecked
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        } = true;

        public string ClassificationText => Classification switch
        {
            ModStatusClassification.AlreadyInstalled => "Installed",
            ModStatusClassification.UpdateAvailable => "Update Available",
            ModStatusClassification.New => "New Mod",
            ModStatusClassification.Duplicate => "Duplicate",
            ModStatusClassification.MissingDependency => "Missing Dep",
            ModStatusClassification.PotentiallyIncompatible => "Incompatible",
            _ => "Unknown"
        };

        public IBrush ClassificationBrush => Classification switch
        {
            ModStatusClassification.AlreadyInstalled => Brushes.Gray,
            ModStatusClassification.UpdateAvailable => Brushes.DarkOrange,
            ModStatusClassification.New => Brushes.Green,
            ModStatusClassification.Duplicate => Brushes.Purple,
            ModStatusClassification.MissingDependency => Brushes.OrangeRed,
            ModStatusClassification.PotentiallyIncompatible => Brushes.Red,
            _ => Brushes.Gray
        };
    }

    public partial class CollectionChecklistDialog : Window
    {
        private List<CollectionChecklistItem> _items = [];

        public CollectionChecklistDialog()
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            BtnDownloadAll.Click += (_, _) => SetAllChecked(true);
            BtnClearAll.Click += (_, _) => SetAllChecked(false);
            BtnDownloadMissing.Click += (_, _) => SetMissingChecked();
            BtnUpdateExisting.Click += (_, _) => SetUpdateChecked();

            BtnConfirm.Click += (_, _) => Close(_items.Where(i => i.IsChecked).Select(i => i.Metadata).ToList());
            BtnCancel.Click += (_, _) => Close(null);
        }

        public static async Task<List<WorkshopItemMetadata>?> ShowAsync(
            Window owner,
            List<WorkshopItemMetadata> childrenMetadata,
            WorkshopQueueManager queueManager)
        {
            CollectionChecklistDialog dialog = new()
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            List<ulong> allIds = [.. childrenMetadata.Select(c => c.PublishedFileId)];
            List<CollectionChecklistItem> checklistItems = [.. childrenMetadata.Select(meta => new CollectionChecklistItem
            {
                Metadata = meta,
                Classification = queueManager.ClassifyModWithMetadata(meta, allIds)
            })];

            dialog._items = checklistItems;
            dialog.ItemsList.ItemsSource = checklistItems;
            dialog.SetMissingAndUpdatesChecked();

            return await dialog.ShowDialog<List<WorkshopItemMetadata>?>(owner);
        }

        private void SetChecked(Func<CollectionChecklistItem, bool> predicate)
        {
            foreach (CollectionChecklistItem item in _items)
                item.IsChecked = predicate(item);
        }

        private void SetMissingAndUpdatesChecked()
            => SetChecked(item => item.Classification is ModStatusClassification.New or
                                  ModStatusClassification.MissingDependency or
                                  ModStatusClassification.UpdateAvailable);

        private void SetAllChecked(bool value) => SetChecked(_ => value);

        private void SetMissingChecked()
            => SetChecked(item => item.Classification is ModStatusClassification.New or
                                  ModStatusClassification.MissingDependency);

        private void SetUpdateChecked()
            => SetChecked(item => item.Classification == ModStatusClassification.UpdateAvailable);
    }
}
