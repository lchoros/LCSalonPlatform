using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.VerifyCode;

/// <summary>
/// The code typed for one verification (ADR 0014). A right code books the appointment the verification carries; a wrong
/// one counts an attempt. <see cref="VerifyCodeValidator"/> refuses anything that is not six digits before an attempt is spent.
/// </summary>
public sealed record VerifyCodeCommand(Guid VerificationId, string? Code) : ICommand<PublicBookingVerification>
{
    /// <summary>The code never appears in a log line.</summary>
    public override string ToString() => $"{nameof(VerifyCodeCommand)} {{ VerificationId = {VerificationId} }}";
}

/// <summary>What the verify page needs to render: the masked phone and whether the row can still take a code. A query.</summary>
public sealed record PendingVerificationQuery(Guid VerificationId) : IQuery<PendingVerification?>;
