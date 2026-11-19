namespace Salon.Plugin.Sms.Log;

/// <summary>
/// Where the fake-SMS email copy goes (ADR 0078): the local Mailpit mailbox by default; on a hosted pilot, the
/// salon owner's own inbox, so every text a client would have received can be read until a real SMS provider
/// arrives at M3.
/// </summary>
public sealed class SmsMirrorOptions
{
    public const string SectionName = "Sms";

    public string MirrorAddress { get; set; } = LogSmsProvider.MirrorMailbox;
}
