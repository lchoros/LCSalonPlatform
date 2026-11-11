namespace Salon.Services.Security;

/// <summary>
/// Encrypts one field's text for storage (ADR 0011: client health notes). The implementation (ASP.NET Core Data Protection)
/// lives in Salon.Web.Framework because that stack ships with ASP.NET Core, which the service layer does not reference.
/// Cipher text is opaque, authenticated and different on every call for the same input.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>Cipher text for <paramref name="plain"/>; the input must not be empty.</summary>
    string Protect(string plain);

    /// <summary>The plain text, or a <see cref="System.Security.Cryptography.CryptographicException"/> when the cipher text is not ours or was tampered with.</summary>
    string Unprotect(string cipher);

    /// <summary>False, with a null <paramref name="plain"/>, when the cipher text cannot be read (other key ring, garbage, tampering).</summary>
    bool TryUnprotect(string cipher, out string? plain);
}