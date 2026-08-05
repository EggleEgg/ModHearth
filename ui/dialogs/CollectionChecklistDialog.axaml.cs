using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    /// <summary>
    /// Handles downloading steam workshop collections
    /// </summary>
    public class CollectionChecklistItem : INotifyPropertyChanged
    {
        private bool _isChecked = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public WorkshopItemMetadata Metadata { get; set; } = null!;
        public string Title => Metadata.Title;
        public ModStatusClassification Classification { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }

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
            var dialog = new CollectionChecklistDialog
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var allIds = childrenMetadata.Select(c => c.PublishedFileId).ToList();
            var checklistItems = childrenMetadata.Select(meta => new CollectionChecklistItem
            {
                Metadata = meta,
                Classification = queueManager.ClassifyModWithMetadata(meta, allIds)
            }).ToList();

            dialog._items = checklistItems;
            dialog.ItemsList.ItemsSource = checklistItems;
            dialog.SetMissingAndUpdatesChecked();

            return await dialog.ShowDialog<List<WorkshopItemMetadata>?>(owner);
        }

        private void SetMissingAndUpdatesChecked()
        {
            foreach (var item in _items)
            {
                item.IsChecked = item.Classification == ModStatusClassification.New || 
                                 item.Classification == ModStatusClassification.MissingDependency ||
                                 item.Classification == ModStatusClassification.UpdateAvailable;
            }
        }

        private void SetAllChecked(bool value)
        {
            foreach (var item in _items)
            {
                item.IsChecked = value;
            }
        }

        private void SetMissingChecked()
        {
            foreach (var item in _items)
            {
                item.IsChecked = item.Classification == ModStatusClassification.New || 
                                 item.Classification == ModStatusClassification.MissingDependency;
            }
        }

        private void SetUpdateChecked()
        {
            foreach (var item in _items)
            {
                item.IsChecked = item.Classification == ModStatusClassification.UpdateAvailable;
            }
        }
    }
}
