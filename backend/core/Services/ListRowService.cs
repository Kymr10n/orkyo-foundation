using System.Text.Json;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// The data side of lists: rows in an instance, and the resolver that finds (or creates) the
/// instance belonging to one resource's list field. Filling a list in is ordinary editing, the
/// same as filling in any other field on a resource — reshaping it is governance and lives in
/// <see cref="IListDefinitionService"/>.
/// </summary>
public interface IListRowService
{
    /// <summary>The instance itself, or null when unknown.</summary>
    Task<ListInstanceInfo?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>Rows of one instance, oldest first, or null when the instance is unknown.</summary>
    Task<List<ListRowInfo>?> GetRowsAsync(Guid instanceId, CancellationToken ct = default);

    Task<ListRowInfo?> CreateRowAsync(Guid instanceId, ListRowRequest request, CancellationToken ct = default);
    Task<ListRowInfo?> UpdateRowAsync(Guid instanceId, Guid rowId, ListRowRequest request, CancellationToken ct = default);
    Task<bool> DeleteRowAsync(Guid instanceId, Guid rowId, CancellationToken ct = default);

    /// <summary>
    /// The instance for one resource's list field, without creating it. Null until the first write
    /// — a read never brings a holder into existence, so listing resources leaves no trail of
    /// empty instances.
    /// </summary>
    Task<ListInstanceInfo?> ResolveResourceInstanceAsync(Guid resourceId, Guid fieldId, CancellationToken ct = default);

    /// <summary>
    /// The instance for one resource's list field, creating it when absent. Returns null when the
    /// resource or field is unknown, or the field is not a list field of that resource's type.
    /// </summary>
    Task<ListInstanceInfo?> EnsureResourceInstanceAsync(Guid resourceId, Guid fieldId, CancellationToken ct = default);
}

