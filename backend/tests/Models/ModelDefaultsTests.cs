using Api.Models;
using Api.Models.Export;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Model defaults that decide behaviour when a caller leaves the field out.
/// </summary>
public class ModelDefaultsTests
{
    [Fact]
    public void SearchResultPermissions_DefaultToReadOnly()
    {
        var perms = new SearchResultPermissions();

        perms.CanRead.Should().BeTrue();
        perms.CanEdit.Should().BeFalse();
    }

    [Fact]
    public void ExportRequest_DefaultsToMasterDataOfEverySite()
    {
        var req = new ExportRequest();

        req.IncludeMasterData.Should().BeTrue();
        req.IncludePlanningData.Should().BeFalse();
        req.SiteIds.Should().BeNull();
    }

    [Fact]
    public void Feedback_StartsAsNew()
    {
        var feedback = new Feedback { Id = Guid.NewGuid(), FeedbackType = "bug", Title = "Broken button" };

        feedback.Status.Should().Be("new");
    }
}
