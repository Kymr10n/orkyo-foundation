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

    [Fact]
    public void PageRequest_Sanitize_ClampsNegativePage()
    {
        var request = new PageRequest { Page = -5, PageSize = 20 };
        var sanitized = request.Sanitize();

        sanitized.Page.Should().Be(1);
        sanitized.PageSize.Should().Be(20);
    }

    [Fact]
    public void PageRequest_Sanitize_ClampsZeroPage()
    {
        var request = new PageRequest { Page = 0 };
        var sanitized = request.Sanitize();

        sanitized.Page.Should().Be(1);
    }

    [Fact]
    public void PageRequest_Sanitize_ClampsPageSizeAboveMax()
    {
        var request = new PageRequest { PageSize = 500 };
        var sanitized = request.Sanitize();

        sanitized.PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public void PageRequest_Sanitize_ClampsPageSizeBelowMin()
    {
        var request = new PageRequest { PageSize = 0 };
        var sanitized = request.Sanitize();

        sanitized.PageSize.Should().Be(1);
    }

    [Fact]
    public void PageRequest_Sanitize_DoesNotModifyValidValues()
    {
        var request = new PageRequest { Page = 3, PageSize = 25 };
        var sanitized = request.Sanitize();

        sanitized.Page.Should().Be(3);
        sanitized.PageSize.Should().Be(25);
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

    [Fact]
    public void PagedResult_TotalPages_CalculatesCorrectly()
    {
        var result = PagedResult<int>.Create([], 101, new PageRequest { PageSize = 50 });

        result.TotalPages.Should().Be(3); // ceil(101/50) = 3
    }

    [Fact]
    public void PagedResult_TotalPages_ExactDivision()
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { PageSize = 50 });

        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void PagedResult_TotalPages_SingleItem()
    {
        var result = PagedResult<int>.Create([], 1, new PageRequest { PageSize = 50 });

        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void PagedResult_TotalPages_ZeroItems()
    {
        var result = PagedResult<int>.Create([], 0, new PageRequest { PageSize = 50 });

        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public void PagedResult_HasNextPage_TrueWhenMorePages()
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { Page = 1, PageSize = 50 });

        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_HasNextPage_FalseOnLastPage()
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { Page = 2, PageSize = 50 });

        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_HasPreviousPage_FalseOnFirstPage()
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { Page = 1, PageSize = 50 });

        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_HasPreviousPage_TrueOnSecondPage()
    {
        var result = PagedResult<int>.Create([], 100, new PageRequest { Page = 2, PageSize = 50 });

        result.HasPreviousPage.Should().BeTrue();
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
}
