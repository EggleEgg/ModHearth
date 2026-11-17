namespace ModHearth.App.Tests
{
    /// <summary>
    /// Categories for tests that depend on the host machine. CI runs headless and has no Dwarf
    /// Fortress installation, so workflows exclude these categories with
    /// <c>--filter "Category!=RequiresDwarfFortress"</c>. The trait name must be
    /// <c>Category</c>: the xunit VSTest adapter exposes traits under their own name, not under
    /// VSTest's <c>TestCategory</c> alias.
    /// </summary>
    internal static class TestCategories
    {
        public const string TraitName = "Category";

        /// <summary>
        /// Requires a local Dwarf Fortress installation/executable layout that CI does not provide.
        /// </summary>
        public const string RequiresDwarfFortress = "RequiresDwarfFortress";
    }
}
