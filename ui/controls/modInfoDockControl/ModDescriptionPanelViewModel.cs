using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;

namespace ModHearth.UI;

public sealed class ModDescriptionPanelViewModel : Tool
{
    private string descriptionHtml = string.Empty;

    public string DescriptionHtml
    {
        get => descriptionHtml;
        set
        {
            if (descriptionHtml == value)
                return;
            descriptionHtml = value;
            OnPropertyChanged();
        }
    }
}