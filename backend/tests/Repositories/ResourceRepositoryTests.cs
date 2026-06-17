using Api.Constants;
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
    private readonly ISpaceService _spaces;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public ResourceRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _resources = scope.ServiceProvider.GetRequiredService<IResourceService>();
        _repo = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        _requests = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
        _assignments = scope.ServiceProvider.GetRequiredService<IResourceAssignmentRepository>();
        _spaces = scope.ServiceProvider.GetRequiredService<ISpaceService>();
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
        var space = await _spaces.CreateAsync(
            siteA, name: $"Space-{Guid.NewGuid():N}"[..20], code: $"SP-{Guid.NewGuid():N}"[..12],
            description: null, isPhysical: false, geometry: null, properties: null);

        var resource = await _resources.GetByIdAsync(space.Id);

        // Spaces are immovable: home stays null, current resolves to spaces.site_id.
        Assert.Null(resource!.HomeSiteId);
        Assert.Equal(siteA, resource.CurrentSiteId);
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
