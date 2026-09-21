using Api.Helpers;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// The write side of requirement scoping. A request, or a request template, may only carry a
/// criterion that is applicable to requests and applies to a resource type the request can hold:
/// one of its target types, or any active directory type, since people are staffed on the
/// request rather than named as a target. The read side is
/// <see cref="Models.RequestRequirementInfo.AppliesTo"/>, and as there a criterion with no
/// scope recorded applies to every type. Runs inside the caller's transaction, after the targets
/// and the values are written, so narrowing the targets is checked the same way as adding a value.
/// </summary>
internal static class CriterionScopeSql
{
    /// <summary>
    /// The criteria on <paramref name="valueTable"/> for @owner_id that the owner cannot hold,
    /// with the scope each one applies to. Table and column names are hardcoded literals chosen
    /// by the two callers, never user input.
    /// </summary>
    private static string UnsupportedCriteria(string valueTable, string targetTable, string ownerColumn) => $@"
        SELECT c.name,
               c.applicable_to_requests,
               (SELECT string_agg(rt.key, ', ' ORDER BY rt.key)
                  FROM criterion_resource_types crt
                  JOIN resource_types rt ON rt.id = crt.resource_type_id
                 WHERE crt.criterion_id = c.id) AS scope
          FROM {valueTable} v
          JOIN criteria c ON c.id = v.criterion_id
         WHERE v.{ownerColumn} = @owner_id
           AND (NOT c.applicable_to_requests
                OR (EXISTS (SELECT 1 FROM criterion_resource_types crt WHERE crt.criterion_id = c.id)
                    AND NOT EXISTS (
                        SELECT 1
                          FROM criterion_resource_types crt
                          JOIN resource_types rt ON rt.id = crt.resource_type_id
                         WHERE crt.criterion_id = c.id
                           AND ((rt.has_directory_profile AND rt.is_active)
                                OR rt.id IN (SELECT resource_type_id FROM {targetTable} WHERE {ownerColumn} = @owner_id)))))
         ORDER BY c.name";

    internal static Task EnsureRequestRequirementsApplyAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid requestId, CancellationToken ct) =>
        EnsureAllApplyAsync(conn, tx, "request_requirements", "request_target_resource_types", "request_id", requestId, "request", ct);

    internal static Task EnsureTemplateItemsApplyAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid templateId, CancellationToken ct) =>
        EnsureAllApplyAsync(conn, tx, "template_items", "template_target_resource_types", "template_id", templateId, "template", ct);

    private static async Task EnsureAllApplyAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string valueTable, string targetTable, string ownerColumn, Guid ownerId, string ownerNoun,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(UnsupportedCriteria(valueTable, targetTable, ownerColumn), conn, tx);
        cmd.Parameters.AddWithValue("owner_id", ownerId);

        var problems = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString("name");
                problems.Add(reader.GetBoolean("applicable_to_requests")
                    ? $"'{name}' applies to {reader.GetNullableString("scope")}"
                    : $"'{name}' is not applicable to requests");
            }
        }

        if (problems.Count > 0)
            throw new ArgumentException(
                $"No resource this {ownerNoun} can hold carries these criteria: {string.Join("; ", problems)}. " +
                $"A criterion must apply to one of the {ownerNoun}'s target resource types, or to people.");
    }
}
