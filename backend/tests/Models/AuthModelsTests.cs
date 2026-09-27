using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

public class AuthModelsTests
{
    [Fact]
    public void User_KeycloakPropertiesAreNullable()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Status = UserStatus.Active,
            IsTenantAdmin = false
        };

        Assert.Null(user.KeycloakId);
        Assert.Null(user.KeycloakMetadata);
    }
}
