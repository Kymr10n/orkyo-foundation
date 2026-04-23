using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSettingsValidatorTests
{
    private static TenantSettingDescriptor IntDesc(string min = "1", string max = "100")
        => new("category.some_int", "category", "Label", "Desc", "int", "10", MinValue: min, MaxValue: max);

    private static TenantSettingDescriptor DoubleDesc(string min = "0.0", string max = "1.0")
        => new("category.some_double", "category", "Label", "Desc", "double", "0.5", MinValue: min, MaxValue: max);

    private static TenantSettingDescriptor BoolDesc()
        => new("category.some_bool", "category", "Label", "Desc", "bool", "true");

    private static TenantSettingDescriptor StringDesc(string key = "category.some_string")
        => new(key, "category", "Label", "Desc", "string", "default");

    [Fact]
    public void Validate_ValueExceedingMaxLength_Throws()
    {
        var descriptor = StringDesc();
        var oversize = new string('a', TenantSettingsValidator.MaxStringLength + 1);

        var act = () => TenantSettingsValidator.Validate(descriptor, oversize);

        act.Should().Throw<ArgumentException>().WithMessage("*exceeds maximum length*");
    }

    [Theory]
    [InlineData("not-an-int")]
    [InlineData("3.14")]
    public void Validate_IntDescriptor_RejectsNonInteger(string raw)
    {
        var act = () => TenantSettingsValidator.Validate(IntDesc(), raw);
        act.Should().Throw<ArgumentException>().WithMessage("*must be an integer*");
    }

    [Fact]
    public void Validate_IntBelowMin_Throws()
    {
        var act = () => TenantSettingsValidator.Validate(IntDesc(min: "5"), "1");
        act.Should().Throw<ArgumentException>().WithMessage("*minimum is 5*");
    }

    [Fact]
    public void Validate_IntAboveMax_Throws()
    {
        var act = () => TenantSettingsValidator.Validate(IntDesc(max: "10"), "999");
        act.Should().Throw<ArgumentException>().WithMessage("*maximum is 10*");
    }

    [Fact]
    public void Validate_IntInRange_Passes()
    {
        var act = () => TenantSettingsValidator.Validate(IntDesc(min: "1", max: "100"), "42");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void Validate_DoubleDescriptor_RejectsNonNumber(string raw)
    {
        var act = () => TenantSettingsValidator.Validate(DoubleDesc(), raw);
        act.Should().Throw<ArgumentException>().WithMessage("*must be a number*");
    }

    [Fact]
    public void Validate_DoubleOutOfRange_Throws()
    {
        var act = () => TenantSettingsValidator.Validate(DoubleDesc(min: "0.1", max: "0.9"), "0.05");
        act.Should().Throw<ArgumentException>().WithMessage("*minimum is 0.1*");
    }

    [Fact]
    public void Validate_DoubleParsesInvariantCulture()
    {
        var act = () => TenantSettingsValidator.Validate(DoubleDesc(), "0.5");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void Validate_BoolDescriptor_RejectsNonBool(string raw)
    {
        var act = () => TenantSettingsValidator.Validate(BoolDesc(), raw);
        act.Should().Throw<ArgumentException>().WithMessage("*must be 'true' or 'false'*");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("False")]
    public void Validate_BoolValid_Passes(string raw)
    {
        var act = () => TenantSettingsValidator.Validate(BoolDesc(), raw);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_StringDescriptor_RejectsBlank(string raw)
    {
        var act = () => TenantSettingsValidator.Validate(StringDesc(), raw);
        act.Should().Throw<ArgumentException>().WithMessage("*cannot be empty*");
    }

    [Theory]
    [InlineData("ff0000")]      // missing #
    [InlineData("#abc")]        // 3 chars
    [InlineData("#zzzzzz")]     // not hex
    public void Validate_HexColorKey_RejectsBadHex(string raw)
    {
        var descriptor = StringDesc("branding.branding_primary_color");
        var act = () => TenantSettingsValidator.Validate(descriptor, raw);
        act.Should().Throw<ArgumentException>().WithMessage("*valid hex color*");
    }

    [Theory]
    [InlineData("#ff0000")]
    [InlineData("#abcdef")]
    [InlineData("#012345")]
    public void Validate_HexColorKey_AcceptsValidHex(string raw)
    {
        var descriptor = StringDesc("branding.branding_primary_color");
        var act = () => TenantSettingsValidator.Validate(descriptor, raw);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MimeTypesKey_RejectsInvalidMime()
    {
        var descriptor = StringDesc("uploads.upload_allowed_mime_types");
        var act = () => TenantSettingsValidator.Validate(descriptor, "image/png,not_a_mime");
        act.Should().Throw<ArgumentException>().WithMessage("*invalid MIME type*");
    }

    [Fact]
    public void Validate_MimeTypesKey_AcceptsCommaSeparatedList()
    {
        var descriptor = StringDesc("uploads.upload_allowed_mime_types");
        var act = () => TenantSettingsValidator.Validate(descriptor, "image/png, image/jpeg, application/pdf");
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ProductNameKey_RejectsHtml()
    {
        var descriptor = StringDesc("branding.branding_product_name");
        var act = () => TenantSettingsValidator.Validate(descriptor, "Hello <script>");
        act.Should().Throw<ArgumentException>().WithMessage("*HTML tags*");
    }

    [Fact]
    public void Validate_ProductNameKey_RejectsOver100Chars()
    {
        var descriptor = StringDesc("branding.branding_product_name");
        var act = () => TenantSettingsValidator.Validate(descriptor, new string('a', 101));
        act.Should().Throw<ArgumentException>().WithMessage("*not exceed 100 characters*");
    }

    [Fact]
    public void Validate_ProductNameKey_AcceptsCleanValue()
    {
        var descriptor = StringDesc("branding.branding_product_name");
        var act = () => TenantSettingsValidator.Validate(descriptor, "Acme Corp");
        act.Should().NotThrow();
    }
}
