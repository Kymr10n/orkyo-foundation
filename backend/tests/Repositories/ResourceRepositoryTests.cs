using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Integration coverage for the derived, read-only <see cref="ResourceInfo.CurrentSiteId"/>.
/// "Current site" is no longer stored (migration 1560); it is computed in the resource read query:
/// a space resolves to its own site; a person/tool resolves to the site of a non-cancelled assignment
/// overlapping now(), else its home site. These tests exercise every branch of that COALESCE against
/// a real tenant DB. Rows are committed (the repository opens its own connection), so each test uses
/// unique entities — mirroring the other repository integration tests.
/// </summary>
[Collection("Database collection")]
public class ResourceRepositoryTests
{
    private readonly IResourceService _resources;
    private readonly IResourceRepository _repo;
    private readonly IRequestRepository _requests;
    private readonly IResourceAssignmentRepository _assignments;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public ResourceRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _resources = scope.ServiceProvider.GetRequiredService<IResourceService>();
        _repo = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        _requests = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
        _assignments = scope.ServiceProvider.GetRequiredService<IResourceAssignmentRepository>();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_PersonWithNoAssignments_CurrentSiteIsHome()
    {
        var siteA = await CreateSiteAsync("A");
        var personId = await CreatePersonAsync(homeSiteId: siteA);

        var person = await _resources.GetByIdAsync(personId);

        Assert.NotNull(person);
        Assert.Equal(siteA, person.HomeSiteId);
        Assert.Equal(siteA, person.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_PersonAssignedNowAtOtherSite_CurrentSiteIsAssignmentSite()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var person = await _resources.GetByIdAsync(personId);

        // Home stays the anchor; current reflects where the live assignment puts them.
        Assert.Equal(siteA, person!.HomeSiteId);
        Assert.Equal(siteB, person.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_AssignmentCancelled_CurrentSiteFallsBackToHome()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        await SetAssignmentsCancelledAsync(personId);

        var person = await _resources.GetByIdAsync(personId);

        Assert.Equal(siteA, person!.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_AssignmentNotOverlappingNow_CurrentSiteFallsBackToHome()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        // Entirely in the future — does not contain now().
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2));

        var person = await _resources.GetByIdAsync(personId);

        Assert.Equal(siteA, person!.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_SiteNeutralRequest_CurrentSiteFallsBackToHome()
    {
        var siteA = await CreateSiteAsync("A");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: null); // site-neutral — does not pin a location
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var person = await _resources.GetByIdAsync(personId);

        Assert.Equal(siteA, person!.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_PersonWithNoHomeAndNoAssignment_CurrentSiteIsNull()
    {
        var personId = await CreatePersonAsync(homeSiteId: null);

        var person = await _resources.GetByIdAsync(personId);

        Assert.Null(person!.HomeSiteId);
        Assert.Null(person.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_Space_CurrentSiteIsSpaceSite()
    {
        var siteA = await CreateSiteAsync("A");
        var space = await _resources.CreateAsync(new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            Name = $"Space-{Guid.NewGuid():N}"[..20],
            Code = $"SP-{Guid.NewGuid():N}"[..12],
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteA,
            CrossSiteAllowed = false,
        });

        var resource = await _resources.GetByIdAsync(space.Id);

        // Spaces are immovable, and their site is now simply their home site — the whole point
        // of the fold. Current still resolves to it, so the reported location is unchanged.
        Assert.Equal(siteA, resource!.HomeSiteId);
        Assert.Equal(siteA, resource.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_ImmovableResourceAssignedElsewhere_CurrentSiteStaysHome()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var toolId = await CreateImmovableToolAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(toolId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var tool = await _resources.GetByIdAsync(toolId);

        // cross_site_allowed = false: the assignment says something about the request, not the
        // resource's location.
        Assert.Equal(siteA, tool!.CurrentSiteId);
    }

    [Fact]
    public async Task GetById_TwoOverlappingAssignments_LatestStartWins()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var siteC = await CreateSiteAsync("C");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var earlier = await CreateRequestAsync(siteId: siteB);
        var later = await CreateRequestAsync(siteId: siteC);
        await AssignAsync(personId, earlier, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(1));
        await AssignAsync(personId, later, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var person = await _resources.GetByIdAsync(personId);

        Assert.Equal(siteC, person!.CurrentSiteId);
    }

    [Fact]
    public async Task GetAll_SiteFilterWithoutWindow_UsesAsOfNowCurrentSite()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var atB = await _repo.GetAllAsync(new ResourceListFilter
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            IsActive = true,
            SiteId = siteB,
        });
        var atA = await _repo.GetAllAsync(new ResourceListFilter
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            IsActive = true,
            SiteId = siteA,
        });

        // Currently working at B → listed under B; homed at A → still listed under A too.
        Assert.Contains(atB, r => r.Id == personId);
        Assert.Contains(atA, r => r.Id == personId);
        Assert.Equal(siteB, atB.Single(r => r.Id == personId).CurrentSiteId);
    }

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPage_ReturnsSliceAndUnpagedTotal()
    {
        var marker = $"Paged-{Guid.NewGuid():N}"[..18];
        for (var i = 0; i < 5; i++)
        {
            await _resources.CreateAsync(new CreateResourceRequest
            {
                ResourceTypeKey = ResourceTypeKeys.Person,
                Name = $"{marker}-{i}",
                AllocationMode = AllocationModes.Exclusive,
                BaseAvailabilityPercent = 100,
            });
        }
        var filter = new ResourceListFilter { Search = marker };

        var (pageOne, total) = await _repo.GetPageAsync(filter, limit: 2, offset: 0);
        var (pageTwo, _) = await _repo.GetPageAsync(filter, limit: 2, offset: 2);
        var (pastEnd, pastEndTotal) = await _repo.GetPageAsync(filter, limit: 2, offset: 10);

        Assert.Equal(5, total);
        Assert.Equal(2, pageOne.Count);
        Assert.Equal(2, pageTwo.Count);
        Assert.Empty(pastEnd);
        Assert.Equal(5, pastEndTotal);
        // Stable order (name, id): the pages do not overlap.
        Assert.Empty(pageOne.Select(r => r.Id).Intersect(pageTwo.Select(r => r.Id)));
    }

    [Fact]
    public async Task GetPage_SiteFilterWithoutWindow_CountMatchesItems()
    {
        // The site filter references the current-site lateral, so the COUNT must run over the
        // same FROM — this pins the UsesCurrentSite branch of the count query.
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(personId, requestId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        var (items, total) = await _repo.GetPageAsync(new ResourceListFilter
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            IsActive = true,
            SiteId = siteB,
        }, limit: 100, offset: 0);

        Assert.Contains(items, r => r.Id == personId);
        Assert.Equal(items.Count, total);
    }

    // ── site-window membership filter (drives the People utilization grid) ──────

    [Fact]
    public async Task GetAll_SiteWindowFilter_IncludesHomeSitePerson_ExcludesOtherSite()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var (from, to) = Window();

        Assert.Contains(await ListPeopleAtSite(siteA, from, to), r => r.Id == personId);
        Assert.DoesNotContain(await ListPeopleAtSite(siteB, from, to), r => r.Id == personId);
    }

    [Fact]
    public async Task GetAll_SiteWindowFilter_IncludesCrossSiteAssignmentOverlappingWindow()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        var (from, to) = Window();
        await AssignAsync(personId, requestId, from.AddMinutes(30), to.AddMinutes(-30));

        // Homed at A but working at B during the window → appears under B as well as A.
        Assert.Contains(await ListPeopleAtSite(siteB, from, to), r => r.Id == personId);
        Assert.Contains(await ListPeopleAtSite(siteA, from, to), r => r.Id == personId);
    }

    [Fact]
    public async Task GetAll_SiteWindowFilter_ExcludesAssignmentOutsideWindow()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var requestId = await CreateRequestAsync(siteId: siteB);
        var (from, to) = Window();
        await AssignAsync(personId, requestId, to.AddDays(1), to.AddDays(2)); // entirely after the window

        Assert.DoesNotContain(await ListPeopleAtSite(siteB, from, to), r => r.Id == personId);
    }

    [Fact]
    public async Task GetAll_SiteWindowFilter_ExcludesCancelledAndSiteNeutralAssignments()
    {
        var siteA = await CreateSiteAsync("A");
        var siteB = await CreateSiteAsync("B");
        var personId = await CreatePersonAsync(homeSiteId: siteA);
        var (from, to) = Window();

        var cancelledReq = await CreateRequestAsync(siteId: siteB);
        await AssignAsync(personId, cancelledReq, from.AddMinutes(30), to.AddMinutes(-30));
        await SetAssignmentsCancelledAsync(personId);

        var neutralReq = await CreateRequestAsync(siteId: null);
        await AssignAsync(personId, neutralReq, from.AddMinutes(30), to.AddMinutes(-30));

        // Neither a cancelled assignment nor a site-neutral request pulls the person into site B.
        Assert.DoesNotContain(await ListPeopleAtSite(siteB, from, to), r => r.Id == personId);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static (DateTime from, DateTime to) Window()
    {
        var now = DateTime.UtcNow;
        return (now.AddHours(-2), now.AddHours(2));
    }

    private Task<List<ResourceInfo>> ListPeopleAtSite(Guid siteId, DateTime from, DateTime to) =>
        _repo.GetAllAsync(new ResourceListFilter
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            IsActive = true,
            SiteId = siteId,
            SiteWindowFrom = from,
            SiteWindowTo = to,
        });


    // ── code uniqueness ───────────────────────────────────────────────────────

    [Fact]
    public async Task Update_CannotTakeACodeAlreadyUsedAtTheSameSite()
    {
        // Create refuses a taken code. An update that did not would be the way around it, and the
        // floorplan and import paths that key on (site, code) would then have two answers.
        var site = await CreateSiteAsync("C");
        var first = await CreateSpaceAsync(site, $"A-{Guid.NewGuid():N}"[..10]);
        var second = await CreateSpaceAsync(site, $"B-{Guid.NewGuid():N}"[..10]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _repo.UpdateAsync(second.Id, new UpdateResourceRequest { Code = first.Code! }));
    }

    [Fact]
    public async Task Update_KeepingItsOwnCode_IsNotAClashWithItself()
    {
        var site = await CreateSiteAsync("D");
        var space = await CreateSpaceAsync(site, $"K-{Guid.NewGuid():N}"[..10]);

        var updated = await _repo.UpdateAsync(space.Id, new UpdateResourceRequest
        {
            Code = space.Code!,
            Name = $"Renamed-{Guid.NewGuid():N}"[..16],
        });

        Assert.Equal(space.Code, updated!.Code);
    }

    [Fact]
    public async Task Update_MovingToASiteWhereTheCodeIsTaken_IsRefused()
    {
        // The check is against the site the resource will have, not the one it has now.
        var siteA = await CreateSiteAsync("E");
        var siteB = await CreateSiteAsync("F");
        var shared = $"MOVE-{Guid.NewGuid():N}"[..10];

        await CreateSpaceAsync(siteB, shared);
        var mover = await CreateSpaceAsync(siteA, shared);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _repo.UpdateAsync(mover.Id, new UpdateResourceRequest { HomeSiteId = siteB }));
    }

