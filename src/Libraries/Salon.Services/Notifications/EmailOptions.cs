namespace Salon.Services.Notifications;

/// <summary>Host-level SMTP transport settings. Phase 1 points at Mailpit (localhost:1025). Per-tenant sender details come later via ISettingService.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "no-reply@salon.local";

    public string FromName { get; set; } = "Salon Platform";
}