using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Salon.Services.Security;

namespace Salon.Web.Framework.Security;

/// <summary>
/// <see cref="IFieldEncryptor"/> over ASP.NET Core Data Protection (ADR 0011): one protector for the purpose
/// <see cref="Purpose"/>, so cipher text made for health notes cannot be replayed as a cookie or an anti-forgery token
/// and vice versa. Data Protection authenticates and uses a fresh IV per call; the keys live in the configured key ring
/// (the user profile in Development and in the test host; persistent storage is an M3 deployment requirement).
/// </summary>
internal sealed class DataProtectionFieldEncryptor(IDataProtectionProvider provider) : IFieldEncryptor
{
    public const string Purpose = "Salon.Clients.HealthNotes";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plain)
    {
        ArgumentException.ThrowIfNullOrEmpty(plain);
        return _protector.Protect(plain);
    }

    public string Unprotect(string cipher)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipher);
        return _protector.Unprotect(cipher);
    }

    public bool TryUnprotect(string cipher, out string? plain)
    {
        plain = null;
        if (string.IsNullOrEmpty(cipher))
        {
            return false;
        }

        try
        {
            plain = _protector.Unprotect(cipher);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            // Not even base64url: a manually edited row. Unreadable, not a server error.
            return false;
        }
    }
}