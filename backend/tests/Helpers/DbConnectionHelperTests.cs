using Api.Helpers;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Helpers;

public class DbConnectionHelperTests
{
    [Fact]
    public async Task OpenTenantConnectionAsync_Throws_WhenTenantContextMissing()
    {
        var context = new DefaultHttpContext();
        var act = async () => await context.OpenTenantConnectionAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tenant context*");
    }
}

[Collection("Database collection")]
public class DbConnectionHelperIntegrationTests
{
    private readonly string _tenantCs;

    public DbConnectionHelperIntegrationTests(DatabaseFixture fixture)
    {
        _tenantCs =
            $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    [Fact]
    public async Task OpenTenantConnectionAsync_ReturnsOpenConnection_WhenTenantContextPresent()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantContext"] = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = TestConstants.TenantSlug,
            TenantDbConnectionString = _tenantCs,
            Tier = ServiceTier.Enterprise,
            Status = "active"
        };

        await using var conn = await context.OpenTenantConnectionAsync();

        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
