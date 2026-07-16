using Avalonia;
using Avalonia.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Core;
using DockOrientation = Dock.Model.Core.Orientation;

namespace ModHearth.UI;

public partial class MainWindow
{
    private readonly ModPreviewPanelViewModel modPreviewPanelViewModel = new();
    private readonly ModDataPanelViewModel modDataPanelViewModel = new();
    private readonly ModDescriptionPanelViewModel modDescriptionPanelViewModel = new();

    private Tool? modPreviewTool;
    private Tool? modDataTool;
    private Tool? descriptionTool;
    private ModReference? currentModDataModRef;
    private class ModInfoDockFactory : Factory { }
    ModInfoDockFactory factory = new ModInfoDockFactory();
    private void InitializeModInfoDock()
    {
        // Configure properties directly on your panel view models since they are now Tools!
        modPreviewPanelViewModel.Id = "ModPreviewTool";
        modPreviewPanelViewModel.Title = "Preview";
        modPreviewPanelViewModel.CanClose = true;
        modPreviewPanelViewModel.CanFloat = true;
        modPreviewPanelViewModel.CanPin = true;
        modPreviewPanelViewModel.CanDrag = true;

        modDataPanelViewModel.Id = "ModDataTool";
        modDataPanelViewModel.Title = "Mod Data";
        modDataPanelViewModel.CanClose = true;
        modDataPanelViewModel.CanFloat = true;
        modDataPanelViewModel.CanPin = true;
        modDataPanelViewModel.CanDrag = true;

        modDescriptionPanelViewModel.Id = "DescriptionTool";
        modDescriptionPanelViewModel.Title = "Description";
        modDescriptionPanelViewModel.CanClose = true;
        modDescriptionPanelViewModel.CanFloat = true;
        modDescriptionPanelViewModel.CanPin = true;
        modDescriptionPanelViewModel.CanDrag = true;

        modPreviewTool = modPreviewPanelViewModel;
        modDataTool = modDataPanelViewModel;
        descriptionTool = modDescriptionPanelViewModel;

        double proportion = ConfigManager.GetModDataPanelProportion();
        if (double.IsNaN(proportion) || proportion < 0.05 || proportion > 0.95)
            proportion = 0.35;
        DockOrientation orientation = ConfigManager.GetModDataPanelOrientation() == 1
            ? DockOrientation.Horizontal
            : DockOrientation.Vertical;
        bool modDataFirst = ConfigManager.GetModDataPanelFirst();

        // The parent dock group
        ToolDock modDataDock = new ToolDock
        {
            Id = "ModDataDock",
            Proportion = proportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(modDataTool),
            ActiveDockable = modDataTool,
            CanClose = false,
            CanFloat = false
        };
        ToolDock descriptionDock = new ToolDock
        {
            Id = "DescriptionDock",
            Proportion = 1 - proportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(descriptionTool),
            ActiveDockable = descriptionTool,
            CanClose = false,
            CanFloat = false,
            CanDrag = false
        };
        ProportionalDockSplitter splitter = new ProportionalDockSplitter
        {
            Id = "ModInfoSplitter"
        };
        ProportionalDock layoutDock = new ProportionalDock
        {
            Id = "ModInfoLayout",
            Orientation = orientation,
            VisibleDockables = modDataFirst
                ? factory.CreateList<IDockable>(modDataDock, splitter, descriptionDock)
                : factory.CreateList<IDockable>(descriptionDock, splitter, modDataDock)
        };

        double previewProportion = ConfigManager.GetModPreviewPanelProportion();
        if (double.IsNaN(previewProportion) || previewProportion < 0.05 || previewProportion > 0.95)
            previewProportion = 0.45;
        DockOrientation previewOrientation = ConfigManager.GetModPreviewPanelOrientation() == 1
            ? DockOrientation.Horizontal
            : DockOrientation.Vertical;
        bool previewFirst = ConfigManager.GetModPreviewPanelFirst();

        ToolDock previewDock = new ToolDock
        {
            Id = "ModPreviewDock",
            Proportion = previewProportion,
            Alignment = Alignment.Top,
            GripMode = GripMode.Visible,
            VisibleDockables = factory.CreateList<IDockable>(modPreviewTool),
            ActiveDockable = modPreviewTool,
            CanClose = false,
            CanFloat = false
        };
        layoutDock.Proportion = 1 - previewProportion;
        ProportionalDockSplitter previewSplitter = new ProportionalDockSplitter
        {
            Id = "ModPreviewSplitter"
        };
        ProportionalDock outerLayoutDock = new ProportionalDock
        {
            Id = "ModInfoOuterLayout",
            Orientation = previewOrientation,
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
            CanClose = false,
            CanFloat = false
        };

        factory.InitLayout(root);
        modInfoDockControl.Factory = factory;
        modInfoDockControl.Layout = root;

        PopulateModDataViewer(null);
    }

    private void SaveModDataPanelLayout()
    {
        if (modDataTool?.Owner is not IDock dataDock)
            return;

        double proportion = dataDock.Proportion;
        if (double.IsNaN(proportion) || proportion < 0.05 || proportion > 0.95)
            proportion = 0.35;

        int orientation = ConfigManager.GetModDataPanelOrientation();
        bool modDataFirst = ConfigManager.GetModDataPanelFirst();
        if (dataDock.Owner is ProportionalDock proportionalDock && proportionalDock.VisibleDockables != null)
        {
            orientation = proportionalDock.Orientation == DockOrientation.Horizontal ? 1 : 0;
            int dataIndex = proportionalDock.VisibleDockables.IndexOf(dataDock);
            int descriptionIndex = descriptionTool?.Owner is IDockable descriptionDock
                ? proportionalDock.VisibleDockables.IndexOf(descriptionDock)
                : -1;
            if (dataIndex >= 0 && descriptionIndex >= 0)
                modDataFirst = dataIndex < descriptionIndex;
        }

        ConfigManager.SetModDataPanelLayout(proportion, orientation, modDataFirst);
    }

    private void SaveModPreviewPanelLayout()
    {
        if (modPreviewTool?.Owner is not IDock previewDock)
            return;

        double proportion = previewDock.Proportion;
        if (double.IsNaN(proportion) || proportion < 0.05 || proportion > 0.95)
            proportion = 0.45;

        int orientation = ConfigManager.GetModPreviewPanelOrientation();
        bool previewFirst = ConfigManager.GetModPreviewPanelFirst();
        if (previewDock.Owner is ProportionalDock proportionalDock && proportionalDock.VisibleDockables != null)
        {
            orientation = proportionalDock.Orientation == DockOrientation.Horizontal ? 1 : 0;
            int previewIndex = proportionalDock.VisibleDockables.IndexOf(previewDock);
            int otherIndex = -1;
            for (int i = 0; i < proportionalDock.VisibleDockables.Count; i++)
            {
                IDockable dockable = proportionalDock.VisibleDockables[i];
                if (i != previewIndex && dockable is IDock && dockable is not ProportionalDockSplitter)
                {
                    otherIndex = i;
                    break;
                }
            }
            if (previewIndex >= 0 && otherIndex >= 0)
                previewFirst = previewIndex < otherIndex;
        }

        ConfigManager.SetModPreviewPanelLayout(proportion, orientation, previewFirst);
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
        {
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
        };

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