public class ListRowService(
    IListInstanceRepository instanceRepository,
    IListDefinitionRepository definitionRepository,
    IResourceCustomFieldRepository customFieldRepository,
    IResourceRepository resourceRepository) : IListRowService
{
    /// <summary>
    /// Rows are read whole, rendered in one table and shipped in exports, so the count is bounded
    /// here — nothing else bounds it. High enough that no real maintenance log hits it, low enough
    /// that one instance cannot become an unbounded payload.
    /// </summary>
    public const int MaxRowsPerInstance = 500;

    public Task<ListInstanceInfo?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default)
        => instanceRepository.GetByIdAsync(instanceId, ct);

    public async Task<List<ListRowInfo>?> GetRowsAsync(Guid instanceId, CancellationToken ct = default)
    {
        if (await instanceRepository.GetByIdAsync(instanceId, ct) is null) return null;
        return await instanceRepository.GetRowsAsync(instanceId, ct);
    }

    public async Task<ListRowInfo?> CreateRowAsync(
        Guid instanceId, ListRowRequest request, CancellationToken ct = default)
    {
        var instance = await instanceRepository.GetByIdAsync(instanceId, ct);
        if (instance is null) return null;

        await ValidateValuesAsync(instance, request.Values, null, ct);

        return await instanceRepository.CreateRowAsync(instanceId, request.Values, MaxRowsPerInstance, ct)
            ?? throw new ArgumentException($"A list holds at most {MaxRowsPerInstance} rows");
    }

    public async Task<ListRowInfo?> UpdateRowAsync(
        Guid instanceId, Guid rowId, ListRowRequest request, CancellationToken ct = default)
    {
        var instance = await instanceRepository.GetByIdAsync(instanceId, ct);
        if (instance is null) return null;

        // A row is addressed through its instance, so one belonging to another instance is a 404
        // rather than someone else's row edited by mistake.
        var existing = await instanceRepository.GetRowAsync(rowId, ct);
        if (existing is null || existing.ListInstanceId != instanceId) return null;

        await ValidateValuesAsync(instance, request.Values, existing.Id, ct);

        return await instanceRepository.UpdateRowAsync(rowId, request.Values, ct);
    }

    public async Task<bool> DeleteRowAsync(Guid instanceId, Guid rowId, CancellationToken ct = default)
    {
        var existing = await instanceRepository.GetRowAsync(rowId, ct);
        if (existing is null || existing.ListInstanceId != instanceId) return false;

        return await instanceRepository.DeleteRowAsync(rowId, ct);
    }

    public async Task<ListInstanceInfo?> ResolveResourceInstanceAsync(
        Guid resourceId, Guid fieldId, CancellationToken ct = default)
        => await instanceRepository.GetResourceInstanceAsync(resourceId, fieldId, ct);

    public async Task<ListInstanceInfo?> EnsureResourceInstanceAsync(
        Guid resourceId, Guid fieldId, CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct);
        if (resource is null) return null;

        var field = await customFieldRepository.GetByIdAsync(fieldId, ct);

        // The field has to belong to this resource's own type and actually be a list field:
        // otherwise the pair names an instance that has no shape, and the binding CHECK would
        // reject the insert with a constraint error instead of a 404.
        if (field is null
            || field.ResourceTypeId != resource.ResourceTypeId
            || field.DataType != CustomFieldDataTypes.List
            || field.ListDefinitionId is not { } definitionId)
        {
            return null;
        }

        return await instanceRepository.GetOrCreateResourceInstanceAsync(definitionId, resourceId, fieldId, ct);
    }

    /// <summary>
    /// Checks a row against the columns its definition declares. Inactive columns are skipped the
    /// way inactive fields are: a column retired while rows still carry values for it must not make
    /// those rows unsaveable.
    /// </summary>
    /// <param name="rowId">
    /// The row being written, or null when it is being created. Only a row that already exists can
    /// be pointed at, so this is what a <c>row_ref</c> cell must not reach.
    /// </param>
    private async Task ValidateValuesAsync(
        ListInstanceInfo instance, IReadOnlyDictionary<string, JsonElement> values,
        Guid? rowId, CancellationToken ct)
    {
        var columns = await definitionRepository.GetColumnsAsync(instance.ListDefinitionId, ct);
        var byKey = columns.ToDictionary(c => c.Key, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!byKey.TryGetValue(key, out var column))
                throw new ArgumentException($"'{key}' is not a column of this list");

            // An empty value is an unfilled optional cell, whatever its type. Whether it may be
            // empty is the required check below, not a type question.
            if (CustomFieldValueRules.IsEmpty(value)) continue;

            CustomFieldValueRules.Validate($"Column '{column.Label}'", column.DataType, value, column.Options);
        }

        foreach (var column in columns.Where(c => c is { IsActive: true, IsRequired: true }))
        {
            if (!values.TryGetValue(column.Key, out var value) || CustomFieldValueRules.IsEmpty(value))
                throw new ArgumentException($"Column '{column.Label}' is required");
        }

        await ValidateRowRefsAsync(instance, columns, values, rowId, ct);
    }

    /// <summary>
    /// Checks what a <c>row_ref</c> cell points at: a row of this same instance, not this row, and
    /// no chain that comes back here.
    /// </summary>
    /// <remarks>
    /// The database cannot hold this — every row lives in one table, so a foreign key would let a
    /// cell name a row of some other instance, and a cycle is not a constraint at all. The rows are
    /// read once and walked in memory instead, which an instance capped at
    /// <see cref="MaxRowsPerInstance"/> rows makes cheap; the read happens only when a cell
    /// actually carries a reference.
    /// <para>
    /// The read and the write are not one transaction, so two edits landing together can still
    /// close a loop neither saw — A pointed at B while B was being pointed at A. Left as is
    /// deliberately: serializing every row write to defend a two-person race costs more than the
    /// state it prevents, which is visible in the table and cleared by emptying either cell.
    /// </para>
    /// </remarks>
    private async Task ValidateRowRefsAsync(
        ListInstanceInfo instance, IReadOnlyList<ListColumnInfo> columns,
        IReadOnlyDictionary<string, JsonElement> values, Guid? rowId, CancellationToken ct)
    {
        var refs = columns
            .Where(c => c.DataType == ListColumnDataTypes.RowRef)
            .Select(c => (Column: c, Target: TargetOf(values, c.Key)))
            .Where(r => r.Target is not null)
            .ToList();

        if (refs.Count == 0) return;

        var rows = (await instanceRepository.GetRowsAsync(instance.Id, ct)).ToDictionary(r => r.Id);

        foreach (var (column, target) in refs)
        {
            if (target == rowId)
                throw new ArgumentException($"Column '{column.Label}' cannot point at this row itself");

            if (!rows.ContainsKey(target!.Value))
                throw new ArgumentException($"Column '{column.Label}' must point at a row of this list");

            // A row being created is pointed at by nothing yet, so it cannot close a cycle.
            if (rowId is not { } self) continue;

            // Walk up from the target. `seen` is not the cycle check — it stops a cycle that
            // already exists elsewhere in the column from looping this walk forever.
            var seen = new HashSet<Guid>();
            for (var current = target.Value; seen.Add(current);)
            {
                if (current == self)
                    throw new ArgumentException($"Column '{column.Label}' would make a loop");

                if (!rows.TryGetValue(current, out var row)
                    || TargetOf(row.Values, column.Key) is not { } next)
                {
                    break;
                }

                current = next;
            }
        }
    }

    /// <summary>The row id a cell holds, or null when it is empty or absent. Shape is already checked.</summary>
    private static Guid? TargetOf(IReadOnlyDictionary<string, JsonElement> values, string key)
        => values.TryGetValue(key, out var value)
           && !CustomFieldValueRules.IsEmpty(value)
           && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;
}
