using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Core;
using DockOrientation = Dock.Model.Core.Orientation;
using System.Collections.Specialized;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Controls.ProportionalStackPanel;

namespace ModHearth.UI;

public partial class MainWindow
{
    private const string ModDataDockId = "ModDataDock";
    private const string DescriptionDockId = "DescriptionDock";
    private const string ModPreviewDockId = "ModPreviewDock";
    private const string ModInfoLayoutId = "ModInfoLayout";
    private const string ModInfoOuterLayoutId = "ModInfoOuterLayout";

    private readonly ModPreviewPanelViewModel modPreviewPanelViewModel = new();
    private readonly ModDataPanelViewModel modDataPanelViewModel = new();
    private readonly ModDescriptionPanelViewModel modDescriptionPanelViewModel = new();

    private Tool? modPreviewTool;
    private Tool? modDataTool;
    private Tool? descriptionTool;
    private readonly HashSet<ToolDock> emptyToolDocks = new();
    private readonly HashSet<object> knownSplitters = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ToolDock, double> reclaimedToolDockProportions = new();
    private const double CollapsedToolDockProportion = 0.000001; //slightly sketchy but it works
    private ModReference? currentModDataModRef;
    private readonly ModInfoDockFactory factory = new();

    private sealed class ModInfoDockFactory : Factory
    {
        public override void InitLayout(IDockable layout)
        {
            HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
            {
                [nameof(IDockWindow)] = () => new HostWindow()
            };
            base.InitLayout(layout);
        }
    }

    private void InitializeModInfoDock()
    {
        modPreviewPanelViewModel.Id = "ModPreviewTool";
        modPreviewPanelViewModel.Title = "Preview";
        modPreviewPanelViewModel.CanClose = false;
        modPreviewPanelViewModel.CanFloat = false;
        modPreviewPanelViewModel.CanPin = true;
        modPreviewPanelViewModel.CanDrag = true;

        modDataPanelViewModel.Id = "ModDataTool";
        modDataPanelViewModel.Title = "Mod Data";
        modDataPanelViewModel.CanClose = false;
        modDataPanelViewModel.CanFloat = false;
        modDataPanelViewModel.CanPin = true;
        modDataPanelViewModel.CanDrag = true;

        modDescriptionPanelViewModel.Id = "DescriptionTool";
        modDescriptionPanelViewModel.Title = "Description";
        modDescriptionPanelViewModel.CanClose = false;
        modDescriptionPanelViewModel.CanFloat = false;
        modDescriptionPanelViewModel.CanPin = true;
        modDescriptionPanelViewModel.CanDrag = true;

        modPreviewTool = modPreviewPanelViewModel;
        modDataTool = modDataPanelViewModel;
        descriptionTool = modDescriptionPanelViewModel;

        double modDataProportion = ClampProportion(ConfigManager.GetModDataPanelProportion(), 0.35);
        double descriptionProportion = ClampProportion(ConfigManager.GetModDescriptionPanelProportion(), 1 - modDataProportion);
        NormalizeProportions(ref modDataProportion, ref descriptionProportion);

        DockOrientation innerOrientation = ConfigManager.GetModDataPanelOrientation() == 1
            ? DockOrientation.Horizontal
            : DockOrientation.Vertical;
        bool modDataFirst = ConfigManager.GetModDataPanelFirst();

        double previewProportion = ClampProportion(ConfigManager.GetModPreviewPanelProportion(), 0.45);
        double infoPanelProportion = ClampProportion(ConfigManager.GetModInfoPanelProportion(), 1 - previewProportion);
        NormalizeProportions(ref infoPanelProportion, ref previewProportion);

        DockOrientation previewOrientation = ConfigManager.GetModPreviewPanelOrientation() == 1
            ? DockOrientation.Horizontal
            : DockOrientation.Vertical;
        bool previewFirst = ConfigManager.GetModPreviewPanelFirst();

        ToolDock modDataDock = new ToolDock
        {
            Id = ModDataDockId,
            Proportion = modDataProportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(modDataTool),
            ActiveDockable = modDataTool,
            CanClose = false,
            CanFloat = false,
            CanDrag = true,
            IsCollapsable = false
        };
        ToolDock descriptionDock = new ToolDock
        {
            Id = DescriptionDockId,
            Proportion = descriptionProportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(descriptionTool),
            ActiveDockable = descriptionTool,
            CanClose = false,
            CanFloat = false,
            CanDrag = true,
            IsCollapsable = false
        };

        ToolDock previewDock = new ToolDock
        {
            Id = ModPreviewDockId,
            Proportion = previewProportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(modPreviewTool),
            ActiveDockable = modPreviewTool,
            CanClose = false,
            CanFloat = false,
            CanDrag = true,
            IsCollapsable = false
        };

        ProportionalDockSplitter splitter = new ProportionalDockSplitter
        {
            Id = "ModInfoSplitter"
        };
        _ = knownSplitters.Add(splitter);

        ProportionalDock layoutDock = new ProportionalDock
        {
            Id = ModInfoLayoutId,
            Orientation = innerOrientation,
            IsCollapsable = false,
            Proportion = infoPanelProportion,
            VisibleDockables = modDataFirst
                ? factory.CreateList<IDockable>(modDataDock, splitter, descriptionDock)
                : factory.CreateList<IDockable>(descriptionDock, splitter, modDataDock)
        };

        ProportionalDockSplitter previewSplitter = new ProportionalDockSplitter
        {
            Id = "ModPreviewSplitter"
        };
        _ = knownSplitters.Add(previewSplitter);


        ProportionalDock outerLayoutDock = new ProportionalDock
        {
            Id = ModInfoOuterLayoutId,
            Orientation = previewOrientation,
            IsCollapsable = false,
            VisibleDockables = previewFirst
                ? factory.CreateList<IDockable>(previewDock, previewSplitter, layoutDock)
                : factory.CreateList<IDockable>(layoutDock, previewSplitter, previewDock)
        };

        RootDock root = new RootDock
        {
            Id = "ModInfoRoot",
            VisibleDockables = factory.CreateList<IDockable>(outerLayoutDock),
            ActiveDockable = outerLayoutDock,
            DefaultDockable = outerLayoutDock,
        };

        factory.InitLayout(root);
        modInfoDockControl.Factory = factory;
        modInfoDockControl.Layout = root;

        HookEmptyDockRebalance(modDataDock);
        HookEmptyDockRebalance(descriptionDock);
        HookEmptyDockRebalance(previewDock);

        PopulateModDataViewer(null);
    }

