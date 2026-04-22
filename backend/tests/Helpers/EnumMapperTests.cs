using Api.Helpers;
using Api.Models;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Helpers;

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
}
