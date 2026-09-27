using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the 0%-coverage response/DTO model types:
/// AuditEventDto, ContactRequest, SearchResult/SearchResponse/SearchResultOpen/SearchResultPermissions,
/// TenantSuspendedResponse.
/// </summary>
public class ResponseModelsTests
{
    // ── AuditEventDto ──────────────────────────────────────────────────────

    [Fact]
    public void AuditEventDto_NullableFields_AreNullByDefault()
    {
        var dto = new AuditEventDto
        {
            Id = Guid.NewGuid(),
            ActorType = "system",
            Action = "cleanup",
            CreatedAt = DateTime.UtcNow
        };

        dto.ActorUserId.Should().BeNull();
        dto.TargetType.Should().BeNull();
        dto.TargetId.Should().BeNull();
        dto.Metadata.Should().BeNull();
        dto.RequestId.Should().BeNull();
        dto.IpAddress.Should().BeNull();
    }

    // ── ContactRequest ─────────────────────────────────────────────────────

    [Fact]
    public void ContactRequest_Company_IsOptional()
    {
        var req = new ContactRequest
        {
            Name = "Bob",
            Email = "bob@example.com",
            Subject = "Question",
            Message = "Hello?"
        };

        req.Company.Should().BeNull();
    }

    // ── SearchResult / SearchResponse / nested records ─────────────────────

    [Fact]
    public void SearchResultPermissions_Defaults_CanReadTrueCanEditFalse()
    {
        var perms = new SearchResultPermissions();

        perms.CanRead.Should().BeTrue();
        perms.CanEdit.Should().BeFalse();
    }
}
