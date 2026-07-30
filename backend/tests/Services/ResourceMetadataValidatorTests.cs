using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class ResourceMetadataValidatorTests
{
    private static readonly Guid TypeId = Guid.NewGuid();

    private readonly Mock<IResourceTypeFieldRepository> _fieldRepoMock = new();
    private readonly ResourceMetadataValidator _validator;

    public ResourceMetadataValidatorTests()
    {
        _validator = new ResourceMetadataValidator(_fieldRepoMock.Object);
    }

    private void WithFields(params ResourceTypeFieldInfo[] fields) =>
        _fieldRepoMock
            .Setup(r => r.GetByTypeAsync(TypeId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields.ToList());

    private static ResourceTypeFieldInfo Field(
        string key,
        string dataType,
        bool isRequired = false,
        bool isActive = true,
        string? options = null,
        string? validation = null) => new()
        {
            Id = Guid.NewGuid(),
            ResourceTypeId = TypeId,
            Key = key,
            Label = key,
            DataType = dataType,
            Options = options is null ? null : Json(options),
            Validation = validation is null ? null : Json(validation),
            IsRequired = isRequired,
            SortOrder = 0,
            IsActive = isActive,
        };

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static Dictionary<string, JsonElement> Metadata(string rawObject) =>
        JsonDocument.Parse(rawObject).RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

    [Fact]
    public async Task ValidValues_ProduceNoBlockers()
    {
        WithFields(
            Field("mileage", ResourceFieldDataTypes.Number),
            Field("name", ResourceFieldDataTypes.Text),
            Field("electric", ResourceFieldDataTypes.Boolean),
            Field("bought", ResourceFieldDataTypes.Date),
            Field("fuel", ResourceFieldDataTypes.Select, options: """{"values":["petrol","diesel"]}"""));

        var result = await _validator.ValidateAsync(TypeId, Metadata(
            """{"mileage":12000,"name":"Van","electric":false,"bought":"2024-03-01","fuel":"diesel"}"""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task UnknownKey_IsBlocked()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"colour":"red"}"""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Blockers, b => b.FieldKey == "colour");
    }

    [Fact]
    public async Task MissingRequiredField_IsBlockedWhenComplete()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number, isRequired: true));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{}"""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Blockers, b => b.FieldKey == "mileage");
    }

    [Fact]
    public async Task MissingRequiredField_IsAllowedWhenNotComplete()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number, isRequired: true));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{}"""), requireComplete: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NullValueForRequiredField_IsBlocked()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number, isRequired: true));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"mileage":null}"""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task NullValueForOptionalField_IsAccepted()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"mileage":null}"""));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(ResourceFieldDataTypes.Number, """{"f":"text"}""")]
    [InlineData(ResourceFieldDataTypes.Text, """{"f":42}""")]
    [InlineData(ResourceFieldDataTypes.Boolean, """{"f":"yes"}""")]
    [InlineData(ResourceFieldDataTypes.Date, """{"f":"01/03/2024"}""")]
    public async Task WrongValueKind_IsBlocked(string dataType, string metadata)
    {
        WithFields(Field("f", dataType));

        var result = await _validator.ValidateAsync(TypeId, Metadata(metadata));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task NumberOutsideRange_IsBlocked()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number, validation: """{"min":0,"max":100}"""));

        var tooLow = await _validator.ValidateAsync(TypeId, Metadata("""{"mileage":-1}"""));
        var tooHigh = await _validator.ValidateAsync(TypeId, Metadata("""{"mileage":101}"""));
        var justRight = await _validator.ValidateAsync(TypeId, Metadata("""{"mileage":100}"""));

        Assert.False(tooLow.IsValid);
        Assert.False(tooHigh.IsValid);
        Assert.True(justRight.IsValid);
    }

    [Fact]
    public async Task TextExceedingMaxLength_IsBlocked()
    {
        WithFields(Field("plate", ResourceFieldDataTypes.Text, validation: """{"maxLength":5}"""));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"plate":"ABC1234"}"""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TextFailingRegex_IsBlocked()
    {
        WithFields(Field("plate", ResourceFieldDataTypes.Text, validation: """{"regex":"^[A-Z]{2}-[0-9]{3}$"}"""));

        var bad = await _validator.ValidateAsync(TypeId, Metadata("""{"plate":"nope"}"""));
        var good = await _validator.ValidateAsync(TypeId, Metadata("""{"plate":"AB-123"}"""));

        Assert.False(bad.IsValid);
        Assert.True(good.IsValid);
    }

    [Fact]
    public async Task InvalidRegexPattern_IsReportedRatherThanThrown()
    {
        WithFields(Field("plate", ResourceFieldDataTypes.Text, validation: """{"regex":"["}"""));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"plate":"anything"}"""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task SelectValueOutsideOptions_IsBlocked()
    {
        WithFields(Field("fuel", ResourceFieldDataTypes.Select, options: """{"values":["petrol","diesel"]}"""));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"fuel":"steam"}"""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValueForDeactivatedField_IsWarningNotBlocker()
    {
        WithFields(Field("legacy", ResourceFieldDataTypes.Text, isActive: false));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"legacy":"kept"}"""));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.FieldKey == "legacy");
    }

    [Fact]
    public async Task DeactivatedRequiredField_IsNotDemandedOnCreate()
    {
        WithFields(Field("legacy", ResourceFieldDataTypes.Text, isRequired: true, isActive: false));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{}"""));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NullMetadata_IsValidForTypeWithoutRequiredFields()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number));

        var result = await _validator.ValidateAsync(TypeId, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NullMetadata_IsBlockedWhenTypeHasRequiredFields()
    {
        WithFields(Field("mileage", ResourceFieldDataTypes.Number, isRequired: true));

        var result = await _validator.ValidateAsync(TypeId, null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RequiredTextField_RejectsBlankValue()
    {
        WithFields(Field("plate", ResourceFieldDataTypes.Text, isRequired: true));

        var result = await _validator.ValidateAsync(TypeId, Metadata("""{"plate":"   "}"""));

        Assert.False(result.IsValid);
    }
}
