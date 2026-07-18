using Api.Constants;
using Api.Models.Reporting;
using Api.Repositories;
using Api.Services;
using Api.Services.Reporting;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed regression test for the capacity-vs-demand reporting query. A resource with multiple
/// assignments must report its availability ONCE per group it belongs to — not once per assignment.
/// The previous single-statement join Cartesian-inflated available_hours by the assignment count;
/// this pins the corrected (de-inflated) value.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportingCapacityVsDemandIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ReportingCapacityVsDemandIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CapacityVsDemand_ResourceWithMultipleAssignments_AvailabilityCountedOncePerGroup()
    {
        var factory = _fixture.CreateConnectionFactory();
        var org = new OrgContext
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "test-tenant",
            DbConnectionString = _fixture.TestTenantConnectionString,
        };
        var resources = new ResourceRepository(org, factory);
        var groups = new ResourceGroupRepository(org, factory);
        var members = new ResourceGroupMemberRepository(org, factory);

        var from = new DateTime(2099, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddHours(10); // periodHours = 10

        // Tool resource @100% availability, in ONE unique group so the assertion is isolated from
        // any other tenant data (which lands in different (type, group) cells).
        var toolTypeId = await ResourceTypeIdAsync("tool");
        var resource = await resources.CreateAsync(
            toolTypeId, "tool", $"Tool-{Guid.NewGuid():N}", null, null, AllocationModes.Fractional, 100);
        var group = await groups.CreateAsync("tool", $"CapGrp-{Guid.NewGuid():N}", null, 100, null, null);
        await members.SetMembersAsync(group.Id, new[] { resource.Id });

        // Two non-cancelled assignments, each tied to a 1-hour request inside the window.
        var req1 = await SeedRequestAsync(from.AddHours(1), from.AddHours(2));
        var req2 = await SeedRequestAsync(from.AddHours(3), from.AddHours(4));
        await SeedAssignmentAsync(req1, resource.Id, from.AddHours(1), from.AddHours(2));
        await SeedAssignmentAsync(req2, resource.Id, from.AddHours(3), from.AddHours(4));

        try
        {
            var svc = new ReportingQueryService(factory);
            var tenant = new TenantContext
            {
                TenantId = Guid.NewGuid(),
                TenantSlug = "test-tenant",
                TenantDbConnectionString = _fixture.TestTenantConnectionString,
                Status = "active",
            };

            var result = await svc.GetCapacityVsDemandAsync(
                tenant, new ReportingQuery { From = from, To = to, PageSize = 5000 });

            var row = result.Items.Single(r => r.ResourceGroupName == group.Name);

            // available = 100%/100 × 10h = 10h — NOT 20h (would be 2× under the old Cartesian join).
            row.AvailableHours.Should().Be(10d);
            row.AllocatedHours.Should().Be(2d); // two 1-hour assignments
            row.DemandHours.Should().Be(2d);    // two 1-hour requests
        }
        finally
        {
            await CleanupAsync(group.Id, resource.Id, req1, req2);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> ResourceTypeIdAsync(string key)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id FROM resource_types WHERE key = @key", conn);
        cmd.Parameters.AddWithValue("key", key);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> SeedRequestAsync(DateTime startTs, DateTime endTs)
    {
        var id = Guid.NewGuid();
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO requests
                (id, name, site_id, status, start_ts, end_ts, minimal_duration_value, minimal_duration_unit,
                 planning_mode, created_at, updated_at)
            VALUES
                (@id, @name, NULL, 'planned', @startTs, @endTs, 60, 'minutes', 'leaf', @now, @now)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Req {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("startTs", startTs);
        cmd.Parameters.AddWithValue("endTs", endTs);
        cmd.Parameters.AddWithValue("now", startTs);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedAssignmentAsync(Guid requestId, Guid resourceId, DateTime startUtc, DateTime endUtc)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resource_assignments (id, request_id, resource_id, start_utc, end_utc, assignment_status)
            VALUES (gen_random_uuid(), @requestId, @resourceId, @start, @end, 'Planned')", conn);
        cmd.Parameters.AddWithValue("requestId", requestId);
        cmd.Parameters.AddWithValue("resourceId", resourceId);
        cmd.Parameters.AddWithValue("start", startUtc);
        cmd.Parameters.AddWithValue("end", endUtc);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(Guid groupId, Guid resourceId, Guid req1, Guid req2)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM resource_assignments WHERE resource_id = @r;
            DELETE FROM resource_group_members WHERE resource_id = @r;
            DELETE FROM requests WHERE id = ANY(@reqs);
            DELETE FROM resources WHERE id = @r;
            DELETE FROM resource_groups WHERE id = @g;", conn);
        cmd.Parameters.AddWithValue("r", resourceId);
        cmd.Parameters.AddWithValue("g", groupId);
        cmd.Parameters.AddWithValue("reqs", new[] { req1, req2 });
        await cmd.ExecuteNonQueryAsync();
    }
}
