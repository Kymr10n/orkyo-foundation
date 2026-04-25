using Api.Models.Preset;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class StarterTemplateServiceTests
{
    #region GetAvailableTemplates

    [Fact]
    public void GetAvailableTemplates_ShouldReturnFiveTemplates()
    {
        var sut = CreateService();

        var templates = sut.GetAvailableTemplates();

        templates.Should().HaveCount(5);
    }

    [Fact]
    public void GetAvailableTemplates_ShouldContainExpectedKeys()
    {
        var sut = CreateService();

        var keys = sut.GetAvailableTemplates().Select(t => t.Key).ToList();

        keys.Should().Contain("empty");
        keys.Should().Contain("demo");
        keys.Should().Contain("camping-site");
        keys.Should().Contain("construction-site");
        keys.Should().Contain("manufacturing");
    }

    [Fact]
    public void GetAvailableTemplates_EmptyTemplate_ShouldNotIncludeDemoData()
    {
        var sut = CreateService();

        var empty = sut.GetAvailableTemplates().First(t => t.Key == "empty");

        empty.IncludesDemoData.Should().BeFalse();
        empty.Name.Should().Be("Empty");
    }

    [Fact]
    public void GetAvailableTemplates_DemoTemplate_ShouldIncludeDemoData()
    {
        var sut = CreateService();

        var demo = sut.GetAvailableTemplates().First(t => t.Key == "demo");

        demo.IncludesDemoData.Should().BeTrue();
        demo.Name.Should().Be("Demo");
    }

    [Theory]
    [InlineData("camping-site", false)]
    [InlineData("construction-site", false)]
    [InlineData("manufacturing", false)]
    public void GetAvailableTemplates_PresetTemplates_ShouldNotIncludeDemoData(string key, bool expectedDemoData)
    {
        var sut = CreateService();

        var template = sut.GetAvailableTemplates().First(t => t.Key == key);

        template.IncludesDemoData.Should().Be(expectedDemoData);
    }

    [Fact]
    public void GetAvailableTemplates_AllTemplates_ShouldHaveRequiredFields()
    {
        var sut = CreateService();

        foreach (var template in sut.GetAvailableTemplates())
        {
            template.Key.Should().NotBeNullOrWhiteSpace($"Key should be set for {template.Name}");
            template.Name.Should().NotBeNullOrWhiteSpace($"Name should be set for {template.Key}");
            template.Description.Should().NotBeNullOrWhiteSpace($"Description should be set for {template.Key}");
            template.Icon.Should().NotBeNullOrWhiteSpace($"Icon should be set for {template.Key}");
        }
    }

    #endregion

    #region ApplyStarterTemplateAsync — routing / error handling

    [Fact]
    public async Task ApplyStarterTemplateAsync_UnknownTemplate_ShouldThrowArgumentException()
    {
        var sut = CreateService();

        var act = () => sut.ApplyStarterTemplateAsync(
            Guid.NewGuid(), "tenant_test", Guid.NewGuid(), "nonexistent");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown starter template*nonexistent*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("office-space")]
    [InlineData("DEMO")]
    [InlineData("Empty")]
    public async Task ApplyStarterTemplateAsync_InvalidKeys_ShouldThrowArgumentException(string badKey)
    {
        var sut = CreateService();

        var act = () => sut.ApplyStarterTemplateAsync(
            Guid.NewGuid(), "tenant_test", Guid.NewGuid(), badKey);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("camping-site")]
    [InlineData("construction-site")]
    [InlineData("manufacturing")]
    public async Task ApplyStarterTemplateAsync_PresetKeys_ShouldAttemptDbConnection(string key)
    {
        var mockConn = new Mock<IDbConnectionFactory>();
        mockConn.Setup(c => c.CreateConnectionForDatabase(It.IsAny<string>()))
            .Returns(() => new Npgsql.NpgsqlConnection("Host=localhost;Port=1;Database=fake"));

        var sut = new StarterTemplateService(
            mockConn.Object,
            new Mock<IFileStorageService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<StarterTemplateService>>().Object);

        var act = () => sut.ApplyStarterTemplateAsync(Guid.NewGuid(), "tenant_test", Guid.NewGuid(), key);

        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Should().NotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task ApplyStarterTemplateAsync_DemoKey_ShouldAttemptDbConnection()
    {
        var mockConn = new Mock<IDbConnectionFactory>();
        mockConn.Setup(c => c.CreateConnectionForDatabase(It.IsAny<string>()))
            .Returns(() => new Npgsql.NpgsqlConnection("Host=localhost;Port=1;Database=fake"));

        var sut = new StarterTemplateService(
            mockConn.Object,
            new Mock<IFileStorageService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<StarterTemplateService>>().Object);

        var act = () => sut.ApplyStarterTemplateAsync(Guid.NewGuid(), "tenant_test", Guid.NewGuid(), "demo");

        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Should().NotBeOfType<ArgumentException>();
    }

    #endregion

    #region StarterTemplateInfo record

    [Fact]
    public void StarterTemplateInfo_ShouldSupportRecordEquality()
    {
        var a = new StarterTemplateInfo
        {
            Key = "test",
            Name = "Test",
            Description = "Desc",
            Icon = "icon"
        };
        var b = new StarterTemplateInfo
        {
            Key = "test",
            Name = "Test",
            Description = "Desc",
            Icon = "icon"
        };

        a.Should().Be(b);
    }

    [Fact]
    public void StarterTemplateInfo_IncludesDemoData_DefaultsToFalse()
    {
        var info = new StarterTemplateInfo
        {
            Key = "x",
            Name = "X",
            Description = "X",
            Icon = "x"
        };

        info.IncludesDemoData.Should().BeFalse();
    }

    #endregion

    private static StarterTemplateService CreateService()
    {
        var connFactory = new Mock<IDbConnectionFactory>();
        var fileStorage = new Mock<IFileStorageService>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<StarterTemplateService>>();

        return new StarterTemplateService(
            connFactory.Object,
            fileStorage.Object,
            logger.Object);
    }
}
