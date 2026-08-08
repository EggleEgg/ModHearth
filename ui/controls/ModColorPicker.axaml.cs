using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ModHearth.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using Avalonia.Media;

namespace ModHearth.UI;

/// <summary>
/// UI Used for the "search by color" filter
/// </summary>
public partial class ModColorPicker : UserControl
{
    public static readonly StyledProperty<ObservableCollection<ModColorInfo>> AvailableColorsProperty =
        AvaloniaProperty.Register<ModColorPicker, ObservableCollection<ModColorInfo>>(nameof(AvailableColors));

    public static readonly StyledProperty<ObservableCollection<ModColorInfo>> SelectedColorsProperty =
        AvaloniaProperty.Register<ModColorPicker, ObservableCollection<ModColorInfo>>(nameof(SelectedColors));

    public static readonly StyledProperty<bool> HasSelectedColorsProperty =
        AvaloniaProperty.Register<ModColorPicker, bool>(nameof(HasSelectedColors), false);

    public event EventHandler? SelectionChanged;
    public event EventHandler? PickerClicked;

    public ICommand ClearSelectionCommand { get; }

    public ObservableCollection<ModColorInfo> AvailableColors
    {
        get => GetValue(AvailableColorsProperty);
        set => SetValue(AvailableColorsProperty, value);
    }

    public ObservableCollection<ModColorInfo> SelectedColors
    {
        get => GetValue(SelectedColorsProperty);
        set => SetValue(SelectedColorsProperty, value);
    }

    public bool HasSelectedColors
    {
        get => GetValue(HasSelectedColorsProperty);
        set => SetValue(HasSelectedColorsProperty, value);
    }

    public ModColorPicker()
    {
        // Initialize collections before InitializeComponent to ensure they are available for binding
        AvailableColors = [];
        SelectedColors = [];

        InitializeComponent();

        ClearSelectionCommand = ReactiveCommand.Create(ClearSelection);

        SelectedColors.CollectionChanged += (s, e) =>
        {
            HasSelectedColors = SelectedColors.Any();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        // Intercept scrollbar interaction to protect Picker_Tapped
        var scrollViewer = this.FindControl<ScrollViewer>("ColorScrollViewer");
        if (scrollViewer != null)
        {
            HorizontalScrollHelper.EnableSidewaysScrolling(scrollViewer);
        }

        var clearSelectionButton = this.FindControl<Button>("ClearSelectionButton");
        if (clearSelectionButton != null)
        {
            SearchButtonBehavior.GetOrCreate(clearSelectionButton);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Color_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ModColorInfo modColorInfo)
        {
            e.Handled = true; // Stop propagation to PickerClicked
            modColorInfo.IsSelected = !modColorInfo.IsSelected;
            if (modColorInfo.IsSelected)
            {
                if (!SelectedColors.Contains(modColorInfo))
                    SelectedColors.Add(modColorInfo);
            }
            else
            {
                _ = SelectedColors.Remove(modColorInfo);
            }
        }
    }

    private void Picker_Tapped(object? sender, TappedEventArgs e)
    {
        // Only trigger if we didn't tap a specific color
        if (!e.Handled)
        {
            PickerClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearSelection()
    {
        foreach (ModColorInfo color in AvailableColors)
        {
            color.IsSelected = false;
        }
        SelectedColors.Clear();
    }

    public void ApplyStyle(IBrush normalBrush, IBrush hoverBrush, IBrush pressedBrush, IBrush textBrush)
    {
        var clearSelectionButton = this.FindControl<Button>("ClearSelectionButton");
        if (clearSelectionButton != null)
        {
            SearchButtonBehavior.GetOrCreate(clearSelectionButton).ApplyBrushes(normalBrush, hoverBrush, pressedBrush);
            clearSelectionButton.Foreground = textBrush;
            clearSelectionButton.BorderBrush = Brushes.Transparent;
            clearSelectionButton.BorderThickness = new Thickness(0);
        }
    }
}
