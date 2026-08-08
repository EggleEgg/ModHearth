using Avalonia.Controls;

namespace ModHearth.UI;

public interface IModRefContextMenuProvider
{
    void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm);
    void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm);
    IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm);
    ModHearthManager? GetManager();
    ModRefControl? GetContextMenuHost();
}