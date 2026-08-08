using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ModHearth.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            if (IsSmokeTestWindowEnabled())
            {
                var window = desktop.MainWindow;
                window.Opened += (_, _) =>
                {
                    Dispatcher.UIThread.Post(() => window.Close());
                };

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (window.IsVisible)
                        window.Close();
                };
                timer.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();

        if (DevMode.IsEnabled)
            _ = this.AttachDeveloperTools();
    }

    private static bool IsSmokeTestWindowEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW"), "1", StringComparison.OrdinalIgnoreCase);

    // ── Shared ModRefContextMenu handling ──────────────────────────────────────────
    //
    // ModRefControl used to declare its own <UserControl.ContextMenu> inline, so every row a virtualizing ListBox realized for the first time during a scroll paid the full construction
    // cost of a ~30-control menu tree. That now happens once for a single instance shared by every ModRefControl in the app (the "ModRefContextMenu" resource above, assigned in
    // ModRefControl's constructor). Because one object now serves every row, these handlers can't rely on an owning instance's `this` -- ownership is resolved from the event instead and
    // cached for the duration of the open, since Click fires on a MenuItem inside the popup, not on the control the menu was placed against.
    private ModRefControl? currentContextMenuRow;
    private IModRefContextMenuProvider? currentContextMenuProvider;

    internal ModRefViewModel? GetCurrentContextMenuVm() => currentContextMenuRow?.DataContext as ModRefViewModel;

    private void OnModRefContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        (ModRefControl? rowControl, IModRefContextMenuProvider? provider) = ResolveContextMenuOwner(menu.PlacementTarget as Control);
        currentContextMenuRow = rowControl;
        currentContextMenuProvider = provider;

        if (rowControl?.DataContext is not ModRefViewModel vm)
            return;

        // The ContextMenu can be reassigned to a different owning control (ModUpdateLogWindow and WorkshopDownloaderWindow both do this via a hidden ContextMenuHost), 
        // so its own DataContext  inheritance can't be relied on -- bind the Header content directly against the resolved vm.
        menu.DataContext = vm;

        if (provider != null)
        {
            ModHearthManager? manager = provider.GetManager();
            if (manager != null)
            {
                var selected = provider.GetSelectedModReferences(vm);
                ModContextMenuSupport.PrepareContextMenu(menu, manager, vm.ModReference, selected);
            }
        }

        provider?.OnModRefContextMenuOpened(menu, vm);

        bool allowRelationships = rowControl.AllowRelationshipEditing;
        bool allowActions = rowControl.AllowContextActions;
        bool allowColor = rowControl.AllowColorEditing;
        bool allowSeparators = rowControl.AllowSeparators;
        bool anyVisible = false;

        foreach (Control item in menu.Items.OfType<Control>())
        {
            // Change checkbox state on right click
            if (item is MenuItem menuItem)
            {
                menuItem.PointerPressed -= OnModRefContextMenuItemPointerPressed;
                menuItem.PointerPressed += OnModRefContextMenuItemPointerPressed;
            }

            string? tag = (item as MenuItem)?.Tag?.ToString() ?? (item as Separator)?.Tag?.ToString();

            // Only ever suppress here, never re-enable. Preserves per-item decisions providers already made (e.g. hiding "Open Steam Page" for non-steam mods).
            if (ModRefControl.IsRelationshipTag(tag))
            {
                item.IsVisible = allowRelationships;
                if (allowRelationships && string.Equals(tag, "relations-root", StringComparison.Ordinal) && item is MenuItem relationsRoot)
                    ModContextMenuSupport.ConfigureRelationsMenu(relationsRoot, vm);
            }
            else if (string.Equals(tag, "set-mod-color-root", StringComparison.Ordinal))
                item.IsVisible = allowColor;
            else if (item is Separator)
                item.IsVisible = allowSeparators;
            else if (!allowActions)
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

    private void OnModRefContextMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;
        if (currentContextMenuRow?.DataContext is not ModRefViewModel vm)
            return;

        Console.WriteLine($"[ModRefControl] OnContextMenuItemClick - Provider found: {currentContextMenuProvider != null}");
        currentContextMenuProvider?.OnModRefContextMenuItemClicked(item, vm);
    }

    private void OnModRefContextMenuItemPointerPressed(object? sender, PointerPressedEventArgs e)
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

    // Resolves the ModRefControl (for DataContext + Allow* flags) and the enclosing IModRefContextMenuProvider that should govern the current open, given the control the shared
    // menu was actually invoked against. For a real per-row usage (MainWindow's lists, SortRulesControl's tree) placementTarget IS the ModRefControl. For providers that reassign
    // this same shared menu onto a DataGrid/ListBox via a hidden row (ModUpdateLogControl, WorkshopDownloaderControl), placementTarget is that DataGrid/ListBox instead, so the
    // governing ModRefControl is fetched from the provider explicitly.
    private static (ModRefControl? RowControl, IModRefContextMenuProvider? Provider) ResolveContextMenuOwner(Control? placementTarget)
    {
        if (placementTarget == null)
            return (null, null);

        IModRefContextMenuProvider? provider = placementTarget.FindAncestorOfType<IModRefContextMenuProvider>();
        ModRefControl? rowControl = placementTarget as ModRefControl ?? provider?.GetContextMenuHost();
        return (rowControl, provider);
    }
}