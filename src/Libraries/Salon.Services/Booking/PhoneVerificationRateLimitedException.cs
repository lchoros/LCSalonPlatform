namespace Salon.Services.Booking;

/// <summary>Too many codes went to one phone number inside the window (ADR 0014); the UI asks the client to wait.</summary>
public sealed class PhoneVerificationRateLimitedException : InvalidOperationException
{
    public PhoneVerificationRateLimitedException()
        : base("Too many verification codes were requested for this phone number; try again later.")
    {
    }

    public PhoneVerificationRateLimitedException(string message)
        : base(message)
    {
    }

    public PhoneVerificationRateLimitedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
