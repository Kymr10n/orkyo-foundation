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
    public void AuditEventDto_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var created = DateTime.UtcNow;

        var dto = new AuditEventDto
        {
            Id = id,
            ActorUserId = actorId,
            ActorType = "user",
            Action = "login",
            TargetType = "session",
            TargetId = "sess-1",
            Metadata = "{\"ip\":\"1.2.3.4\"}",
            RequestId = "req-abc",
            IpAddress = "1.2.3.4",
            CreatedAt = created
        };

        dto.Id.Should().Be(id);
        dto.ActorUserId.Should().Be(actorId);
        dto.ActorType.Should().Be("user");
        dto.Action.Should().Be("login");
        dto.TargetType.Should().Be("session");
        dto.TargetId.Should().Be("sess-1");
        dto.Metadata.Should().Contain("ip");
        dto.RequestId.Should().Be("req-abc");
        dto.IpAddress.Should().Be("1.2.3.4");
        dto.CreatedAt.Should().Be(created);
    }

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
    public void ContactRequest_StoresAllFields()
    {
        var req = new ContactRequest
        {
            Name = "Alice Smith",
            Email = "alice@example.com",
            Company = "Acme Corp",
            Subject = "Demo request",
            Message = "I'd like to schedule a demo."
        };

        req.Name.Should().Be("Alice Smith");
        req.Email.Should().Be("alice@example.com");
        req.Company.Should().Be("Acme Corp");
        req.Subject.Should().Be("Demo request");
        req.Message.Should().Be("I'd like to schedule a demo.");
    }

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
    public void SearchResult_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var updated = DateTime.UtcNow;

        var result = new SearchResult
        {
            Type = "request",
            Id = id,
            Title = "Work Order #42",
            Subtitle = "Production Hall A",
            SiteId = siteId,
            Score = 0.95,
            UpdatedAt = updated,
            Open = new SearchResultOpen
            {
                Route = "/requests",
                Params = new Dictionary<string, string> { ["id"] = id.ToString() }
            },
            Permissions = new SearchResultPermissions
            {
                CanRead = true,
                CanEdit = true
            }
        };

        result.Type.Should().Be("request");
        result.Id.Should().Be(id);
        result.Title.Should().Be("Work Order #42");
        result.Subtitle.Should().Be("Production Hall A");
        result.SiteId.Should().Be(siteId);
        result.Score.Should().Be(0.95);
        result.UpdatedAt.Should().Be(updated);
        result.Open.Route.Should().Be("/requests");
        result.Open.Params.Should().ContainKey("id");
        result.Permissions.CanRead.Should().BeTrue();
        result.Permissions.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void SearchResultOpen_EmptyParams_ByDefault()
    {
        var open = new SearchResultOpen { Route = "/spaces" };

        open.Params.Should().BeEmpty();
    }

    [Fact]
    public void SearchResultPermissions_Defaults_CanReadTrueCanEditFalse()
    {
        var perms = new SearchResultPermissions();

        perms.CanRead.Should().BeTrue();
        perms.CanEdit.Should().BeFalse();
    }

    [Fact]
    public void SearchResponse_StoresQueryAndResults()
    {
        var response = new SearchResponse
        {
            Query = "production",
            Results = new List<SearchResult>
            {
                new()
                {
                    Type = "space",
                    Id = Guid.NewGuid(),
                    Title = "Hall A",
                    Score = 0.8,
                    Open = new SearchResultOpen { Route = "/spaces" },
                    Permissions = new SearchResultPermissions()
                }
            }
        };

        response.Query.Should().Be("production");
        response.Results.Should().HaveCount(1);
    }

    // ── TenantSuspendedResponse ────────────────────────────────────────────

    [Fact]
    public void TenantSuspendedResponse_StoresAllFields()
    {
        var resp = new TenantSuspendedResponse
        {
            Code = "tenant_suspended",
            Message = "Your account has been suspended.",
            TenantStatus = "Suspended",
            Reason = "payment_overdue",
            SelfServiceAllowed = true
        };

        resp.Code.Should().Be("tenant_suspended");
        resp.Message.Should().NotBeNullOrEmpty();
        resp.TenantStatus.Should().Be("Suspended");
        resp.Reason.Should().Be("payment_overdue");
        resp.SelfServiceAllowed.Should().BeTrue();
    }
}
