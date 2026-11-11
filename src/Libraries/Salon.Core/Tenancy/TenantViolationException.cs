namespace Salon.Core.Tenancy;

/// <summary>Thrown when code tries to write a tenant-owned row outside its tenant, or with no tenant at all.</summary>
public class TenantViolationException : InvalidOperationException
{
    public TenantViolationException()
    {
    }

    public TenantViolationException(string message)
        : base(message)
    {
    }

    public TenantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}