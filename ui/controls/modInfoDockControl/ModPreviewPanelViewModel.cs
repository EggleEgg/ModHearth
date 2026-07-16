using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI;

public sealed class ModPreviewPanelViewModel : Tool
{
    private IImage? previewImage;

    public IImage? PreviewImage
    {
        get => previewImage;
        set
        {
            if (Equals(previewImage, value))
                return;
            previewImage = value;
            OnPropertyChanged();
        }
    }
}