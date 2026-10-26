using Api.Models;

namespace Api.Tests.Models;

/// <summary>
/// Tests for PageRequest and PagedResult pagination models.
/// </summary>
public class PagedResultTests
{
    // ─── PageRequest ─────────────────────────────────────────────

    [Fact]
    public void PageRequest_Defaults_AreCorrect()
    {
        var request = new PageRequest();

        request.Page.Should().Be(1);
        request.PageSize.Should().Be(PageRequest.DefaultPageSize);
        request.Offset.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 10, 20)]
    [InlineData(5, 25, 100)]
    public void PageRequest_Offset_CalculatesCorrectly(int page, int pageSize, int expectedOffset)
    {
        var request = new PageRequest { Page = page, PageSize = pageSize };

        request.Offset.Should().Be(expectedOffset);
    }

    [Theory]
    [InlineData(-5, 20, 1, 20)]                        // negative page
    [InlineData(0, 20, 1, 20)]                         // zero page
    [InlineData(1, 500, 1, PageRequest.MaxPageSize)]   // page size above the max
    [InlineData(1, 0, 1, 1)]                           // page size below one
    [InlineData(3, 25, 3, 25)]                         // valid values are kept
    public void PageRequest_Sanitize_ClampsPageAndPageSize(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var sanitized = new PageRequest { Page = page, PageSize = pageSize }.Sanitize();

        sanitized.Page.Should().Be(expectedPage);
        sanitized.PageSize.Should().Be(expectedPageSize);
    }

    [Fact]
    public void PageRequest_Offset_HandlesNegativePageGracefully()
    {
        var request = new PageRequest { Page = -1, PageSize = 10 };

        // Offset uses Math.Max(1, Page) so negative page => offset 0
        request.Offset.Should().Be(0);
    }

    // ─── PagedResult ─────────────────────────────────────────────

    [Fact]
    public void PagedResult_Create_SetsAllProperties()
    {
        var items = new List<string> { "a", "b", "c" };
        var request = new PageRequest { Page = 2, PageSize = 10 };

        var result = PagedResult<string>.Create(items, 25, request);

        result.Items.Should().BeEquivalentTo(items);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalItems.Should().Be(25);
    }

    [Theory]
    [InlineData(101, 3)]   // ceil(101/50)
    [InlineData(100, 2)]   // exact division
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void PagedResult_TotalPages_IsTheCeilingOfItemsOverPageSize(int totalItems, int expectedPages)
    {
        var result = PagedResult<int>.Create([], totalItems, new PageRequest { PageSize = 50 });

        result.TotalPages.Should().Be(expectedPages);
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, false, true)]
    public void PagedResult_HasNextAndPreviousPage_FollowThePage(int page, bool hasNext, bool hasPrevious)
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { Page = page, PageSize = 50 });

        result.HasNextPage.Should().Be(hasNext);
        result.HasPreviousPage.Should().Be(hasPrevious);
    }

    [Fact]
    public void PagedResult_Create_SanitizesRequest()
    {
        var items = new List<string> { "x" };
        var badRequest = new PageRequest { Page = -1, PageSize = 999 };

        var result = PagedResult<string>.Create(items, 10, badRequest);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    // ─── PageRequest.From / ClampLimit ───────────────────────────

    [Theory]
    [InlineData(null, null, 1, PageRequest.DefaultPageSize)]   // absent → the first default page
    [InlineData(3, 25, 3, 25)]                                 // supplied values are kept
    [InlineData(-2, 999, 1, PageRequest.MaxPageSize)]          // out of range → sanitized
    public void From_BuildsASanitizedRequest(int? page, int? pageSize, int expectedPage, int expectedPageSize)
    {
        var request = PageRequest.From(page, pageSize);

        request.Page.Should().Be(expectedPage);
        request.PageSize.Should().Be(expectedPageSize);
    }

    [Theory]
    [InlineData(null, 20)]   // nothing requested → the fallback
    [InlineData(10, 10)]     // within range → as asked
    [InlineData(500, 50)]    // above the max → the max
    [InlineData(0, 1)]       // below one → one row
    public void ClampLimit_BoundsTheRequestedLimit(int? requested, int expected)
        => PageRequest.ClampLimit(requested, fallback: 20, max: 50).Should().Be(expected);

    // ─── PagedResult.Capped ──────────────────────────────────────

    [Fact]
    public void Capped_ReportsTheCapAsThePageSize_NotTheSanitizedMaximum()
    {
        var result = PagedResult<int>.Capped([1, 2], 2, PageRequest.MaxUnpagedItems);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(PageRequest.MaxUnpagedItems);
        result.TotalItems.Should().Be(2);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void Capped_SignalsTruncation_WhenTheTotalIsAboveTheCap()
    {
        var result = PagedResult<int>.Capped([1], PageRequest.MaxUnpagedItems + 1, PageRequest.MaxUnpagedItems);

        result.TotalItems.Should().Be(PageRequest.MaxUnpagedItems + 1);
        result.HasNextPage.Should().BeTrue();
    }
}
