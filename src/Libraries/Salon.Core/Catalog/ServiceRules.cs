using NodaTime;

namespace Salon.Core.Catalog;

/// <summary>
/// The pure rules a service must satisfy (ADR 0013), shared by the Create and Edit slices (rule 3 forbids one slice
/// referencing the other, so anything both need sits here). The validators turn each failure into a localized message;
/// these methods only decide. Minutes sit on a 5-minute grid; the processing window lies inside the duration.
/// </summary>
public static class ServiceRules
{
    public const int NameMaxLength = 200;
    public const int CategoryNameMaxLength = ServiceCategory.NameMaxLength;
    public const int DescriptionMaxLength = 2000;
    public const int StepMinutes = 5;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 600;
    public const int MaxBufferAfterMinutes = 120;
    public const int MaxProcessingMinutes = 240;
    public const decimal MaxPrice = 100_000m;

    /// <summary>The stored form of a name: trimmed, never null.</summary>
    public static string NormalizeName(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>The stored form of a description: trimmed, null when blank.</summary>
    public static string? NormalizeDescription(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    public static bool HasName(string? name) => !string.IsNullOrWhiteSpace(name);

    /// <summary>Length after trimming; a missing name passes here and fails <see cref="HasName"/>, so it is reported once.</summary>
    public static bool NameFits(string? name) => name is null || name.Trim().Length <= NameMaxLength;

    public static bool CategoryNameFits(string? name) => name is null || name.Trim().Length <= CategoryNameMaxLength;

    public static bool DescriptionFits(string? description) => description is null || description.Trim().Length <= DescriptionMaxLength;

    public static bool IsOnStep(int minutes) => minutes % StepMinutes == 0;

    public static bool IsDurationInRange(int minutes) => minutes is >= MinDurationMinutes and <= MaxDurationMinutes;

    public static bool IsBufferInRange(int minutes) => minutes is >= 0 and <= MaxBufferAfterMinutes;

    public static bool IsProcessingInRange(int minutes) => minutes is >= 0 and <= MaxProcessingMinutes;

    /// <summary>True when the processing window <c>[startsAfter, startsAfter + processing)</c> lies inside <c>[0, duration]</c>.</summary>
    public static bool ProcessingFitsInside(int durationMinutes, int processingMinutes, int processingStartsAfterMinutes)
        => processingStartsAfterMinutes >= 0
           && processingMinutes >= 0
           && processingStartsAfterMinutes + processingMinutes <= durationMinutes;

    /// <summary>Without processing time the offset means nothing and is stored as 0, so two services with no processing compare equal.</summary>
    public static int NormalizeProcessingStart(int processingMinutes, int processingStartsAfterMinutes)
        => processingMinutes == 0 ? 0 : processingStartsAfterMinutes;

    public static bool IsPriceInRange(decimal price) => price >= 0m && price <= MaxPrice;

    public static bool HasAtMostTwoDecimals(decimal price) => decimal.Round(price, 2) == price;

    /// <summary>The duration this person takes (ADR 0018): the override when it has one, else the service's.</summary>
    public static Duration EffectiveDuration(Service service, StaffServiceOverride? staffOverride)
    {
        ArgumentNullException.ThrowIfNull(service);
        return staffOverride?.Duration ?? service.Duration;
    }

    /// <summary>The price this person charges (ADR 0018): the override when it has one, else the service's.</summary>
    public static decimal EffectivePrice(Service service, StaffServiceOverride? staffOverride)
    {
        ArgumentNullException.ThrowIfNull(service);
        return staffOverride?.Price ?? service.Price;
    }

    /// <summary>Everyone offers a service unless a row says otherwise.</summary>
    public static bool IsOffered(StaffServiceOverride? staffOverride) => staffOverride?.Offers ?? true;

    /// <summary>A staff duration is blank (service default) or obeys the same grid and bounds as a service duration.</summary>
    public static bool IsOverrideDurationValid(int? minutes)
        => minutes is null || (IsDurationInRange(minutes.Value) && IsOnStep(minutes.Value));

    /// <summary>A staff price is blank (service default) or obeys the same range and precision as a service price.</summary>
    public static bool IsOverridePriceValid(decimal? price)
        => price is null || (IsPriceInRange(price.Value) && HasAtMostTwoDecimals(price.Value));

    /// <summary>"Cut" and "cut" are the same service to a receptionist: names compare trimmed and case-insensitively.</summary>
    public static bool NamesMatch(string? a, string? b)
        => string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Escapes the LIKE wildcards so a name is matched literally (Postgres' default escape character is the backslash).</summary>
    public static string LikeLiteral(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("%", "\\%", StringComparison.Ordinal)
                   .Replace("_", "\\_", StringComparison.Ordinal);
    }
}