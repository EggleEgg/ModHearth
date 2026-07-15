using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ModHearth.Models;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using System;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Reactive;

namespace ModHearth.UI;

/// <summary>
/// UI Used for the "search by color" filter
/// </summary>
public partial class ModColorPicker : UserControl
{
    private sealed class ButtonState
    {
        public IBrush NormalBrush { get; set; } = Brushes.Transparent;
        public IBrush HoverBrush { get; set; } = Brushes.Transparent;
        public IBrush PressedBrush { get; set; } = Brushes.Transparent;
        public bool IsPointerOver { get; set; }
        public bool IsPressed { get; set; }
    }

    private readonly ButtonState _clearButtonState = new ButtonState();
    private Button? _clearSelectionButton;

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
        AvailableColors = new ObservableCollection<ModColorInfo>();
        SelectedColors = new ObservableCollection<ModColorInfo>();

        InitializeComponent();
        DataContext = this;
        ClearSelectionCommand = ReactiveCommand.Create(ClearSelection);

        _clearSelectionButton = this.FindControl<Button>("ClearSelectionButton");
        if (_clearSelectionButton != null)
        {
            InitializeButtonState(_clearSelectionButton, _clearButtonState);
        }

        SelectedColors.CollectionChanged += (s, e) =>
        {
            HasSelectedColors = SelectedColors.Any();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        // Intercept scrollbar interaction to protect Picker_Tapped
        var scrollViewer = this.FindControl<ScrollViewer>("ColorScrollViewer");
        if (scrollViewer != null)
        {
            scrollViewer.AddHandler(InputElement.PointerPressedEvent, (sender, args) =>
                {
                    if (args.Source is Visual visual && visual.FindAncestorOfType<ScrollBar>(includeSelf: true) != null)
                    {
                        args.Handled = true;
                    }
                }, RoutingStrategies.Tunnel);

            scrollViewer.PointerWheelChanged += (sender, args) =>
            {
                // If the user rolls a standard vertical wheel (Delta.Y is not 0)
                if (args.Delta.Y != 0)
                {
                    var currentOffset = scrollViewer.Offset;
                    // Adjust this number (e.g., 40) to change how fast/smoothly it scrolls per notch
                    double scrollSpeed = 40;

                    double newX = currentOffset.X - (args.Delta.Y * scrollSpeed);
                    double maxX = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;
                    newX = Math.Clamp(newX, 0, Math.Max(0, maxX));

                    scrollViewer.Offset = new Vector(newX, currentOffset.Y);
                    args.Handled = true;
                }
            };
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
                SelectedColors.Remove(modColorInfo);
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

    public void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        IBrush searchButtonBrush = BrushCache.GetBrush(Style.instance!.searchButtonColor.ToAvaloniaColor());
        IBrush searchButtonHoverBrush = BrushCache.GetBrush(Style.instance.searchButtonHoverColor.ToAvaloniaColor());
        IBrush searchButtonPressedBrush = BrushCache.GetBrush(Style.instance.searchButtonPressedColor.ToAvaloniaColor());
        IBrush buttonTextBrush = BrushCache.GetBrush(Style.instance.buttonTextColor.ToAvaloniaColor());

        if (_clearSelectionButton != null)
        {
            ApplyButtonBrushes(_clearSelectionButton, _clearButtonState,
                searchButtonBrush, searchButtonHoverBrush, searchButtonPressedBrush);
            _clearSelectionButton.Foreground = buttonTextBrush;
        }
    }

    private void InitializeButtonState(Button button, ButtonState state)
    {
        button.GetObservable(InputElement.IsPointerOverProperty)
              .Subscribe(isOver =>
              {
                  state.IsPointerOver = isOver;
                  if (!isOver)
                      state.IsPressed = false;
                  UpdateButtonBackground(button, state);
              });

        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                state.IsPressed = true;
                UpdateButtonBackground(button, state);
            }
        };

        button.PointerReleased += (_, _) =>
        {
            state.IsPressed = false;
            state.IsPointerOver = button.IsPointerOver;
            UpdateButtonBackground(button, state);
        };

        button.PointerCaptureLost += (_, _) =>
        {
            state.IsPressed = false;
            state.IsPointerOver = button.IsPointerOver;
            UpdateButtonBackground(button, state);
        };

        button.Click += (_, _) =>
        {
            state.IsPressed = false;
            state.IsPointerOver = button.IsPointerOver;
            UpdateButtonBackground(button, state);
        };
    }

    private static void ApplyButtonBrushes(
        Button button,
        ButtonState state,
        IBrush normalBrush,
        IBrush hoverBrush,
        IBrush pressedBrush)
    {
        state.NormalBrush = normalBrush;
        state.HoverBrush = hoverBrush;
        state.PressedBrush = pressedBrush;
        UpdateButtonBackground(button, state);
    }

    private static void UpdateButtonBackground(Button button, ButtonState state)
    {
        IBrush targetBrush;
        if (state.IsPressed)
            targetBrush = state.PressedBrush;
        else if (state.IsPointerOver)
            targetBrush = state.HoverBrush;
        else
            targetBrush = state.NormalBrush;

        button.Background = targetBrush;
    }
}
