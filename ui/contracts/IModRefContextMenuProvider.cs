using Avalonia.Controls;
using ModHearth.Models;
using ModHearth.UI.ViewModels;

namespace ModHearth.UI.Contracts
{
    public interface IModRefContextMenuProvider
    {
        void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm);
        void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm);
        IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm);
        ModHearthManager? GetManager();
    }
}
