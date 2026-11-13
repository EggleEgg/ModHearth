using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI.Controls.ModInfoDockControl
{
    public sealed class ModDescriptionPanelViewModel : Tool
    {
        public string DescriptionHtml
        {
            get;
            set
            {
                if (field == value)
                    return;
                field = value;
                OnPropertyChanged();
            }
        } = string.Empty;
    }
}
