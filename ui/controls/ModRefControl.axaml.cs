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
        ContextMenu = GetSharedContextMenu();
        UpdateContextMenuState();
    }

    // Resolved once and reused for the app's lifetime. Application.Resources is populated exactly once at startup, so this is genuinely one object shared by every ModRefControl ever constructed.
    private static ContextMenu? sharedContextMenu;

    private static ContextMenu GetSharedContextMenu()
    {
        sharedContextMenu ??= (ContextMenu)Application.Current!.Resources["ModRefContextMenu"]!;
        return sharedContextMenu;
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

    internal static bool IsRelationshipTag(string? tag) => tag is not null &&
        (string.Equals(tag, "relations-root", StringComparison.Ordinal) ||
        tag.StartsWith("relation-", StringComparison.Ordinal));

}
