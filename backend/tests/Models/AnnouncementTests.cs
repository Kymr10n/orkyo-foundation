using Xunit;
using FluentAssertions;
using Api.Models;

namespace Api.Tests.Models;

public class AnnouncementTests
{
    [Fact]
    public void AnnouncementDto_IsExpired_True_WhenExpiresAtInPast()
    {
        var dto = new AnnouncementDto
        {
            Id = Guid.NewGuid(),
            Title = "Old",
            Body = "Expired",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
        };

        dto.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void AnnouncementDto_IsExpired_False_WhenExpiresAtInFuture()
    {
        var dto = new AnnouncementDto
        {
            Id = Guid.NewGuid(),
            Title = "Active",
            Body = "Still valid",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        };

        dto.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Announcement_DefaultRevision_IsOne()
    {
        var a = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Body = "Body",
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
        };

        a.Revision.Should().Be(1);
    }

    [Fact]
    public void CreateAnnouncementRequest_RetentionDays_NullByDefault()
    {
        var req = new CreateAnnouncementRequest { Title = "T", Body = "B" };

        req.RetentionDays.Should().BeNull();
        req.IsImportant.Should().BeFalse();
    }

    [Fact]
    public void UpdateAnnouncementRequest_ExpiresAt_NullByDefault()
    {
        var req = new UpdateAnnouncementRequest { Title = "T", Body = "B" };

        req.ExpiresAt.Should().BeNull();
        req.IsImportant.Should().BeFalse();
    }
}
