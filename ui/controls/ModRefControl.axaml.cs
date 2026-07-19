using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ModHearth.UI;

public partial class ModRefControl : UserControl
{
    public static readonly StyledProperty<bool> ShowColorUnderlayProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(ShowColorUnderlay), false);

    public bool ShowColorUnderlay
    {
        get => GetValue(ShowColorUnderlayProperty);
        set => SetValue(ShowColorUnderlayProperty, value);
    }

    public static readonly StyledProperty<bool> ShowDetailedRuleBadgesProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(ShowDetailedRuleBadges), false);

    public bool ShowDetailedRuleBadges
    {
        get => GetValue(ShowDetailedRuleBadgesProperty);
        set => SetValue(ShowDetailedRuleBadgesProperty, value);
    }

    public static readonly StyledProperty<bool> AllowRelationshipEditingProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(AllowRelationshipEditing), false);

    public bool AllowRelationshipEditing
    {
        get => GetValue(AllowRelationshipEditingProperty);
        set => SetValue(AllowRelationshipEditingProperty, value);
    }

    public static readonly StyledProperty<IBrush> HostBackgroundProperty =
        AvaloniaProperty.Register<ModRefControl, IBrush>(nameof(HostBackground), Brushes.Transparent);

    public IBrush HostBackground
    {
        get => GetValue(HostBackgroundProperty);
        set => SetValue(HostBackgroundProperty, value);
    }

    public ModRefControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            if (change.OldValue is ModRefViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (change.NewValue is ModRefViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
            }
            UpdateBadgeVisibilities();
        }
        else if (change.Property == ShowDetailedRuleBadgesProperty)
        {
            UpdateBadgeVisibilities();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModRefViewModel.RuleBadges) ||
            e.PropertyName == nameof(ModRefViewModel.RelationshipCount))
        {
            UpdateBadgeVisibilities();
        }
    }

    private void UpdateBadgeVisibilities()
    {
        if (DataContext is not ModRefViewModel vm)
            return;

        bool showDetailed = ShowDetailedRuleBadges;
        var detailed = this.FindControl<Control>("DetailedBadgesControl");
        var simple = this.FindControl<Control>("SimpleBadgeControl");

        if (detailed != null)
        {
            detailed.IsVisible = showDetailed && vm.HasRuleBadges;
        }
        if (simple != null)
        {
            simple.IsVisible = !showDetailed && vm.HasRelationships;
        }
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (DataContext is not ModRefViewModel vm)
            return;

        // Apply visibility based on AllowRelationshipEditing before provider runs
        bool allowEditing = AllowRelationshipEditing;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            string? tag = item.Tag?.ToString();
            if (string.Equals(tag, "relations-root", StringComparison.Ordinal) ||
                string.Equals(tag, "add-required-root", StringComparison.Ordinal) ||
                tag?.StartsWith("relation-", StringComparison.Ordinal) == true)
            {
                item.IsVisible = allowEditing;
            }
        }

        IModRefContextMenuProvider? provider = this.FindAncestorOfType<IModRefContextMenuProvider>();
        if (provider != null)
        {
            provider.OnModRefContextMenuOpened(menu, vm);
        }
    }

    private void OnContextMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        if (DataContext is not ModRefViewModel vm)
            return;

        IModRefContextMenuProvider? provider = this.FindAncestorOfType<IModRefContextMenuProvider>();
        if (provider != null)
        {
            provider.OnModRefContextMenuItemClicked(item, vm);
        }
    }
}
