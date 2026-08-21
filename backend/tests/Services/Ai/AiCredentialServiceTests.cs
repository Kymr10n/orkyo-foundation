using Api.Models;
using Api.Repositories;
using Api.Security.Encryption;
using Api.Services;
using Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The credential service is the only place a plaintext API key exists in this codebase.
/// These tests pin the two properties that matter: the key is encrypted before it reaches
/// the repository, and it never leaves through the status DTO.
/// </summary>
public class AiCredentialServiceTests
{
    private const string ValidKey = "sk-ant-api03-abcdefghijklmnop-A4Qz";
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly Mock<IAiCredentialRepository> _repository = new();
    private readonly Mock<ITenantUserService> _tenantUsers = new();
    private readonly IEncryptionService _encryption = new AesGcmEncryptionService(new byte[32]);

    private AiCredentialService CreateSut() => new(
        _repository.Object, _encryption,
        new OrgContext { OrgId = OrgId, OrgSlug = "acme", DbConnectionString = "Host=localhost" },
        _tenantUsers.Object, NullLogger<AiCredentialService>.Instance);

    [Fact]
    public async Task Save_StoresCiphertext_NeverThePlaintextKey()
    {
        string? stored = null;
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid?, CancellationToken>((ciphertext, _, _, _) => stored = ciphertext)
            .Returns(Task.CompletedTask);

        await CreateSut().SaveAsync(ValidKey, actorUserId: null);

        stored.Should().NotBeNull();
        stored.Should().NotContain(ValidKey);
        _encryption.IsProtected(stored).Should().BeTrue();
    }

    [Fact]
    public async Task Save_ThenGetApiKey_RoundTripsThroughEncryption()
    {
        SetupRepositoryRoundTrip();

        var sut = CreateSut();
        await sut.SaveAsync(ValidKey, actorUserId: null);

        (await sut.GetApiKeyAsync()).Should().Be(ValidKey);
    }

    [Fact]
    public async Task GetApiKey_FromAnotherWorkspace_ReturnsNullRatherThanLeaking()
    {
        // Ciphertext is bound to its workspace as GCM associated data, so a row copied
        // into another workspace's database fails authentication instead of decrypting.
        var foreignCiphertext = _encryption.ProtectString(ValidKey, Guid.NewGuid())!;
        _repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCredentialRow { ApiKeyCiphertext = foreignCiphertext, KeyHint = "…A4Qz" });

        (await CreateSut().GetApiKeyAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_ExposesOnlyAHint_NotTheKey()
    {
        SetupRepositoryRoundTrip();
        var sut = CreateSut();
        await sut.SaveAsync(ValidKey, actorUserId: null);

        var status = await sut.GetStatusAsync();

        status.Configured.Should().BeTrue();
        status.KeyHint.Should().Be("…A4Qz");
        // The DTO has no property that could carry the key — assert on the serialized shape
        // so a future field addition has to face this test.
        System.Text.Json.JsonSerializer.Serialize(status).Should().NotContain(ValidKey);
    }

    [Fact]
    public async Task GetStatus_WithNoStoredKey_ReportsUnconfigured()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((AiCredentialRow?)null);

        var status = await CreateSut().GetStatusAsync();

        status.Configured.Should().BeFalse();
        status.KeyHint.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("sk-openai-not-an-anthropic-key")]
    public async Task Save_RejectsAKeyThatIsNotPlausiblyAnthropic(string candidate)
    {
        var act = () => CreateSut().SaveAsync(candidate, actorUserId: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetModel_FallsBackToTheApplicationDefault()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCredentialRow { ApiKeyCiphertext = "x", KeyHint = "…A4Qz", Model = null });

        (await CreateSut().GetModelAsync()).Should().Be(AiDefaults.Model);
    }

    /// <summary>Makes the repository behave like a real one: what was written is what is read back.</summary>
    private void SetupRepositoryRoundTrip()
    {
        AiCredentialRow? row = null;
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid?, CancellationToken>((ciphertext, hint, _, _) =>
                row = new AiCredentialRow { ApiKeyCiphertext = ciphertext, KeyHint = hint })
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
    }
}
