using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class ResourceScanCodeServiceTests
{
    private readonly Mock<IResourceScanCodeRepository> _codes = new();
    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IResourceTypeRepository> _types = new();
    private readonly ResourceScanCodeService _service;

    private static readonly Guid TypeId = Guid.NewGuid();
    private static readonly ResourceInfo Drill = Resource("Drill");
    private static readonly ResourceInfo Mill = Resource("Mill");

    public ResourceScanCodeServiceTests()
    {
        _resources.Setup(r => r.GetByIdAsync(Drill.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Drill);
        _resources.Setup(r => r.GetByIdAsync(Mill.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Mill);
        SetTypeEnabled(true);
        _service = new ResourceScanCodeService(_codes.Object, _resources.Object, _types.Object);
    }

    private static ResourceInfo Resource(string name) => new()
    {
        Id = Guid.NewGuid(),
        ResourceTypeId = TypeId,
        ResourceTypeKey = "machine",
        Name = name,
        AllocationMode = "Exclusive",
        BaseAvailabilityPercent = 100,
        IsActive = true,
    };

    private void SetTypeEnabled(bool enabled) =>
        _types.Setup(t => t.GetByIdAsync(TypeId, It.IsAny<CancellationToken>())).ReturnsAsync(new ResourceTypeInfo
        {
            Id = TypeId,
            Key = "machine",
            DisplayName = "Machine",
            DisplayNamePlural = "Machines",
            HasGeometry = false,
            HasDirectoryProfile = false,
            SingleGroupMembership = false,
            ScanCodesEnabled = enabled,
            IsSystem = false,
            IsActive = true,
        });

    private static ResourceScanCodeInfo Code(ResourceInfo owner, string code) =>
        new() { Id = Guid.NewGuid(), ResourceId = owner.Id, Code = code };

    private static ScanCodeMatch Match(ResourceInfo owner, string code, bool enabled = true) => new(
        Code(owner, code),
        new ScanCodeResourceRef { Id = owner.Id, Name = owner.Name, ResourceTypeKey = owner.ResourceTypeKey, IsActive = true },
        enabled);

    private static LinkResourceScanCodeRequest Link(string code, bool move = false) =>
        new() { Code = code, MoveFromOtherResource = move };

    private void UpsertReturns(ResourceScanCodeInfo? result, bool move = false) =>
        _codes.Setup(c => c.UpsertAsync(Drill.Id, "A", It.IsAny<Guid?>(), move, It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private void NoUpsert() =>
        _codes.Verify(c => c.UpsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Lookup_TrimsTheCode_AndNamesTheLinkedResource()
    {
        _codes.Setup(c => c.GetByCodeAsync("ABC", It.IsAny<CancellationToken>())).ReturnsAsync(Match(Drill, "ABC"));

        var result = await _service.LookupAsync("  ABC \n");

        Assert.Equal(ScanCodeLookupStatus.Linked, result.Status);
        Assert.Equal(Drill.Id, result.Resource!.Id);
    }

    [Fact]
    public async Task Lookup_TypeSwitchedOff_HidesTheResource()
    {
        _codes.Setup(c => c.GetByCodeAsync("ABC", It.IsAny<CancellationToken>())).ReturnsAsync(Match(Drill, "ABC", enabled: false));

        var result = await _service.LookupAsync("ABC");

        Assert.Equal(ScanCodeLookupStatus.TypeDisabled, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Link_TypeSwitchedOff_IsRefused()
    {
        SetTypeEnabled(false);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.LinkAsync(Drill.Id, Link("A"), null));
        NoUpsert();
    }

    [Fact]
    public async Task Link_NewCode_StoresTheTrimmedText()
    {
        var user = Guid.NewGuid();
        var inserted = Code(Drill, "A");
        _codes.Setup(c => c.UpsertAsync(Drill.Id, "A", user, false, It.IsAny<CancellationToken>())).ReturnsAsync(inserted);

        Assert.Same(inserted, await _service.LinkAsync(Drill.Id, Link(" A "), user));
    }

    [Fact]
    public async Task Link_CodeOfThisResource_ReturnsTheExistingLink()
    {
        var match = Match(Drill, "A");
        UpsertReturns(null);
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(match);

        Assert.Same(match.Code, await _service.LinkAsync(Drill.Id, Link("A"), null));
    }

    [Fact]
    public async Task Link_CodeOfAnotherResource_NamesItAndDoesNotMove()
    {
        UpsertReturns(null);
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(Match(Mill, "A"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.LinkAsync(Drill.Id, Link("A"), null));

        Assert.Contains("Mill", ex.Message);
    }

    [Fact]
    public async Task Link_MoveFromOtherResource_TakesTheCode()
    {
        var moved = Code(Drill, "A");
        UpsertReturns(moved, move: true);

        Assert.Same(moved, await _service.LinkAsync(Drill.Id, Link("A", move: true), null));
        _codes.Verify(c => c.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Link_CodeThatVanishesMidRace_IsAConflict()
    {
        UpsertReturns(null);
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync((ScanCodeMatch?)null);

        await Assert.ThrowsAsync<ConflictException>(() => _service.LinkAsync(Drill.Id, Link("A"), null));
    }
}
