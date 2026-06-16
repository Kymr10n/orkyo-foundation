using Api.Models;
using AwesomeAssertions;
using Xunit;

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

    [Fact]
    public void UserAnnouncementDto_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var dto = new UserAnnouncementDto
        {
            Id = id,
            Title = "System Maintenance",
            Body = "Scheduled downtime on Saturday.",
            IsImportant = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsRead = false
        };

        dto.Id.Should().Be(id);
        dto.Title.Should().Be("System Maintenance");
        dto.IsImportant.Should().BeTrue();
        dto.IsRead.Should().BeFalse();
    }

    [Fact]
    public void UserAnnouncementDto_DefaultValues_AreReadFalseNotImportant()
    {
        var dto = new UserAnnouncementDto { Title = "T", Body = "B" };

        dto.IsRead.Should().BeFalse();
        dto.IsImportant.Should().BeFalse();
    }
}
