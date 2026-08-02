using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModHearth.UI;

/// <summary>
/// ObservableCollection variant with a bulk-replace operation that raises a single
/// CollectionChanged notification instead of one per item. Populating a DataGrid-bound collection
/// item-by-item (the default Add() behavior) means one full incremental view-update pass per item,
/// which dominates population time once you're past a few dozen rows.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces the entire contents with a single CollectionChanged notification, instead of one
    /// Reset from Clear() followed by one per added item. Also required for correctness, not just
    /// speed: Avalonia's DataGrid (as of Avalonia.Controls.DataGrid 12.0.1) doesn't reliably pick
    /// up a single notification whose NewItems contains more than one item -- see
    /// AvaloniaUI/Avalonia#3510 and #8970 -- so a batched Add notification isn't a safe substitute
    /// for Reset here even setting performance aside.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (T item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}