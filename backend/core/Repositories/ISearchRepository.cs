using Api.Models;

namespace Api.Repositories;

/// <summary>Full-text search across resources, requests, sites, templates, and criteria.</summary>
public interface ISearchRepository
{
    /// <summary>
    /// Runs a cross-entity search. Results are ranked by relevance.
    /// </summary>
    /// <param name="query">The search term.</param>
    /// <param name="siteId">Scope to a specific site; pass <c>null</c> to search all sites.</param>
    /// <param name="types">Restrict to entity types (e.g. "resource", "request"); <c>null</c> searches all.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    Task<List<SearchResult>> SearchAsync(string query, Guid? siteId, string[]? types, int limit = 20, CancellationToken ct = default);
}
