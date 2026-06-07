using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds reusable request templates (the user-facing "save as template" feature) for the recurring
/// work the narrative instantiates: a Preventive Maintenance and a Quality Audit template per facility,
/// each with a matching skill requirement. Requires an existing tenant user (reset preserves users); a
/// no-op if none exists.
/// </summary>
public static class TemplateFactory
{
    public static async Task<int> SeedAsync(
        NpgsqlConnection conn,
        IReadOnlyDictionary<string, Guid> criteria)
    {
        Guid userId;
        await using (var cmd = new NpgsqlCommand("SELECT id FROM public.users ORDER BY created_at LIMIT 1", conn))
        {
            var u = await cmd.ExecuteScalarAsync();
            if (u is null or DBNull) return 0; // no user → skip templates
            userId = (Guid)u;
        }

        var now = DateTime.UtcNow;
        var defs = new List<(string Name, string Desc, string SkillKey)>();
        foreach (var f in FacilityModel.All)
        {
            defs.Add(($"{f.SiteCode} Preventive Maintenance", "Monthly preventive maintenance for a machine.", SkillCatalog.Maintenance));
            defs.Add(($"{f.SiteCode} Quality Audit", "Quarterly quality-system audit.", SkillCatalog.QaInspection));
        }

        var templateIds = new List<(Guid Id, string SkillKey)>();
        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.request_templates (id, user_id, name, description, minimal_duration_value, minimal_duration_unit, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (name, desc, skill) in defs)
            {
                var id = Guid.NewGuid();
                templateIds.Add((id, skill));
                await w.StartRowAsync();
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(userId, NpgsqlDbType.Uuid);
                await w.WriteAsync(name, NpgsqlDbType.Varchar);
                await w.WriteAsync(desc, NpgsqlDbType.Text);
                await w.WriteAsync(2, NpgsqlDbType.Integer);
                await w.WriteAsync("hours", NpgsqlDbType.Varchar);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await w.CompleteAsync();
        }

        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.request_template_requirements (id, template_id, criterion_id, value, created_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (id, skill) in templateIds)
            {
                await w.StartRowAsync();
                await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(criteria[skill], NpgsqlDbType.Uuid);
                await w.WriteAsync("true", NpgsqlDbType.Jsonb);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await w.CompleteAsync();
        }

        return templateIds.Count;
    }
}
