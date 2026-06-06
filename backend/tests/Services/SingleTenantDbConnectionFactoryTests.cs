using Api.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class SingleTenantDbConnectionFactoryTests
{
    private const string ValidCs = "Host=localhost;Database=test_db;Username=app;Password=secret";

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenConnectionStringIsEmpty()
    {
        Action act = () => new SingleTenantDbConnectionFactory("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenConnectionStringIsWhitespace()
    {
        Action act = () => new SingleTenantDbConnectionFactory("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateControlPlaneConnection_ReturnsNpgsqlConnection()
    {
        var factory = new SingleTenantDbConnectionFactory(ValidCs);

        using var connection = factory.CreateControlPlaneConnection();

        connection.Should().BeOfType<NpgsqlConnection>();
    }

    [Fact]
    public void CreateConnectionForDatabase_ReturnsNpgsqlConnectionWithSameString()
    {
        var factory = new SingleTenantDbConnectionFactory(ValidCs);

        using var control = factory.CreateControlPlaneConnection();
        using var byId = factory.CreateConnectionForDatabase("other_db");

        // Single-tenant: both methods return connections to the same configured string.
        byId.ConnectionString.Should().Be(control.ConnectionString);
    }

    [Fact]
    public void FromConfiguration_ThrowsInvalidOperationException_WhenDefaultKeyMissing()
    {
        var config = new ConfigurationBuilder().Build();

        Action act = () => SingleTenantDbConnectionFactory.FromConfiguration(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultConnection*");
    }

    [Fact]
    public void FromConfiguration_ThrowsInvalidOperationException_WhenCustomKeyMissing()
    {
        var config = new ConfigurationBuilder().Build();

        Action act = () => SingleTenantDbConnectionFactory.FromConfiguration(config, "CustomKey");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CustomKey*");
    }

    [Fact]
    public void FromConfiguration_ReturnsFactory_WhenDefaultKeyPresent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:DefaultConnection", ValidCs)])
            .Build();

        var factory = SingleTenantDbConnectionFactory.FromConfiguration(config);

        factory.Should().NotBeNull();
    }

    [Fact]
    public void FromConfiguration_ReadsCustomConnectionStringKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:Community", ValidCs)])
            .Build();

        var factory = SingleTenantDbConnectionFactory.FromConfiguration(config, "Community");

        factory.Should().NotBeNull();
    }

    [Fact]
    public void CreateTenantConnection_ReturnsNpgsqlConnection()
    {
        var factory = new SingleTenantDbConnectionFactory(ValidCs);
        var tenant = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "community",
            TenantDbConnectionString = ValidCs,
            Status = "active"
        };

        using var connection = factory.CreateTenantConnection(tenant);

        connection.Should().BeOfType<NpgsqlConnection>();
    }

    [Fact]
    public void CreateOrgConnection_ReturnsNpgsqlConnection()
    {
        var factory = new SingleTenantDbConnectionFactory(ValidCs);
        var org = new OrgContext
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "community",
            DbConnectionString = ValidCs
        };

        using var connection = factory.CreateOrgConnection(org);

        connection.Should().BeOfType<NpgsqlConnection>();
    }
}
