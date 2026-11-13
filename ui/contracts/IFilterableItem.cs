using ModHearth.UI.Helpers;

namespace ModHearth.UI.Contracts
{
    /// <summary>
    /// A list item that participates in the shared search pipeline. <see cref="SearchFilterHelper"/>
    /// owns the filtered/visible flags so every filtered list applies the same rules.
    /// </summary>
    public interface IFilterableItem
    {
        bool IsVisible { get; set; }

        bool IsFilteredOut { get; set; }

        bool MatchesFilter(SearchFilterContext context);
    }
}
