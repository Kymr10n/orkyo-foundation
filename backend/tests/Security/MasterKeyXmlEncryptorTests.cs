using System.Xml.Linq;
using Api.Security;
using Api.Security.Encryption;

namespace Orkyo.Foundation.Tests.Security;

public class MasterKeyXmlEncryptorTests
{
    private static readonly AesGcmEncryptionService Crypto = new(new byte[32]);

    [Fact]
    public void Encrypt_LeavesNoPlaintext_AndNamesTheDecryptor()
    {
        var key = new XElement("key", new XAttribute("id", "k1"), new XElement("masterKey", "c2VjcmV0"));

        var info = new MasterKeyXmlEncryptor(Crypto).Encrypt(key);

        info.DecryptorType.Should().Be(typeof(MasterKeyXmlDecryptor));
        info.EncryptedElement.Name.LocalName.Should().Be(MasterKeyXmlEncryptor.ElementName);
        info.EncryptedElement.ToString().Should().NotContain("c2VjcmV0", "the key material is sealed");
        info.EncryptedElement.Elements().Should().BeEmpty("the plaintext structure is sealed too");
    }

    [Fact]
    public void Decrypt_RoundTrips()
    {
        var key = new XElement("key", new XAttribute("id", "k1"), new XElement("masterKey", "c2VjcmV0"));

        var opened = new MasterKeyXmlDecryptor(Crypto).Decrypt(new MasterKeyXmlEncryptor(Crypto).Encrypt(key).EncryptedElement);

        XNode.DeepEquals(opened, key).Should().BeTrue();
    }

    [Fact]
    public void Decrypt_UnderAnotherMasterKey_Fails()
    {
        var sealed_ = new MasterKeyXmlEncryptor(Crypto).Encrypt(new XElement("key")).EncryptedElement;
        var other = new AesGcmEncryptionService(Enumerable.Repeat((byte)7, 32).ToArray());

        var act = () => new MasterKeyXmlDecryptor(other).Decrypt(sealed_);

        act.Should().Throw<EncryptionException>();
    }
}