    private void SaveModInfoDockLayout()
    {
        if (modInfoDockControl.Layout is not IRootDock root)
            return;

        double modDataProportion = ConfigManager.GetModDataPanelProportion();
        int modDataOrientation = ConfigManager.GetModDataPanelOrientation();
        bool modDataFirst = ConfigManager.GetModDataPanelFirst();
        double descriptionProportion = ConfigManager.GetModDescriptionPanelProportion();
        double infoPanelProportion = ConfigManager.GetModInfoPanelProportion();
        double previewProportion = ConfigManager.GetModPreviewPanelProportion();
        int previewOrientation = ConfigManager.GetModPreviewPanelOrientation();
        bool previewFirst = ConfigManager.GetModPreviewPanelFirst();

        ToolDock? modDataDock = FindDockableById(root, ModDataDockId) as ToolDock;
        ToolDock? descriptionDock = FindDockableById(root, DescriptionDockId) as ToolDock;
        ToolDock? previewDock = FindDockableById(root, ModPreviewDockId) as ToolDock;
        ProportionalDock? innerLayout = FindDockableById(root, ModInfoLayoutId) as ProportionalDock;
        ProportionalDock? outerLayout = FindDockableById(root, ModInfoOuterLayoutId) as ProportionalDock;

        if (modDataDock != null
            && descriptionDock != null
            && innerLayout?.VisibleDockables != null
            && modDataTool?.Owner == modDataDock
            && descriptionTool?.Owner == descriptionDock)
        {
            modDataProportion = ClampProportion(modDataDock.Proportion, modDataProportion);
            descriptionProportion = ClampProportion(descriptionDock.Proportion, descriptionProportion);
            modDataOrientation = innerLayout.Orientation == DockOrientation.Horizontal ? 1 : 0;

            int modDataIndex = innerLayout.VisibleDockables.IndexOf(modDataDock);
            int descriptionIndex = innerLayout.VisibleDockables.IndexOf(descriptionDock);
            if (modDataIndex >= 0 && descriptionIndex >= 0)
                modDataFirst = modDataIndex < descriptionIndex;
        }

        if (innerLayout != null)
            infoPanelProportion = ClampProportion(innerLayout.Proportion, infoPanelProportion);

        if (previewDock != null
            && outerLayout?.VisibleDockables != null
            && modPreviewTool?.Owner == previewDock)
        {
            previewProportion = ClampProportion(previewDock.Proportion, previewProportion);
            previewOrientation = outerLayout.Orientation == DockOrientation.Horizontal ? 1 : 0;

            int previewIndex = outerLayout.VisibleDockables.IndexOf(previewDock);
            int innerLayoutIndex = innerLayout != null
                ? outerLayout.VisibleDockables.IndexOf(innerLayout)
                : -1;
            if (previewIndex >= 0 && innerLayoutIndex >= 0)
                previewFirst = previewIndex < innerLayoutIndex;
        }

        ConfigManager.SetModInfoDockLayout(
            modDataProportion,
            modDataOrientation,
            modDataFirst,
            descriptionProportion,
            infoPanelProportion,
            previewProportion,
            previewOrientation,
            previewFirst);
    }

