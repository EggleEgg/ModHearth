using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

/// <summary>
/// Config-backed toggles shown in ModRefControl's shared context menu (Open Folder / Open URL /
/// Copy ID rows). Exposed as a static singleton rather than properties on MainWindow, since that
/// context menu is reused by windows that are not descendants of MainWindow (ModUpdateLogWindow,
/// WorkshopDownloaderWindow when floated) -- an ancestor-based binding silently fails to resolve
/// there, leaving these checkboxes stuck unchecked and unable to persist changes.
/// </summary>
public sealed class ContextMenuPreferences : INotifyPropertyChanged
{
    public static ContextMenuPreferences Instance { get; } = new();

    private ContextMenuPreferences() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpenSteamInClientEnabled
    {
        get => ConfigManager.GetOpenSteamInClient();
        set
        {
            if (value == ConfigManager.GetOpenSteamInClient())
                return;
            ConfigManager.SetOpenSteamInClient(value);
            OnPropertyChanged();
        }
    }

    public bool IsOpenSteamFolderEnabled
    {
        get => ConfigManager.GetOpenSteamFolder();
        set
        {
            if (value == ConfigManager.GetOpenSteamFolder())
                return;
            ConfigManager.SetOpenSteamFolder(value);
            OnPropertyChanged();
        }
    }

    public bool IsCopySteamFileIdEnabled
    {
        get => ConfigManager.GetCopySteamFileId();
        set
        {
            if (value == ConfigManager.GetCopySteamFileId())
                return;
            ConfigManager.SetCopySteamFileId(value);
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
