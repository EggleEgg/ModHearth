using Avalonia.Controls;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.UI.ViewModels;

namespace ModHearth.UI.Contracts
{
    public interface IModRefContextMenuProvider
    {
        // Most providers have nothing to do when the menu opens; only override when needed.
        void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm) { }
        Task OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm);
        IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm);
        ModHearthManager? GetManager();
    }
}
