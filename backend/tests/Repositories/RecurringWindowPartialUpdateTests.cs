using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Availability events and resource absences update only the fields a request carries. The
/// old read-merge-write rewrote every column from a read taken before the UPDATE, so a
/// concurrent change to another field committed in between was silently undone.
/// </summary>
[Collection("Database collection")]
public class RecurringWindowPartialUpdateTests(DatabaseFixture fixture)
{
    private static readonly DateTime Start = new(2031, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Holds <paramref name="concurrentSql"/> uncommitted on its own connection while the
    /// repository update runs, then commits it, and returns what the update answered.
    /// </summary>
    private async Task<T?> UpdateWhileAnotherWriterHoldsTheRowAsync<T>(
        string concurrentSql, Guid id, Func<Task<T?>> update)
    {
        await using var conn = new NpgsqlConnection(fixture.TenantConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(concurrentSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        var pending = update();
        await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(500)));
        await tx.CommitAsync();
        return await pending;
    }

    [Fact]
    public async Task AvailabilityEvent_UpdatingTheTitle_KeepsAConcurrentEnabledChange()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAvailabilityEventRepository>();
        var siteId = await TestHelpers.GetOrCreateTestSite(fixture.CreateAuthorizedClient());
        var created = await repo.CreateAsync(siteId, new CreateAvailabilityEventRequest
        {
            Title = "Before",
            StartTs = Start,
            EndTs = Start.AddHours(2),
        });

        var updated = await UpdateWhileAnotherWriterHoldsTheRowAsync(
            "UPDATE availability_events SET enabled = false WHERE id = @id", created.Id,
            () => repo.UpdateAsync(siteId, created.Id, new UpdateAvailabilityEventRequest { Title = "After" }));

        updated!.Title.Should().Be("After");
        updated.Enabled.Should().BeFalse("the title update must not rewrite a column it was not given");
        await repo.DeleteAsync(siteId, created.Id);
    }

    [Fact]
    public async Task ResourceAbsence_UpdatingTheTitle_KeepsAConcurrentNotesChange()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IResourceAbsenceRepository>();
        var resourceId = await TestHelpers.GetOrCreateTestSpace(fixture.CreateAuthorizedClient());
        var created = await repo.CreateAsync(resourceId, new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Maintenance,
            Title = "Before",
            StartTs = Start,
            EndTs = Start.AddHours(2),
        });

        var updated = await UpdateWhileAnotherWriterHoldsTheRowAsync(
            "UPDATE resource_absences SET notes = 'concurrent' WHERE id = @id", created.Id,
            () => repo.UpdateAsync(resourceId, created.Id, new UpdateResourceAbsenceRequest { Title = "After" }));

        updated!.Title.Should().Be("After");
        updated.Notes.Should().Be("concurrent", "the title update must not rewrite a column it was not given");
        await repo.DeleteAsync(resourceId, created.Id);
    }

    [Fact]
    public async Task ResourceAbsence_SwitchingRecurrenceOff_ClearsTheRule_AndARuleAloneNeedsARecurringWindow()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IResourceAbsenceRepository>();
        var resourceId = await TestHelpers.GetOrCreateTestSpace(fixture.CreateAuthorizedClient());
        var created = await repo.CreateAsync(resourceId, new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Maintenance,
            Title = "Weekly",
            StartTs = Start,
            EndTs = Start.AddHours(2),
            IsRecurring = true,
            RecurrenceRule = "FREQ=WEEKLY",
        });

        var kept = await repo.UpdateAsync(resourceId, created.Id, new UpdateResourceAbsenceRequest { Title = "Weekly 2" });
        kept!.RecurrenceRule.Should().Be("FREQ=WEEKLY");

        var off = await repo.UpdateAsync(resourceId, created.Id, new UpdateResourceAbsenceRequest { IsRecurring = false });
        off!.IsRecurring.Should().BeFalse();
        off.RecurrenceRule.Should().BeNull();

        var ruleOnly = await repo.UpdateAsync(resourceId, created.Id, new UpdateResourceAbsenceRequest { RecurrenceRule = "FREQ=DAILY" });
        ruleOnly!.RecurrenceRule.Should().BeNull("a rule applies only to a recurring window");

        var unchanged = await repo.UpdateAsync(resourceId, created.Id, new UpdateResourceAbsenceRequest());
        unchanged!.Title.Should().Be("Weekly 2");
        (await repo.UpdateAsync(resourceId, Guid.NewGuid(), new UpdateResourceAbsenceRequest { Title = "x" })).Should().BeNull();
        // Another resource's absence is not found through this resource.
        (await repo.UpdateAsync(Guid.NewGuid(), created.Id, new UpdateResourceAbsenceRequest { Title = "x" })).Should().BeNull();
        (await repo.DeleteAsync(Guid.NewGuid(), created.Id)).Should().BeFalse();
        await repo.DeleteAsync(resourceId, created.Id);
    }

    [Fact]
    public async Task ResourceAbsence_ForAMissingResource_IsNotFound()
    {
        // The MCP tool used to skip the resource check the HTTP endpoint made; the repository makes it now.
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IResourceAbsenceRepository>();

        var act = () => repo.CreateAsync(Guid.NewGuid(), new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Maintenance,
            Title = "Nowhere",
            StartTs = Start,
            EndTs = Start.AddHours(2),
        });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AvailabilityEvent_OfAnotherSite_IsNotFound()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAvailabilityEventRepository>();
        var siteId = await TestHelpers.GetOrCreateTestSite(fixture.CreateAuthorizedClient());
        var created = await repo.CreateAsync(siteId, new CreateAvailabilityEventRequest
        {
            Title = "Scoped",
            StartTs = Start,
            EndTs = Start.AddHours(2),
        });
        var otherSite = Guid.NewGuid();

        (await repo.GetByIdAsync(otherSite, created.Id)).Should().BeNull();
        (await repo.UpdateAsync(otherSite, created.Id, new UpdateAvailabilityEventRequest { Title = "x" })).Should().BeNull();
        (await repo.AddScopeAsync(otherSite, created.Id, new AddScopeRequest
        {
            TargetType = ScopeTargetType.Resource,
            TargetId = Guid.NewGuid(),
            Effect = ScopeEffect.Closed,
        })).Should().BeNull();
        (await repo.DeleteAsync(otherSite, created.Id)).Should().BeFalse();
        (await repo.GetByIdAsync(siteId, created.Id)).Should().NotBeNull();
        (await repo.DeleteAsync(siteId, created.Id)).Should().BeTrue();
    }
}
