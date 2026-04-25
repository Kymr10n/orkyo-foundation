using Api.Models;

namespace Api.Repositories;

public interface ISearchRepository
{
    Task<List<SearchResult>> SearchAsync(string query, Guid? siteId, string[]? types, int limit = 20);
}
