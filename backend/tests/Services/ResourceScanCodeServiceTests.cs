using Api.Constants;
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

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Lookup_BlankOrOverlong_IsUnknownWithoutAQuery(string? code)
    {
        var result = await _service.LookupAsync(code ?? new string('x', DomainLimits.ResourceScanCodeMaxLength + 1));

        Assert.Equal(ScanCodeLookupStatus.Unknown, result.Status);
        _codes.Verify(c => c.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByResource_UnknownResource_ReturnsNull()
    {
        Assert.Null(await _service.GetByResourceAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Link_UnknownResource_ReportsNotFound()
    {
        var result = await _service.LinkAsync(Guid.NewGuid(), new LinkResourceScanCodeRequest { Code = "A" }, null);

        Assert.Equal(LinkScanCodeOutcome.ResourceNotFound, result.Outcome);
    }

    [Fact]
    public async Task Link_TypeSwitchedOff_IsRefused()
    {
        SetTypeEnabled(false);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A" }, null);

        Assert.Equal(LinkScanCodeOutcome.TypeDisabled, result.Outcome);
        _codes.Verify(c => c.InsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Link_NewCode_InsertsTheTrimmedText()
    {
        var user = Guid.NewGuid();
        var inserted = Code(Drill, "A");
        _codes.Setup(c => c.InsertAsync(Drill.Id, "A", user, It.IsAny<CancellationToken>())).ReturnsAsync(inserted);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = " A " }, user);

        Assert.Equal(LinkScanCodeOutcome.Linked, result.Outcome);
        Assert.Same(inserted, result.Code);
    }

    [Fact]
    public async Task Link_CodeOfThisResource_IsAlreadyLinked()
    {
        var match = Match(Drill, "A");
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(match);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A" }, null);

        Assert.Equal(LinkScanCodeOutcome.AlreadyLinked, result.Outcome);
        Assert.Same(match.Code, result.Code);
    }

    [Fact]
    public async Task Link_CodeOfAnotherResource_NamesItAndDoesNotMove()
    {
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(Match(Mill, "A"));

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A" }, null);

        Assert.Equal(LinkScanCodeOutcome.OwnedByOther, result.Outcome);
        Assert.Equal("Mill", result.OtherResource!.Name);
        _codes.Verify(c => c.ReassignAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Link_MoveFromOtherResource_ReassignsTheCode()
    {
        var match = Match(Mill, "A");
        var moved = Code(Drill, "A");
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _codes.Setup(c => c.ReassignAsync(match.Code.Id, Drill.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(moved);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A", MoveFromOtherResource = true }, null);

        Assert.Equal(LinkScanCodeOutcome.Moved, result.Outcome);
        Assert.Same(moved, result.Code);
    }

    [Fact]
    public async Task Link_MoveOfACodeUnlinkedMeanwhile_LinksItFresh()
    {
        var match = Match(Mill, "A");
        var inserted = Code(Drill, "A");
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(match);
        _codes.Setup(c => c.ReassignAsync(match.Code.Id, Drill.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync((ResourceScanCodeInfo?)null);
        _codes.Setup(c => c.InsertAsync(Drill.Id, "A", null, It.IsAny<CancellationToken>())).ReturnsAsync(inserted);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A", MoveFromOtherResource = true }, null);

        Assert.Equal(LinkScanCodeOutcome.Moved, result.Outcome);
        Assert.Same(inserted, result.Code);
    }

    [Fact]
    public async Task Link_LosingAnInsertRace_AnswersFromTheWinner()
    {
        var winner = Match(Drill, "A");
        _codes.SetupSequence(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScanCodeMatch?)null)
            .ReturnsAsync(winner);
        _codes.Setup(c => c.InsertAsync(Drill.Id, "A", null, It.IsAny<CancellationToken>())).ReturnsAsync((ResourceScanCodeInfo?)null);

        var result = await _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A" }, null);

        Assert.Equal(LinkScanCodeOutcome.AlreadyLinked, result.Outcome);
        Assert.Same(winner.Code, result.Code);
    }

    [Fact]
    public async Task Link_CodeThatVanishesMidRace_IsAConflict()
    {
        _codes.Setup(c => c.GetByCodeAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync((ScanCodeMatch?)null);
        _codes.Setup(c => c.InsertAsync(Drill.Id, "A", null, It.IsAny<CancellationToken>())).ReturnsAsync((ResourceScanCodeInfo?)null);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.LinkAsync(Drill.Id, new LinkResourceScanCodeRequest { Code = "A" }, null));
    }

    [Fact]
    public async Task Unlink_DelegatesToTheOwnerScopedDelete()
    {
        var codeId = Guid.NewGuid();
        _codes.Setup(c => c.DeleteAsync(Drill.Id, codeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await _service.UnlinkAsync(Drill.Id, codeId));
    }
}
