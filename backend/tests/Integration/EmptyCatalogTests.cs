using Api.Models;
using Api.Models.Preset;
using Api.Repositories;
using Api.Services;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// What the two write paths that need "a placeable type" do when the tenant has none.
///
/// This became an ordinary state on this branch: no resource type is built in any more, so a
/// tenant between signing up and activating their first type has an empty catalog. Both paths
/// refuse, and both must refuse the same way — as something the admin can act on (400), not as
/// an unhandled fault (500).
///
/// Runs against the isolated integration database rather than the shared HTTP fixture: it has to
/// deactivate every placeable type for the duration, which no test sharing a database with
/// others could do safely. The flag is restored in a finally, and nothing is deleted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmptyCatalogTests
{
    private readonly PostgresFixture _fixture;

    public EmptyCatalogTests(PostgresFixture fixture) => _fixture = fixture;

    private OrgContext Org() => new()
    {
        OrgId = Guid.NewGuid(),
        OrgSlug = "test-tenant",
        DbConnectionString = _fixture.TestTenantConnectionString,
    };

    /// <summary>Runs <paramref name="body"/> with every placeable type deactivated.</summary>
    private async Task WithNoPlaceableTypesAsync(Func<Task> body)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();

        async Task SetActiveAsync(bool active)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE resource_types SET is_active = @active WHERE has_geometry", conn);
            cmd.Parameters.AddWithValue("active", active);
            await cmd.ExecuteNonQueryAsync();
        }

        await SetActiveAsync(false);
        try
        {
            await body();
        }
        finally
        {
            await SetActiveAsync(true);
        }
    }

    [Fact]
    public async Task CreatingARequestWithoutTargets_IsRefused_RatherThanTargetingNothing()
    {
        // An empty target list is a real state — a request needing no resource — so it must not
        // be reachable by omission. With nothing to fall back to, the create refuses.
        var requests = new RequestRepository(Org(), _fixture.CreateConnectionFactory());

        await WithNoPlaceableTypesAsync(async () =>
        {
            var thrown = await Assert.ThrowsAsync<ArgumentException>(() =>
                requests.CreateAsync(new CreateRequestRequest
                {
                    Name = $"Needs-a-place-{Guid.NewGuid():N}"[..24],
                    MinimalDurationValue = 1,
                    MinimalDurationUnit = DurationUnit.Hours,
                    SchedulingSettingsApply = false,
                }));

            // ArgumentException maps to 400 at the boundary; the message has to name the fix.
            Assert.Contains("Activate one", thrown.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ApplyingAPresetWithGroups_IsRefused_AsABadRequestRatherThanAFault()
    {
        // A preset's groups belong to a placeable type. With none activated the preset cannot be
        // applied — but this is the admin's own tenant state, not a bug, so it must not arrive as
        // the 500 an InvalidOperationException maps to.
        var preset = new Preset
        {
            PresetId = $"test-{Guid.NewGuid():N}",
            Name = "Empty catalog",
            Version = "1.0.0",
            Contents = new PresetContents
            {
                SpaceGroups =
                [
                    new PresetSpaceGroup { Key = "hall-1", Name = $"Hall {Guid.NewGuid():N}"[..12] },
                ],
            },
        };

        await WithNoPlaceableTypesAsync(async () =>
        {
            await using var conn = await _fixture.OpenTestTenantConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                PresetApplier.ApplyAsync(conn, tx, preset));

            await tx.RollbackAsync();
        });
    }
}
