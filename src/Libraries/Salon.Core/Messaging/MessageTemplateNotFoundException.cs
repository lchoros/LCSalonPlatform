namespace Salon.Core.Messaging;

/// <summary>A template key is not in <see cref="MessageTemplateCatalog"/>, or no text exists for the requested channel and culture.</summary>
public class MessageTemplateNotFoundException : Exception
{
    public MessageTemplateNotFoundException(string key, MessageChannel? channel = null, string? culture = null)
        : base(Describe(key, channel, culture))
    {
        Key = key;
        Channel = channel;
        Culture = culture;
    }

    public MessageTemplateNotFoundException()
    {
    }

    public MessageTemplateNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Key { get; }

    public MessageChannel? Channel { get; }

    public string? Culture { get; }

    private static string Describe(string key, MessageChannel? channel, string? culture)
    {
        var scope = (channel, culture) switch
        {
            (null, null) => string.Empty,
            (not null, null) => $" for channel {channel}",
            (null, not null) => $" for culture '{culture}'",
            _ => $" for channel {channel} and culture '{culture}'",
        };

        return $"Message template '{key}' was not found{scope}.";
    }
}