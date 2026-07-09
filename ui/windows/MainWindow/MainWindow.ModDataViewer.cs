using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;
using TheArtOfDev.HtmlRenderer.Avalonia;
using DockOrientation = Dock.Model.Core.Orientation;

namespace ModHearth.UI;

public partial class MainWindow
{
    private HtmlPanel modDescriptionHtml = null!;
    private Image modPreviewImage = null!;
    private Tool? modPreviewTool;
    private Tool? modDataTool;
    private Tool? descriptionTool;
    private StackPanel modDataPanelContent = null!;
    private ModReference? currentModDataModRef;

    private void InitializeModInfoDock()
    {
        modDescriptionHtml = new HtmlPanel();

        ScrollViewer modDescriptionScrollViewer = new ScrollViewer
        {
            Content = modDescriptionHtml,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        modDataPanelContent = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(6, 4, 6, 4)
        };
        ScrollViewer modDataScrollViewer = new ScrollViewer
        {
            Content = modDataPanelContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        modPreviewImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 4, 10, 4)
        };

        Factory factory = new Factory();

        modPreviewTool = new Tool
        {
            Id = "ModPreviewTool",
            Title = "Preview",
            Content = modPreviewImage,
            CanClose = false,
            CanFloat = false,
            CanPin = false
        };
        modDataTool = new Tool
        {
            Id = "ModDataTool",
            Title = "Mod Data",
            Content = modDataScrollViewer,
            CanClose = false,
            CanFloat = false,
            CanPin = false
        };
        descriptionTool = new Tool
        {
            Id = "DescriptionTool",
            Title = "Description",
            Content = modDescriptionScrollViewer,
            CanClose = false,
            CanFloat = false,
            CanPin = false,
            CanDrag = false
        };

        double proportion = ConfigManager.GetModDataPanelProportion();
        if (double.IsNaN(proportion) || proportion < 0.05 || proportion > 0.95)
            proportion = 0.35;
        DockOrientation orientation = ConfigManager.GetModDataPanelOrientation() == 1
            ? DockOrientation.Horizontal
            : DockOrientation.Vertical;
        bool modDataFirst = ConfigManager.GetModDataPanelFirst();

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
        modDataPanelContent.Children.Clear();

        IBrush textBrush = Style.instance != null
            ? new SolidColorBrush(Style.instance.textColor.ToAvaloniaColor())
            : Brushes.Black;

        if (modref == null)
        {
            modDataPanelContent.Children.Add(new TextBlock
            {
                Text = "Select a mod to view its data.",
                FontSize = 12,
                FontStyle = FontStyle.Italic,
                Foreground = textBrush,
                Opacity = 0.7,
                Margin = new Thickness(2)
            });
            return;
        }

        foreach ((string label, string value) in GetModDataEntries(modref))
            modDataPanelContent.Children.Add(CreateModDataRow(label, value, textBrush));
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

    private Control CreateModDataRow(string label, string value, IBrush textBrush)
    {
        Grid rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        TextBlock labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = textBrush,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 110
        };
        TextBlock valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = textBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(valueBlock, 1);
        rowGrid.Children.Add(labelBlock);
        rowGrid.Children.Add(valueBlock);

        IBrush hoverBrush = Style.instance != null
            ? new SolidColorBrush(Style.instance.modRefPanelColor.ToAvaloniaColor())
            : new SolidColorBrush(Color.Parse("#22888888"));

        Border row = new Border
        {
            Child = rowGrid,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(row, $"Click to copy {label}");
        row.PointerEntered += (_, _) => row.Background = hoverBrush;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                return;

            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            await clipboard.SetTextAsync(value);
            ShowNotification($"Copied {label}", "copyIcon.svg");
        };

        return row;
    }

    private void RefreshModDataViewer()
    {
        PopulateModDataViewer(currentModDataModRef);
    }
}
