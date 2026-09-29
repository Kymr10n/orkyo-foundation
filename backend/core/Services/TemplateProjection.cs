using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// How <see cref="ExportService"/> and <see cref="PresetService"/> turn stored templates into
/// their portable form: one bulk item fetch for all the templates, each item resolved to its
/// criterion's key (an item whose criterion is not in the map is dropped), ordered by key so the
/// output is stable. Each caller maps the result onto its own record type.
/// </summary>
internal static class TemplateProjection
{
    public static async Task<List<T>> ProjectAsync<T>(
        ITemplateRepository repository,
        IReadOnlyList<Template> templates,
        IReadOnlyDictionary<Guid, string> criterionIdToKey,
        Func<Template, List<(string CriterionKey, string Value)>, T> project,
        CancellationToken ct)
    {
        var itemsByTemplate = await repository.GetTemplateItemsByTemplatesAsync(templates.Select(t => t.Id).ToList(), ct);

        return templates.Select(template => project(template, itemsByTemplate.GetValueOrDefault(template.Id, [])
                .Where(i => criterionIdToKey.ContainsKey(i.CriterionId))
                .Select(i => (CriterionKey: criterionIdToKey[i.CriterionId], i.Value))
                .OrderBy(i => i.CriterionKey, StringComparer.Ordinal)
                .ToList()))
            .ToList();
    }
}
