using System.Text;
using System.Xml.Linq;
using Api.Security.Encryption;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Api.Security;

/// <summary>
/// Seals the Data Protection key ring with <c>ORKYO_MASTER_ENCRYPTION_KEY</c>. The ring lives in
/// Valkey next to the sessions it protects; unencrypted, a copy of Valkey could mint a valid
/// session cookie for any session id it also contains. The master key is the one secret that is
/// not in Valkey, so it is the one that can seal the ring. Keys written before this ran stay
/// readable (Data Protection reads a plaintext key as is) until they rotate out.
/// </summary>
public sealed class MasterKeyXmlEncryptor : IXmlEncryptor
{
    internal const string Purpose = "DataProtection-Keys";
    internal const string ElementName = "masterKeyEncrypted";

    private readonly IEncryptionService _crypto;

    public MasterKeyXmlEncryptor(IEncryptionService crypto)
    {
        _crypto = crypto;
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);
        var bytes = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var sealed_ = Convert.ToBase64String(_crypto.ProtectBytes(bytes, Purpose));
        return new EncryptedXmlInfo(new XElement(ElementName, sealed_), typeof(MasterKeyXmlDecryptor));
    }
}

/// <summary>Opens what <see cref="MasterKeyXmlEncryptor"/> sealed. Data Protection activates it
/// through DI, so the same <see cref="IEncryptionService"/> instance serves both.</summary>
public sealed class MasterKeyXmlDecryptor : IXmlDecryptor
{
    private readonly IEncryptionService _crypto;

    public MasterKeyXmlDecryptor(IEncryptionService crypto)
    {
        _crypto = crypto;
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);
        var bytes = _crypto.UnprotectBytes(Convert.FromBase64String(encryptedElement.Value), MasterKeyXmlEncryptor.Purpose);
        return XElement.Parse(Encoding.UTF8.GetString(bytes));
    }
}
