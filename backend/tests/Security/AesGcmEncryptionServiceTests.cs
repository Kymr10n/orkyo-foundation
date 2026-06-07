using System.Security.Cryptography;
using System.Text;
using Api.Security.Encryption;

namespace Orkyo.Foundation.Tests.Security;

public class AesGcmEncryptionServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AesGcmEncryptionService NewService(byte[]? key = null) =>
        new(key ?? RandomNumberGenerator.GetBytes(32));

    // ── Construction ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_RejectsWrongKeyLength(int len)
    {
        var act = () => new AesGcmEncryptionService(RandomNumberGenerator.GetBytes(len));
        act.Should().Throw<ArgumentException>();
    }

    // ── String roundtrip ───────────────────────────────────────────────────────

    [Fact]
    public void ProtectString_Roundtrips()
    {
        var svc = NewService();
        var envelope = svc.ProtectString("hello world", Tenant);

        envelope.Should().StartWith("orkyoenc:v1:aesgcm256:");
        envelope.Should().NotContain("hello");
        svc.UnprotectString(envelope, Tenant).Should().Be("hello world");
    }

    [Fact]
    public void ProtectString_ProducesDifferentCiphertextEachCall_RandomNonce()
    {
        var svc = NewService();
        svc.ProtectString("same", Tenant).Should().NotBe(svc.ProtectString("same", Tenant));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ProtectString_PassesThroughNullAndEmpty(string? value)
    {
        NewService().ProtectString(value, Tenant).Should().Be(value);
    }

    [Fact]
    public void ProtectString_IsIdempotent()
    {
        var svc = NewService();
        var once = svc.ProtectString("secret", Tenant);
        svc.ProtectString(once, Tenant).Should().Be(once);
    }

    [Fact]
    public void UnprotectString_PassesThroughLegacyPlaintext()
    {
        // Migration safety: reading a not-yet-encrypted value returns it unchanged.
        NewService().UnprotectString("legacy plaintext", Tenant).Should().Be("legacy plaintext");
    }

    [Fact]
    public void UnprotectString_WrongTenant_Throws()
    {
        var svc = NewService();
        var envelope = svc.ProtectString("secret", Tenant);
        var act = () => svc.UnprotectString(envelope, OtherTenant);
        act.Should().Throw<EncryptionException>();
    }

    [Fact]
    public void UnprotectString_WrongKey_Throws()
    {
        var a = NewService();
        var envelope = a.ProtectString("secret", Tenant);
        var b = NewService(); // different random key
        var act = () => b.UnprotectString(envelope, Tenant);
        act.Should().Throw<EncryptionException>();
    }

    [Fact]
    public void UnprotectString_TamperedCiphertext_Throws()
    {
        var svc = NewService();
        var envelope = svc.ProtectString("secret", Tenant)!;
        // Flip the first char of the tag||ciphertext segment (a full byte of the auth tag).
        var parts = envelope.Split(':');
        parts[5] = (parts[5][0] == 'A' ? 'B' : 'A') + parts[5][1..];
        var act = () => svc.UnprotectString(string.Join(':', parts), Tenant);
        act.Should().Throw<EncryptionException>();
    }

    [Fact]
    public void UnprotectString_UnknownVersion_Throws()
    {
        var act = () => NewService().UnprotectString("orkyoenc:v9:aesgcm256:1:AAAA:BBBB", Tenant);
        act.Should().Throw<EncryptionException>();
    }

    [Fact]
    public void StringRoundtrip_Unicode()
    {
        var svc = NewService();
        const string text = "Grüße — 日本語 — 🔐";
        svc.UnprotectString(svc.ProtectString(text, Tenant), Tenant).Should().Be(text);
    }

    // ── Bytes roundtrip ────────────────────────────────────────────────────────

    [Fact]
    public void ProtectBytes_Roundtrips_AndHidesPlaintext()
    {
        var svc = NewService();
        var plaintext = Encoding.UTF8.GetBytes("PNG\x89 binary floorplan bytes");
        var sealed_ = svc.ProtectBytes(plaintext, Tenant);

        svc.IsProtected(sealed_).Should().BeTrue();
        Encoding.UTF8.GetString(sealed_).Should().NotContain("floorplan");
        svc.UnprotectBytes(sealed_, Tenant).Should().Equal(plaintext);
    }

    [Fact]
    public void UnprotectBytes_PassesThroughLegacyPlaintextBlob()
    {
        var svc = NewService();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        svc.UnprotectBytes(png, Tenant).Should().Equal(png);
    }

    [Fact]
    public void UnprotectBytes_WrongTenant_Throws()
    {
        var svc = NewService();
        var sealed_ = svc.ProtectBytes(new byte[] { 1, 2, 3, 4 }, Tenant);
        var act = () => svc.UnprotectBytes(sealed_, OtherTenant);
        act.Should().Throw<EncryptionException>();
    }

    [Fact]
    public void ProtectBytes_IsIdempotent()
    {
        var svc = NewService();
        var once = svc.ProtectBytes(new byte[] { 9, 8, 7 }, Tenant);
        svc.ProtectBytes(once, Tenant).Should().Equal(once);
    }

    [Fact]
    public void ProtectBytes_EmptyInput_Roundtrips()
    {
        var svc = NewService();
        var sealed_ = svc.ProtectBytes(ReadOnlySpan<byte>.Empty, Tenant);
        svc.UnprotectBytes(sealed_, Tenant).Should().BeEmpty();
    }
}
