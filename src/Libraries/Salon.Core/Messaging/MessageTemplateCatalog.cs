using System.Diagnostics.CodeAnalysis;

namespace Salon.Core.Messaging;

/// <summary>
/// Every message template key the platform knows, with its channels and allowed tokens (ADR 0008). Adding a key here
/// also needs both default texts under Salon.Services/MessageTemplates; an architecture test checks the parity.
/// An unknown key is a programming error, not data, so <see cref="Get"/> throws.
/// </summary>
public static class MessageTemplateCatalog
{
    public const string BookingConfirmation = "Booking.Confirmation";
    public const string BookingReminder = "Booking.Reminder";
    public const string BookingCancelled = "Booking.Cancelled";

    /// <summary>The SMS verification code sent before an online booking (ADR 0014). SMS only.</summary>
    public const string BookingCode = "Booking.Code";

    private static readonly MessageChannel[] SmsAndEmail = [MessageChannel.Sms, MessageChannel.Email];
    private static readonly MessageChannel[] SmsOnly = [MessageChannel.Sms];

    private static readonly string[] BookingTokens = ["SalonName", "ClientFirstName", "ServiceName", "StaffName", "AppointmentStart"];

    private static readonly Dictionary<string, MessageTemplateDefinition> Definitions = new MessageTemplateDefinition[]
    {
        new(BookingConfirmation, SmsAndEmail, [.. BookingTokens, "CancelUrl"]),
        new(BookingReminder, SmsAndEmail, [.. BookingTokens, "CancelUrl"]),
        new(BookingCancelled, SmsAndEmail, [.. BookingTokens, "BookingUrl"]),
        new(BookingCode, SmsOnly, ["SalonName", "Code"]),
    }.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static IReadOnlyList<MessageTemplateDefinition> All { get; } = [.. Definitions.Values];

    /// <summary>The definition for <paramref name="key"/> (case-sensitive).</summary>
    /// <exception cref="MessageTemplateNotFoundException">The key is not in the catalogue.</exception>
    public static MessageTemplateDefinition Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryGet(key, out var definition) ? definition : throw new MessageTemplateNotFoundException(key);
    }

    public static bool TryGet(string key, [MaybeNullWhen(false)] out MessageTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Definitions.TryGetValue(key, out definition);
    }
}