    private void HookEmptyDockRebalance(ToolDock dock)
    {
        switch (dock.VisibleDockables)
        {
            case INotifyCollectionChanged notifyCollection:
                {
                    notifyCollection.CollectionChanged += (_, _) =>
                    {
                        if (DevMode.IsEnabled)
                            Console.WriteLine($"[DockRebalance] CollectionChanged fired for '{dock.Id}', Count={dock.VisibleDockables?.Count ?? -1}");
                        RebalanceEmptyToolDock(dock);
                    };
                    break;
                }

            default:
                if (DevMode.IsEnabled)
                    Console.WriteLine($"[DockRebalance] '{dock.Id}'.VisibleDockables is NOT INotifyCollectionChanged (actual type: {dock.VisibleDockables?.GetType().FullName ?? "null"})");

                break;
        }
    }

    private void ApplyProportionToVisualTree(IDock dock, double proportion, bool isCollapsed)
    {
        Control? container = modInfoDockControl.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.DataContext is IDock d && ReferenceEquals(d, dock));

        if (container == null)
        {
            if (DevMode.IsEnabled)
                Console.WriteLine($"[DockRebalance] '{dock.Id}' -> no matching container found in visual tree.");
            return;
        }

        Control? target = container;
        while (target != null && target.GetVisualParent() is not ProportionalStackPanel)
            target = target.GetVisualParent() as Control;

        if (target == null)
        {
            if (DevMode.IsEnabled)
                Console.WriteLine($"[DockRebalance] '{dock.Id}' -> found DataContext container but no ProportionalStackPanel ancestor.");
            return;
        }

