using System.Globalization;
using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Models;

namespace Acorn.Application.Services;

/// <summary>
/// Builds the vault screen's view models (sidebar, filtered list, selected entry) from the
/// decrypted data. Secret values are never copied into these view models.
/// </summary>
internal sealed class VaultProjection
{
    private readonly IVaultSession _session;
    private readonly AppState _state;

    public VaultProjection(IVaultSession session, AppState state)
    {
        _session = session;
        _state = state;
    }

    public void Refresh()
    {
        List<Entry> entries;
        List<string> environments;
        try
        {
            var data = _session.Data;
            entries = [.. data.Entries];
            environments = [.. data.Settings.Environments];
        }
        catch (VaultLockedException)
        {
            return;
        }

        _state.Sidebar = BuildSidebar(entries, environments);

        var items = entries
            .Where(e => MatchesFilter(e, _state.Filter) && MatchesSearch(e, _state.SearchText))
            .OrderByDescending(e => e.Favorite)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToListItem)
            .ToList();
        _state.EntryList = new EntryListViewModel(items, entries.Count);

        var selected = _state.SelectedEntryId is { } id ? entries.FirstOrDefault(e => e.Id == id) : null;
        _state.SelectedEntryId = selected?.Id;
        _state.SelectedEntry = selected is null ? null : ToDetail(selected);
    }

    public static EntryListItemViewModel ToListItem(Entry e) => new(
        e.Id, e.Type, e.Name, e.Env, e.Host, e.Port, e.Username,
        HasPassword: e.Password.Length > 0,
        Tags: [.. e.Tags],
        e.Favorite,
        e.UpdatedAt);

    public static EntryDetailViewModel ToDetail(Entry e) => new(
        e.Id, e.Type, e.Name, e.Env, e.Host, e.Port, e.Username,
        HasPassword: e.Password.Length > 0,
        e.SshKeyPath,
        SshCommand.Build(e),
        [.. e.Tags],
        e.CustomFields
            .Select(f => new CustomFieldViewModel(f.Id, f.Label, f.IsSecret, f.IsSecret ? null : f.Value, f.Value.Length > 0))
            .ToList(),
        e.Notes,
        e.Favorite,
        e.CreatedAt,
        e.UpdatedAt);

    private static SidebarViewModel BuildSidebar(List<Entry> entries, List<string> configuredEnvironments)
    {
        var environments = configuredEnvironments
            .Concat(entries.Select(e => e.Env).Where(env => env.Length > 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(env => new SidebarItem(env, entries.Count(e => string.Equals(e.Env, env, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var tags = entries
            .SelectMany(e => e.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new SidebarItem(g.First(), g.Count()))
            .ToList();

        return new SidebarViewModel(entries.Count, entries.Count(e => e.Favorite), environments, tags);
    }

    private static bool MatchesFilter(Entry entry, EntryFilter filter) => filter.Kind switch
    {
        EntryFilterKind.Favorites => entry.Favorite,
        EntryFilterKind.Environment => string.Equals(entry.Env, filter.Value, StringComparison.OrdinalIgnoreCase),
        EntryFilterKind.Tag => entry.Tags.Contains(filter.Value ?? "", StringComparer.OrdinalIgnoreCase),
        _ => true,
    };

    /// <summary>In-memory search over non-secret fields. Every whitespace-separated term must match.</summary>
    private static bool MatchesSearch(Entry entry, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            Has(entry.Name, term) ||
            Has(entry.Host, term) ||
            Has(entry.Username, term) ||
            Has(entry.Env, term) ||
            Has(entry.Type, term) ||
            Has(entry.Notes, term) ||
            entry.Tags.Any(tag => Has(tag, term)) ||
            entry.CustomFields.Any(f => Has(f.Label, term) || (!f.IsSecret && Has(f.Value, term))) ||
            (entry.Port is { } port && port.ToString(CultureInfo.InvariantCulture) == term));
    }

    private static bool Has(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
