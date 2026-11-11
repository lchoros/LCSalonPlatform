namespace Salon.Services.Events;

/// <summary>Host-level outbox dispatch settings (section "Outbox"). Platform configuration, not per-tenant settings.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Failures per message before it is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Messages loaded per dispatcher pass.</summary>
    public int BatchSize { get; set; } = 100;
}