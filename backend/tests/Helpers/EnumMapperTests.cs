using System.Text.Json.Serialization;
using Api.Helpers;
using Api.Models;

namespace Orkyo.Foundation.Tests.Helpers;

public class EnumMapperTests
{
    // --- ToDbValue: JsonStringEnumMemberName attribute wins ---

    [Theory]
    [InlineData(PlanningMode.Leaf, "leaf")]
    [InlineData(PlanningMode.Summary, "summary")]
    [InlineData(PlanningMode.Container, "container")]
    public void ToDbValue_ShouldUseJsonStringEnumMemberName(PlanningMode mode, string expected) =>
        EnumMapper.ToDbValue(mode).Should().Be(expected);

    // --- ToDbValue: fallback to lowercase name when no attribute ---

    public enum NoAttributeEnum { Alpha, BetaValue }

    [Theory]
    [InlineData(NoAttributeEnum.Alpha, "alpha")]
    [InlineData(NoAttributeEnum.BetaValue, "betavalue")]
    public void ToDbValue_ShouldFallbackToLowercaseName_WhenNoAttribute(NoAttributeEnum value, string expected) =>
        EnumMapper.ToDbValue(value).Should().Be(expected);

    // --- FromDbValue: round-trip via JsonStringEnumMemberName ---

    [Theory]
    [InlineData("leaf", PlanningMode.Leaf)]
    [InlineData("summary", PlanningMode.Summary)]
    [InlineData("container", PlanningMode.Container)]
    public void FromDbValue_ShouldParseViaJsonStringEnumMemberName(string dbValue, PlanningMode expected) =>
        EnumMapper.FromDbValue<PlanningMode>(dbValue).Should().Be(expected);

    [Theory]
    [InlineData("LEAF", PlanningMode.Leaf)]
    [InlineData("Leaf", PlanningMode.Leaf)]
    public void FromDbValue_ShouldBeCaseInsensitive(string dbValue, PlanningMode expected) =>
        EnumMapper.FromDbValue<PlanningMode>(dbValue).Should().Be(expected);

    [Fact]
    public void FromDbValue_ShouldThrow_ForUnknownValue()
    {
        var act = () => EnumMapper.FromDbValue<PlanningMode>("unknown_mode");
        act.Should().Throw<ArgumentException>().WithMessage("*unknown_mode*");
    }

    // --- ToPlanningMode convenience wrapper ---

    [Theory]
    [InlineData("leaf", PlanningMode.Leaf)]
    [InlineData("summary", PlanningMode.Summary)]
    [InlineData("container", PlanningMode.Container)]
    public void ToPlanningMode_ShouldDelegateToFromDbValue(string dbValue, PlanningMode expected) =>
        EnumMapper.ToPlanningMode(dbValue).Should().Be(expected);

    // --- ToDbValue: JsonPropertyName attribute (legacy fallback) ---

    public enum LegacyAttributeEnum
    {
        [JsonPropertyName("my_value")]
        MyValue,
        [JsonPropertyName("other_value")]
        OtherValue
    }

    [Theory]
    [InlineData(LegacyAttributeEnum.MyValue, "my_value")]
    [InlineData(LegacyAttributeEnum.OtherValue, "other_value")]
    public void ToDbValue_ShouldUseJsonPropertyName_WhenNoJsonStringEnumMemberName(LegacyAttributeEnum value, string expected) =>
        EnumMapper.ToDbValue(value).Should().Be(expected);

    // --- FromDbValue: JsonPropertyName attribute (legacy fallback) ---

    [Theory]
    [InlineData("my_value", LegacyAttributeEnum.MyValue)]
    [InlineData("other_value", LegacyAttributeEnum.OtherValue)]
    public void FromDbValue_ShouldParseViaJsonPropertyName(string dbValue, LegacyAttributeEnum expected) =>
        EnumMapper.FromDbValue<LegacyAttributeEnum>(dbValue).Should().Be(expected);

    // --- ParseEnum ---

    [Theory]
    [InlineData("Leaf", PlanningMode.Leaf)]
    [InlineData("leaf", PlanningMode.Leaf)]
    [InlineData("SUMMARY", PlanningMode.Summary)]
    public void ParseEnum_ShouldBeCaseInsensitive(string input, PlanningMode expected) =>
        EnumMapper.ParseEnum<PlanningMode>(input).Should().Be(expected);

    [Fact]
    public void ParseEnum_ShouldThrow_ForInvalidValue()
    {
        var act = () => EnumMapper.ParseEnum<PlanningMode>("bogus");
        act.Should().Throw<ArgumentException>();
    }

    // --- FromDbValue: field name match when no attributes ---

    [Theory]
    [InlineData("Alpha", NoAttributeEnum.Alpha)]
    [InlineData("alpha", NoAttributeEnum.Alpha)]
    [InlineData("BetaValue", NoAttributeEnum.BetaValue)]
    [InlineData("betavalue", NoAttributeEnum.BetaValue)]
    public void FromDbValue_ShouldParseByFieldName_WhenNoAttributeExists(string dbValue, NoAttributeEnum expected) =>
        EnumMapper.FromDbValue<NoAttributeEnum>(dbValue).Should().Be(expected);

    // --- ToDbValue: memberInfo null branch (undefined/cast enum value) ---

    [Fact]
    public void ToDbValue_ShouldFallbackToLowercaseString_WhenMemberInfoIsNull()
    {
        // Casting an integer that has no named member causes GetMember to return empty.
        var undefined = (NoAttributeEnum)999;
        EnumMapper.ToDbValue(undefined).Should().Be("999");
    }
}
