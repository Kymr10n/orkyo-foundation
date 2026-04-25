using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        var search = app.MapGroup("/api/search").RequireAuthorization().RequireTenantMembership();

        search.MapGet("/", async (string q, Guid? siteId, string? types, int? limit, ISearchRepository repository, IAuthorizationContext authContext, ITenantSettingsService settingsService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(q))
                    return Results.Ok(new SearchResponse { Query = q ?? "", Results = new List<SearchResult>() });

                var typeFilter = string.IsNullOrWhiteSpace(types)
                    ? null
                    : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var settings = await settingsService.GetSettingsAsync();
                var results = await repository.SearchAsync(q, siteId, typeFilter, Math.Min(limit ?? settings.Search_DefaultPageSize, 50));
                var canEdit = authContext.CanEdit;
                var resultsWithPermissions = results.Select(r => r with { Permissions = r.Permissions with { CanEdit = canEdit } }).ToList();

                return Results.Ok(new SearchResponse { Query = q, Results = resultsWithPermissions });
            }, logger, "global search", new { query = q, siteId, types });
        })
        .WithName("GlobalSearch")
        .WithDescription("Search across all entities")
        .Produces<SearchResponse>(StatusCodes.Status200OK);
    }
}
