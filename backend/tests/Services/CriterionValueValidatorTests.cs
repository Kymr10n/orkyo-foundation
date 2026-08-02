using System.Text.Json;
using Api.Models;
using Api.Services;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Criterion values used to be written as raw JSONB with no type check at all — a Number
/// criterion accepted "banana" and the mismatch surfaced later as a silent non-match in the
/// solver. These pin the check that closed that hole, and with it the value-validation the
/// retired resource_type_fields system carried.
/// </summary>
public class CriterionValueValidatorTests
{
    private readonly CriterionValueValidator _validator = new();

    private static CriterionInfo Criterion(
        CriterionDataType dataType,
        string? validation = null,
        List<string>? enumValues = null,
        string name = "Capacity") => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            EnumValues = enumValues,
            Validation = validation is null
                ? null
                : JsonDocument.Parse(validation).RootElement.Clone(),
        };

    private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ── Type checking ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CriterionDataType.Boolean, "true")]
    [InlineData(CriterionDataType.Boolean, "false")]
    [InlineData(CriterionDataType.Number, "42")]
    [InlineData(CriterionDataType.Number, "4.5")]
    [InlineData(CriterionDataType.String, "\"hello\"")]
    [InlineData(CriterionDataType.Date, "\"2026-08-02\"")]
    public void Accepts_ValueMatchingItsType(CriterionDataType type, string json)
    {
        Assert.Null(_validator.Validate(Criterion(type), Value(json)));
    }

    [Theory]
    [InlineData(CriterionDataType.Boolean, "\"yes\"")]
    [InlineData(CriterionDataType.Number, "\"banana\"")]
    [InlineData(CriterionDataType.String, "42")]
    [InlineData(CriterionDataType.Date, "42")]
    public void Rejects_ValueOfTheWrongType(CriterionDataType type, string json)
    {
        var error = _validator.Validate(Criterion(type), Value(json));
        Assert.NotNull(error);
        Assert.Contains("Capacity", error);
    }

    [Fact]
    public void Accepts_ExplicitNull_BecauseItClearsTheValue()
    {
        // Required-ness is per resource type (criterion_resource_types.is_required) and is
        // enforced where the whole resource is saved, not on a single value.
        Assert.Null(_validator.Validate(Criterion(CriterionDataType.Number), Value("null")));
    }

    // ── Date ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"02-08-2026\"")]
    [InlineData("\"2026-8-2\"")]
    [InlineData("\"2026-13-01\"")]
    [InlineData("\"not a date\"")]
    public void Rejects_DateNotInIsoFormat(string json)
    {
        Assert.NotNull(_validator.Validate(Criterion(CriterionDataType.Date), Value(json)));
    }

    // ── Number constraints ────────────────────────────────────────────────────

    [Fact]
    public void Rejects_NumberBelowMin()
    {
        var error = _validator.Validate(Criterion(CriterionDataType.Number, """{"min":10}"""), Value("5"));
        Assert.Contains("at least 10", error);
    }

    [Fact]
    public void Rejects_NumberAboveMax()
    {
        var error = _validator.Validate(Criterion(CriterionDataType.Number, """{"max":10}"""), Value("11"));
        Assert.Contains("at most 10", error);
    }

    [Fact]
    public void Accepts_NumberInsideRange()
    {
        Assert.Null(_validator.Validate(
            Criterion(CriterionDataType.Number, """{"min":1,"max":10}"""), Value("5")));
    }

    // ── String constraints ────────────────────────────────────────────────────

    [Fact]
    public void Rejects_TextOverMaxLength()
    {
        var error = _validator.Validate(
            Criterion(CriterionDataType.String, """{"maxLength":3}"""), Value("\"abcd\""));
        Assert.Contains("at most 3 characters", error);
    }

    [Fact]
    public void Rejects_TextNotMatchingRegex()
    {
        var error = _validator.Validate(
            Criterion(CriterionDataType.String, """{"regex":"^[A-Z]{3}$"}"""), Value("\"abc\""));
        Assert.Contains("required format", error);
    }

    [Fact]
    public void Accepts_TextMatchingRegex()
    {
        Assert.Null(_validator.Validate(
            Criterion(CriterionDataType.String, """{"regex":"^[A-Z]{3}$"}"""), Value("\"ABC\"")));
    }

    [Fact]
    public void Reports_InvalidRegex_RatherThanThrowing()
    {
        // The pattern is tenant-authored: a bad one must not 500 the request.
        var error = _validator.Validate(
            Criterion(CriterionDataType.String, """{"regex":"["}"""), Value("\"abc\""));
        Assert.Contains("not a valid regular expression", error);
    }

    // ── Enum ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rejects_ValueOutsideEnumValues()
    {
        var error = _validator.Validate(
            Criterion(CriterionDataType.Enum, enumValues: ["S", "M", "L"]), Value("\"XL\""));
        Assert.Contains("S, M, L", error);
    }

    [Fact]
    public void Accepts_ValueInEnumValues()
    {
        Assert.Null(_validator.Validate(
            Criterion(CriterionDataType.Enum, enumValues: ["S", "M", "L"]), Value("\"M\"")));
    }

    [Fact]
    public void Accepts_AnyValue_WhenEnumDeclaresNoValues()
    {
        // Nothing to check against beats rejecting everything.
        Assert.Null(_validator.Validate(Criterion(CriterionDataType.Enum), Value("\"anything\"")));
    }

    // ── No constraints ────────────────────────────────────────────────────────

    [Fact]
    public void Accepts_AnyInTypeValue_WhenValidationIsAbsent()
    {
        // Every criterion that existed before this column was added is unconstrained.
        Assert.Null(_validator.Validate(Criterion(CriterionDataType.Number), Value("99999")));
        Assert.Null(_validator.Validate(Criterion(CriterionDataType.String), Value("\"anything at all\"")));
    }
}
