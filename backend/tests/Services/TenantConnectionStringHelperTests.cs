using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantConnectionStringHelperTests
{
    [Fact]
    public void BuildTenantDatabaseConnectionString_ShouldReplaceDatabaseName()
    {
        const string controlPlane = "Host=localhost;Database=control_plane;Username=postgres;Password=postgres";

        var tenantConnection = TenantConnectionStringHelper.BuildTenantDatabaseConnectionString(controlPlane, "tenant_acme");

        tenantConnection.Should().Contain("Database=tenant_acme");
        tenantConnection.Should().Contain("Host=localhost");
        tenantConnection.Should().Contain("Username=postgres");
        new NpgsqlConnectionStringBuilder(tenantConnection).MinPoolSize.Should().Be(0);
    }

    [Fact]
    public void BuildTenantDatabaseConnectionString_ShouldPreserveOtherSegments()
    {
        const string controlPlane = "Host=db.example.com;Port=5432;Database=control_plane;Username=app;Password=test-password;Ssl Mode=Require";

        var tenantConnection = TenantConnectionStringHelper.BuildTenantDatabaseConnectionString(controlPlane, "tenant_blue");

        tenantConnection.Should().Contain("Host=db.example.com");
        tenantConnection.Should().Contain("Port=5432");
        tenantConnection.Should().Contain("Database=tenant_blue");
        tenantConnection.Should().Contain("Username=app");
        tenantConnection.Should().Contain("SSL Mode=Require");
        new NpgsqlConnectionStringBuilder(tenantConnection).MinPoolSize.Should().Be(0);
    }

    [Fact]
    public void BuildTenantDatabaseConnectionString_ShouldResetNonzeroMinPoolSizeToZero()
    {
        // Tenant pools must be able to shrink to zero when idle, even if the control-plane
        // connection string keeps a warm floor for itself.
        const string controlPlane = "Host=localhost;Database=control_plane;Username=postgres;Password=postgres;Minimum Pool Size=5";

        var tenantConnection = TenantConnectionStringHelper.BuildTenantDatabaseConnectionString(controlPlane, "tenant_acme");

        new NpgsqlConnectionStringBuilder(tenantConnection).MinPoolSize.Should().Be(0);
    }
}
