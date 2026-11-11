namespace Salon.Services.Seeding;

/// <summary>
/// Section "Demo": inputs for <see cref="DemoSeeder"/>. <see cref="OwnerPassword"/> is a local development value set in
/// <c>appsettings.Development.json</c>; when it is absent the demo salon is created without users.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Password of the two demo logins (owner and front desk).</summary>
    public string? OwnerPassword { get; set; }
}