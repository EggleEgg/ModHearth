using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ModHearth.UI;

/// <summary>
/// Centralized class for modlist viewers
/// </summary>
public partial class ModRefControl : UserControl
{
    public static readonly StyledProperty<bool> ShowColorUnderlayProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(ShowColorUnderlay), true);

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

    public static readonly StyledProperty<bool> AllowContextActionsProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(AllowContextActions), true);

    public bool AllowContextActions
    {
        get => GetValue(AllowContextActionsProperty);
        set => SetValue(AllowContextActionsProperty, value);
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

    private static bool IsRelationshipTag(string? tag) => tag is not null &&
        (string.Equals(tag, "relations-root", StringComparison.Ordinal) ||
        tag.StartsWith("relation-", StringComparison.Ordinal));


    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (DataContext is not ModRefViewModel vm)
            return;

        // Let the ancestor window customize content first (wording, steam-vs-local enable/disable, etc).
        // Instance-level flags below run last so they always have final say for THIS control.
        IModRefContextMenuProvider? provider = this.FindAncestorOfType<IModRefContextMenuProvider>();
        provider?.OnModRefContextMenuOpened(menu, vm);

        bool allowRelationships = AllowRelationshipEditing;
        bool allowActions = AllowContextActions;
        bool anyVisible = false;

        foreach (Control item in menu.Items.OfType<Control>())
        {
            string? tag = (item as MenuItem)?.Tag?.ToString() ?? (item as Separator)?.Tag?.ToString();

            // Only ever suppress here, never re-enable. Preserves per-item decisions providers already made (e.g. hiding "Open Steam Page" for non-steam mods).
            if (IsRelationshipTag(tag) && !allowRelationships)
                item.IsVisible = false;
            else if (!IsRelationshipTag(tag) && !allowActions)
                item.IsVisible = false;

            if (item.IsVisible)
                anyVisible = true;
        }

        if (!anyVisible)
        {
            menu.Close();
            e.Handled = true;
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
