using Api.Models;
using Api.Validators;
using FluentValidation.TestHelper;

namespace Orkyo.Foundation.Tests.Validators;

/// <summary>
/// The validators are the only place the announcement shape is checked; the service does not
/// restate the rules.
/// </summary>
public class AnnouncementRequestValidatorsTests
{
    private readonly CreateAnnouncementRequestValidator _create = new();
    private readonly UpdateAnnouncementRequestValidator _update = new();

    [Fact]
    public void Create_BodyAtTheCap_Passes() =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = "T", Body = new string('x', CreateAnnouncementRequestValidator.BodyMaxLength) })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_BodyOverTheCap_Fails() =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = "T", Body = new string('x', CreateAnnouncementRequestValidator.BodyMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Body);

    [Fact]
    public void Update_BodyOverTheCap_Fails() =>
        _update.TestValidate(new UpdateAnnouncementRequest { Title = "T", Body = new string('x', CreateAnnouncementRequestValidator.BodyMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Body);

    [Theory]
    [InlineData("", "Body")]
    [InlineData("   ", "Body")]
    [InlineData("Title", "")]
    [InlineData("Title", "   ")]
    public void Create_BlankTitleOrBody_Fails(string title, string body) =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = title, Body = body })
            .IsValid.Should().BeFalse();

    [Fact]
    public void Create_UnknownChannel_Fails() =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = "T", Body = "B", Channels = ["sms"] })
            .IsValid.Should().BeFalse();

    [Fact]
    public void Create_TitleOverTheCap_Fails() =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = new string('x', CreateAnnouncementRequestValidator.TitleMaxLength + 1), Body = "B" })
            .ShouldHaveValidationErrorFor(x => x.Title);

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Create_RetentionOutOfRange_Fails(int days) =>
        _create.TestValidate(new CreateAnnouncementRequest { Title = "T", Body = "B", RetentionDays = days })
            .ShouldHaveValidationErrorFor(x => x.RetentionDays!.Value);
}