    private async Task<ResourceInfo> CreateSpaceAsync(Guid siteId, string code) =>
        await _resources.CreateAsync(new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            Name = $"Space-{Guid.NewGuid():N}"[..20],
            Code = code,
            AllocationMode = AllocationModes.Exclusive,
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
        });

    private async Task<Guid> CreateSiteAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sites (name, code) VALUES (@n, @c) RETURNING id", conn);
        cmd.Parameters.AddWithValue("n", $"Site {label} {suffix}");
        cmd.Parameters.AddWithValue("c", $"{label}{suffix}");
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> CreatePersonAsync(Guid? homeSiteId)
    {
        var person = await _resources.CreateAsync(new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Person,
            Name = $"Person-{Guid.NewGuid():N}"[..20],
            AllocationMode = AllocationModes.Exclusive,
            BaseAvailabilityPercent = 100,
            HomeSiteId = homeSiteId,
        });
        return person.Id;
    }

    private async Task<Guid> CreateImmovableToolAsync(Guid? homeSiteId)
    {
        var tool = await _resources.CreateAsync(new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Tool,
            Name = $"Tool-{Guid.NewGuid():N}"[..20],
            AllocationMode = AllocationModes.Exclusive,
            BaseAvailabilityPercent = 100,
            HomeSiteId = homeSiteId,
            CrossSiteAllowed = false,
        });
        return tool.Id;
    }

    private async Task<Guid> CreateRequestAsync(Guid? siteId)
    {
        var request = await _requests.CreateAsync(new CreateRequestRequest
        {
            Name = $"Req-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
            SiteId = siteId,
        });
        return request.Id;
    }

    // Goes through the assignment repository (not the service), so it inserts directly without
    // running cross-site validation — the setup just needs the row to exist.
    private Task AssignAsync(Guid personId, Guid requestId, DateTime startUtc, DateTime endUtc) =>
        _assignments.CreateAsync(new CreateResourceAssignmentRequest
        {
            ResourceId = personId,
            RequestId = requestId,
            StartUtc = startUtc,
            EndUtc = endUtc,
        });

    private async Task SetAssignmentsCancelledAsync(Guid resourceId)
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE resource_assignments SET assignment_status = 'Cancelled' WHERE resource_id = @r", conn);
        cmd.Parameters.AddWithValue("r", resourceId);
        await cmd.ExecuteNonQueryAsync();
    }
}
