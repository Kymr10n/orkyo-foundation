using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Routings for the demo, so <b>New from routing</b> opens onto parts a planner can instantiate
/// rather than an empty picker.
///
/// Every operation is a machine-driven job archetype from <see cref="FacilityModel"/>: its template
/// targets the machine type the archetype's role resolves to, and asks for the archetype's skills —
/// the same pairing the backlog uses, because a skill on a room or tool target reports "no
/// compatible resource" for the wrong reason. That rules out the fabrication shop, whose welding,
/// cutting and painting are bench and tool work.
/// </summary>
public static class RoutingFactory
{
    private sealed record Step(string Operation, int SetupMinutes, int RunMinutesPerUnit, int LagMinutesAfter = 0);

    private sealed record RoutingSpec(string Name, string Description, Step[] Steps);

    private static readonly RoutingSpec[] Routings =
    [
        new("Drive shaft DS-200", "Machined at Precision Manufacturing (PMF): turned, milled, then drilled.",
        [
            new("Turn shaft components", 45, 12),
            new("Machine precision components", 60, 18),
            new("Drill fixture holes", 20, 6),
        ]),
        new("Aerospace bracket AB-310", "Machined at Precision Manufacturing (PMF) on the 5-axis cell.",
        [
            // An hour for the part to cool and be deburred before it is fixtured for drilling.
            new("Mill 5-axis aerospace parts", 90, 35, LagMinutesAfter: 60),
            new("Drill fixture holes", 20, 8),
        ]),
        new("Product unit PU-100", "Built and tested at Assembly & Test (PPF).",
        [
            new("Assemble product units", 30, 20),
            new("Test finished units", 15, 10),
        ]),
    ];

    /// <summary>
    /// Writes one request template per distinct operation, then the routings over them. Returns
    /// the number of routings. Throws when a named operation is no longer a machine-driven
    /// archetype, so a renamed archetype breaks the seed instead of silently dropping a step.
    /// </summary>
    public static async Task<int> SeedAsync(
        NpgsqlConnection conn,
        IReadOnlyDictionary<string, Guid> machineTypeIds,
        IReadOnlyDictionary<string, Guid> skillCriteria)
    {
        var now = DateTime.UtcNow;
        var templateIds = new Dictionary<string, Guid>();

        foreach (var op in Routings.SelectMany(r => r.Steps).Select(s => s.Operation).Distinct())
        {
            var arch = Archetype(op);
            var typeKey = MachineCatalog.All.First(m => m.Role == arch.MachineRole).TypeKey;
            var id = templateIds[op] = Guid.NewGuid();

            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO public.templates " +
                "(id, name, description, entity_type, duration_value, duration_unit, " +
                " fixed_start, fixed_end, fixed_duration, created_at, updated_at) " +
                "VALUES (@id, @name, 'Routing operation.', 'request', @duration, 'hours', false, false, true, @now, @now)", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("name", op);
                cmd.Parameters.AddWithValue("duration", arch.MinHours);
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO public.template_target_resource_types (template_id, resource_type_id) VALUES (@id, @typeId)", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("typeId", machineTypeIds[typeKey]);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var skill in arch.RequiredSkills)
            {
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO public.template_items (id, template_id, criterion_id, value, created_at, updated_at) " +
                    "VALUES (@id, @templateId, @criterionId, @value, @now, @now)", conn);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("templateId", id);
                cmd.Parameters.AddWithValue("criterionId", skillCriteria[skill]);
                cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = "true" });
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        foreach (var routing in Routings)
        {
            var routingId = Guid.NewGuid();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO public.routings (id, name, description, created_at, updated_at) " +
                "VALUES (@id, @name, @description, @now, @now)", conn))
            {
                cmd.Parameters.AddWithValue("id", routingId);
                cmd.Parameters.AddWithValue("name", routing.Name);
                cmd.Parameters.AddWithValue("description", routing.Description);
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            for (var i = 0; i < routing.Steps.Length; i++)
            {
                var step = routing.Steps[i];
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO public.routing_steps " +
                    "(routing_id, step_no, operation_template_id, setup_minutes, run_minutes_per_unit, lag_minutes_after) " +
                    "VALUES (@routingId, @stepNo, @templateId, @setup, @run, @lag)", conn);
                cmd.Parameters.AddWithValue("routingId", routingId);
                cmd.Parameters.AddWithValue("stepNo", i + 1);
                cmd.Parameters.AddWithValue("templateId", templateIds[step.Operation]);
                cmd.Parameters.AddWithValue("setup", step.SetupMinutes);
                cmd.Parameters.AddWithValue("run", step.RunMinutesPerUnit);
                cmd.Parameters.AddWithValue("lag", step.LagMinutesAfter);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        return Routings.Length;
    }

    /// <summary>The machine-driven archetype named "{Verb} {Noun}", from any facility.</summary>
    private static JobArchetype Archetype(string operation) =>
        FacilityModel.All
            .SelectMany(f => f.Archetypes)
            .FirstOrDefault(a => $"{a.Verb} {a.Noun}" == operation && a.MachineRole is not null)
        ?? throw new InvalidOperationException($"Routing operation '{operation}' is not a machine-driven job archetype.");
}
