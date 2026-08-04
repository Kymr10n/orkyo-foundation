using Npgsql;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Writes <c>public.request_target_resource_types</c> — the per-request declaration of which
/// resource types must be assigned before a request counts as scheduled (migrations 1720/1730).
///
/// The seeders bulk-COPY requests, so these rows have to be written explicitly: a request with no
/// target rows fails the predicate's EXISTS guard and reports itself unscheduled forever, which
/// would leave utilization and insights showing an empty schedule on a fresh seed.
/// </summary>
public static class RequestTargetFactory
{
    /// <summary>
    /// Inserts one row per (request, resource type key) pair in a single set-based statement —
    /// these seeds run at XLarge scale, so per-row round trips are not an option.
    /// </summary>
    public static async Task WriteAsync(
        NpgsqlConnection conn, IReadOnlyList<(Guid RequestId, string TypeKey)> targets)
    {
        if (targets.Count == 0) return;

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO public.request_target_resource_types (request_id, resource_type_id)
              SELECT t.request_id, rt.id
                FROM unnest(@request_ids, @type_keys) AS t(request_id, type_key)
                JOIN public.resource_types rt ON rt.key = t.type_key", conn);
        cmd.Parameters.AddWithValue("request_ids", targets.Select(t => t.RequestId).ToArray());
        cmd.Parameters.AddWithValue("type_keys", targets.Select(t => t.TypeKey).ToArray());
        var written = await cmd.ExecuteNonQueryAsync();

        // A missing resource type would silently drop targets, and a request that targets nothing
        // reports itself unscheduled forever — exactly the failure this factory exists to prevent.
        if (written != targets.Count)
            throw new InvalidOperationException(
                $"Expected {targets.Count} request target rows, wrote {written}. " +
                "One or more target resource type keys do not exist.");
    }
}
