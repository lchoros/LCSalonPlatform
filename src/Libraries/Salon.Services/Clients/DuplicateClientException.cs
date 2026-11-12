namespace Salon.Services.Clients;

/// <summary>Another client of the same salon already has this phone or email. <see cref="Field"/> names the property so a form can mark it.</summary>
public class DuplicateClientException : InvalidOperationException
{
    public DuplicateClientException(string field, string value)
        : base($"A client with this {field.ToLowerInvariant()} already exists in this salon.")
    {
        Field = field;
        Value = value;
    }

    public DuplicateClientException()
    {
    }

    public DuplicateClientException(string message)
        : base(message)
    {
    }

    public DuplicateClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary><c>Phone</c> or <c>Email</c>: the <see cref="Salon.Core.Clients.Client"/> property that clashed.</summary>
    public string? Field { get; }

    /// <summary>The normalised value that clashed; not part of the message so it stays out of logs.</summary>
    public string? Value { get; }
}