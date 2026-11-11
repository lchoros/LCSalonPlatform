namespace Salon.Core.Messaging;

/// <summary>How a message reaches the client. Stored by integer value; never renumber.</summary>
public enum MessageChannel
{
    Sms = 1,
    Email = 2,
}