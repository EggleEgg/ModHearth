using Avalonia.Media;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI.Controls.ModInfoDockControl
{
    public sealed class ModPreviewPanelViewModel : Tool
    {
        public IImage? PreviewImage
        {
            get;
            set
            {
                if (Equals(field, value))
                    return;
                field = value;
                OnPropertyChanged();
            }
        }
    }
}