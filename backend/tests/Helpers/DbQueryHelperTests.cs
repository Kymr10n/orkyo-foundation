using Api.Helpers;

namespace Orkyo.Foundation.Tests.Helpers;

public class DbQueryHelperTests
{
    [Theory]
    [InlineData("sites")]
    [InlineData("spaces")]
    [InlineData("space_groups")]
    [InlineData("criteria")]
    [InlineData("requests")]
    [InlineData("users")]
    [InlineData("tenants")]
    [InlineData("announcements")]
    [InlineData("presets")]
    [InlineData("templates")]
    public async Task ExistsAsync_AllowedTable_DoesNotThrow(string table)
    {
        // Verify the allowlist check passes (NpgsqlConnection will be null,
        // causing an InvalidOperationException after the check passes).
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => DbQueryHelper.ExistsAsync(null!, table, Guid.NewGuid()));

        // Error must be about the connection, NOT the allowlist
        Assert.Contains("Connection", ex.Message);
    }

    [Theory]
    [InlineData("drop_table")]
    [InlineData("admin_secrets")]
    [InlineData("users; DROP TABLE sites;--")]
    [InlineData("")]
    [InlineData("SITES")] // case-sensitive
    public async Task ExistsAsync_DisallowedTable_ThrowsArgumentException(string table)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => DbQueryHelper.ExistsAsync(null!, table, Guid.NewGuid()));

        Assert.Contains("not in the allowed table list", ex.Message);
    }
}
