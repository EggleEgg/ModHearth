using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.ComponentModel;

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

    public static readonly StyledProperty<bool> AllowColorEditingProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(AllowColorEditing), true);

    public bool AllowColorEditing
    {
        get => GetValue(AllowColorEditingProperty);
        set => SetValue(AllowColorEditingProperty, value);
    }

    public static readonly StyledProperty<bool> AllowSeparatorsProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(AllowSeparators), true);

    public bool AllowSeparators
    {
        get => GetValue(AllowSeparatorsProperty);
        set => SetValue(AllowSeparatorsProperty, value);
    }

    public static readonly StyledProperty<bool> AllowContextMenuProperty =
        AvaloniaProperty.Register<ModRefControl, bool>(nameof(AllowContextMenu), true);

    public bool AllowContextMenu
    {
        get => GetValue(AllowContextMenuProperty);
        set => SetValue(AllowContextMenuProperty, value);
    }

    public ModRefControl()
    {
        InitializeComponent();
        UpdateContextMenuState();
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
        else if (change.Property == AllowContextMenuProperty)
        {
            UpdateContextMenuState();
        }
    }

    private void UpdateContextMenuState()
    {
        if (!AllowContextMenu)
        {
            ContextMenu = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ModRefViewModel.RuleBadges):
            case nameof(ModRefViewModel.RelationshipCount):
                UpdateBadgeVisibilities();
                break;

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

        // The ContextMenu can be reassigned to a different owning control (ModUpdateLogWindow and
        // WorkshopDownloaderWindow both do this via a hidden ContextMenuHost), so its own DataContext
        // inheritance can't be relied on -- bind the Header content directly against the resolved vm.
        menu.DataContext = vm;

        IModRefContextMenuProvider? provider = this.FindAncestorOfType<IModRefContextMenuProvider>();

        // 1. Centralized preparation if a manager is available via the provider.
        // This deduplicates calls to ModContextMenuSupport.PrepareContextMenu across all windows.
        if (provider != null)
        {
            ModHearthManager? manager = provider.GetManager();
            if (manager != null)
            {
                var selected = provider.GetSelectedModReferences(vm);
                ModContextMenuSupport.PrepareContextMenu(menu, manager, vm.ModReference, selected);
            }
        }

        // 2. Let the ancestor window customize content (wording, steam-vs-local enable/disable, etc).
        // Instance-level flags below run last so they always have final say for THIS control.
        provider?.OnModRefContextMenuOpened(menu, vm);

        bool allowRelationships = AllowRelationshipEditing;
        bool allowActions = AllowContextActions;
        bool allowColor = AllowColorEditing;
        bool allowSeparators = AllowSeparators;
        bool anyVisible = false;

        foreach (Control item in menu.Items.OfType<Control>())
        {
            // Change checkbox state on right click
            if (item is MenuItem menuItem)
            {
                menuItem.PointerPressed -= OnContextMenuItemPointerPressed;
                menuItem.PointerPressed += OnContextMenuItemPointerPressed;
            }

            string? tag = (item as MenuItem)?.Tag?.ToString() ?? (item as Separator)?.Tag?.ToString();

            // Only ever suppress here, never re-enable. Preserves per-item decisions providers already made (e.g. hiding "Open Steam Page" for non-steam mods).
            if (IsRelationshipTag(tag))
            {
                if (!allowRelationships)
                    item.IsVisible = false;
                else if (string.Equals(tag, "relations-root", StringComparison.Ordinal) && item is MenuItem relationsRoot)
                    ModContextMenuSupport.ConfigureRelationsMenu(relationsRoot, vm);
            }
            else if (string.Equals(tag, "set-mod-color-root", StringComparison.Ordinal))
            {
                if (!allowColor)
                    item.IsVisible = false;
            }
            else if (item is Separator)
            {
                if (!allowSeparators)
                    item.IsVisible = false;
            }
            else if (!allowActions)
            {
                item.IsVisible = false;
            }

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
        Console.WriteLine($"[ModRefControl] OnContextMenuItemClick - Provider found: {provider != null}");
        if (provider != null)
        {
            provider.OnModRefContextMenuItemClicked(item, vm);
        }
    }

    private void OnContextMenuItemPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        if (!e.GetCurrentPoint(item).Properties.IsRightButtonPressed)
            return;

        var checkBox = item.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
        if (checkBox != null)
        {
            checkBox.IsChecked = !(checkBox.IsChecked ?? false);
            e.Handled = true;
        }
    }
}
