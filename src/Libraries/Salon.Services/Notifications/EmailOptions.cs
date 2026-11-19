namespace Salon.Services.Notifications;

/// <summary>Host-level SMTP transport settings. Local development points at Mailpit (localhost:1025, no credentials); a real relay sets a username and STARTTLS (ADR 0078). Per-tenant sender details come later via ISettingService.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "no-reply@salon.local";

    public string FromName { get; set; } = "Salon Platform";

    /// <summary>SMTP username; empty means an unauthenticated server such as the local Mailpit.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Never in the repo: host configuration or user-secrets only.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Upgrade the connection with STARTTLS before speaking (the submission-port convention, e.g. smtp.gmail.com:587).</summary>
    public bool UseStartTls { get; set; }
}
