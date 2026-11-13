namespace Salon.Core.Messaging;

/// <summary>A catalogue entry: the channels a template key is sent on and the token names its texts may use.</summary>
/// <param name="Key">Dotted PascalCase key, e.g. "Booking.Confirmation"; at most 100 characters.</param>
/// <param name="Channels">The channels the template exists for.</param>
/// <param name="TokenNames">Token names (see <see cref="MessageTokens.IsValidName"/>) the default and any override may reference.</param>
public sealed record MessageTemplateDefinition(string Key, IReadOnlyList<MessageChannel> Channels, IReadOnlyList<string> TokenNames)
{
    public bool Supports(MessageChannel channel) => Channels.Contains(channel);

    /// <summary>True when <paramref name="tokenName"/> is one of <see cref="TokenNames"/>, ignoring case like the renderer does.</summary>
    /// <summary>Tokens the message is useless without (the code in a verification text); a salon's own wording must keep them.</summary>
    public IReadOnlyList<string> RequiredTokenNames { get; init; } = [];

    public bool AllowsToken(string tokenName) => TokenNames.Contains(tokenName, StringComparer.OrdinalIgnoreCase);
}