using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Settings;

/// <summary>One stored setting value of one salon, e.g. "BookingSettings.MinNoticeMinutes" = "60".</summary>
public class Setting : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>"{SettingsClass}.{Property}"; unique per tenant.</summary>
    public required string Name { get; set; }

    /// <summary>Culture-invariant text form of the value; null means "explicitly cleared".</summary>
    public string? Value { get; set; }
}