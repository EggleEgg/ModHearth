using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public ModRefControl()
    {
        InitializeComponent();
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (DataContext is not ModRefViewModel vm)
            return;

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