        ProportionalStackPanel.SetProportion(target, proportion);
        ProportionalStackPanel.SetIsCollapsed(target, isCollapsed);
        if (isCollapsed)
        {
            ProportionalStackPanel.SetCollapsedProportion(target, CollapsedToolDockProportion);
            ProportionalStackPanelSplitter.SetMinimumProportionSize(target, 0);
        }
        else
        {
            // Restore the library's real default rather than hardcoding 75 here, in case that default
            // ever changes upstream. Normal, non-collapsed panes should keep their real interactive-drag
            // floor -- only a genuinely empty/collapsed pane should be exempt from it.
            target.ClearValue(ProportionalStackPanelSplitter.MinimumProportionSizeProperty);
        }
    }

    private void RebalanceEmptyToolDock(ToolDock dock)
    {
        bool isEmptyNow = dock.VisibleDockables == null || dock.VisibleDockables.Count == 0;
        bool wasEmpty = emptyToolDocks.Contains(dock);
        if (DevMode.IsEnabled)
            Console.WriteLine($"[DockRebalance] '{dock.Id}' isEmptyNow={isEmptyNow} wasEmpty={wasEmpty} Proportion={dock.Proportion} Owner={dock.Owner?.Id ?? "null"} OwnerType={dock.Owner?.GetType().Name ?? "null"}");

        if (isEmptyNow == wasEmpty)
            return;

        if (dock.Owner is not IDock parent || parent.VisibleDockables == null)
        {
            if (DevMode.IsEnabled)
                Console.WriteLine($"[DockRebalance] '{dock.Id}' bailing: Owner not usable.");
            return;
        }

        List<IDock> siblings = parent.VisibleDockables
            .Where(d => !ReferenceEquals(d, dock) && d is IDock && !knownSplitters.Contains(d))
            .Cast<IDock>()
            .ToList();
        if (DevMode.IsEnabled)
            Console.WriteLine($"[DockRebalance] '{dock.Id}' siblings=[{string.Join(", ", siblings.Select(s => $"{s.Id}:{s.Proportion:F3}"))}]");
        if (siblings.Count == 0)
            return;

        if (isEmptyNow)
        {
            reclaimedToolDockProportions[dock] = dock.Proportion;
            double freed = dock.Proportion - CollapsedToolDockProportion;
            if (freed > 0)
            {
                double siblingTotal = siblings.Sum(s => s.Proportion);
                foreach (IDock sibling in siblings)
                {
                    double share = siblingTotal > 0 ? sibling.Proportion / siblingTotal : 1.0 / siblings.Count;
                    sibling.Proportion += freed * share;
                }
                dock.Proportion = CollapsedToolDockProportion;
                if (DevMode.IsEnabled) Console.WriteLine($"[DockRebalance] '{dock.Id}' -> Proportion now {dock.Proportion:F3}");
            }
            _ = emptyToolDocks.Add(dock);
        }
        else
        {
            double target = reclaimedToolDockProportions.TryGetValue(dock, out double remembered) ? remembered : 0.3;
            double needed = target - dock.Proportion;
            double siblingTotal = siblings.Sum(s => s.Proportion);
            if (needed > 0 && siblingTotal > needed)
            {
                foreach (IDock sibling in siblings)
                {
                    double share = sibling.Proportion / siblingTotal;
                    sibling.Proportion -= needed * share;
                }
                dock.Proportion = target;
                if (DevMode.IsEnabled) Console.WriteLine($"[DockRebalance] '{dock.Id}' -> Proportion now {dock.Proportion:F3}");
            }
            _ = emptyToolDocks.Remove(dock);
            _ = reclaimedToolDockProportions.Remove(dock);
        }

        //NOTE Use avalonia devtools and look into the value of "ContentPresenter" to debug proportion issues
        Dispatcher.UIThread.Post(() =>
        {
            ApplyProportionToVisualTree(dock, dock.Proportion, isCollapsed: isEmptyNow);
            foreach (IDock sibling in siblings)
                ApplyProportionToVisualTree(sibling, sibling.Proportion, isCollapsed: false);

            if (DevMode.IsEnabled)
                Console.WriteLine($"[DockRebalance] '{dock.Id}' applied Proportion+IsCollapsed to visual tree (collapsed={isEmptyNow}).");
        }, DispatcherPriority.Background);
    }

    private static IDockable? FindDockableById(IDockable dockable, string id)
    {
        if (string.Equals(dockable.Id, id, StringComparison.Ordinal))
            return dockable;

        if (dockable is not IDock dock || dock.VisibleDockables == null)
            return null;

        foreach (IDockable child in dock.VisibleDockables)
        {
            IDockable? found = FindDockableById(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static double ClampProportion(double proportion, double fallback)
    {
        if (double.IsNaN(proportion) || proportion < 0.05 || proportion > 0.95)
            return fallback;
        return proportion;
    }

    private static void NormalizeProportions(ref double first, ref double second)
    {
        if (double.IsNaN(first) || double.IsNaN(second))
            return;

        double sum = first + second;
        if (sum <= 0)
            return;

        first /= sum;
        second /= sum;
    }

    private void PopulateModDataViewer(ModReference? modref)
    {
        currentModDataModRef = modref;
        modDataPanelViewModel.Entries.Clear();

        if (modref == null)
        {
            modDataPanelViewModel.HasSelection = false;
            return;
        }

        modDataPanelViewModel.HasSelection = true;
        foreach ((string label, string value) in GetModDataEntries(modref))
            modDataPanelViewModel.Entries.Add(new ModDataEntryViewModel(label, value));
    }

    private static IEnumerable<(string label, string value)> GetModDataEntries(ModReference modref)
    {
        (string, string?)[] entries =
        [
            ("id", modref.ID),
            ("name", modref.name),
            ("author", modref.author),
            ("numericVersion", modref.numericVersion),
            ("displayedVersion", modref.displayedVersion),
            ("earliestCompatibleNumericVersion", modref.earliestCompatibleNumericVersion),
            ("earliestCompatibleDisplayedVersion", modref.earliestCompatibleDisplayedVersion),
            ("steamName", modref.steamName),
            ("steamID", modref.steamID),
            ("source", modref.Source.ToString()),
            ("path", modref.path),
            ("requiresIds", JoinList(modref.require_ids)),
            ("requireBeforeMe", JoinList(modref.require_before_me)),
            ("requireAfterMe", JoinList(modref.require_after_me)),
            ("conflictsWith", JoinList(modref.conflicts_with)),
        ];

        foreach ((string label, string? value) in entries)
        {
            if (!string.IsNullOrWhiteSpace(value))
                yield return (label, value);
        }
    }

    private static string? JoinList(List<string>? values)
        => values == null || values.Count == 0 ? null : string.Join(", ", values);

    private void RefreshModDataViewer()
    {
        PopulateModDataViewer(currentModDataModRef);
    }
}
