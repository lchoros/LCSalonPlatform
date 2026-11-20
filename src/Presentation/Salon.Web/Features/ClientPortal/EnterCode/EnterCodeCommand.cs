using FluentValidation;
using Salon.Core.Booking;
using Salon.Core.Cqrs;
using Salon.Services.ClientPortal;
using Salon.Services.Localization;

namespace Salon.Web.Features.ClientPortal.EnterCode;

/// <summary>What the code page shows; null when the id is not a sign-in of this salon. A query: no transaction, tenant required.</summary>
public sealed record PendingCodeQuery(Guid VerificationId) : IQuery<ClientPortalPendingCode?>;

/// <summary>The six digits from the text message (ADR 0083). A command: a wrong code is counted on the row.</summary>
public sealed record EnterCodeCommand(Guid VerificationId, string? Code) : ICommand<ClientPortalSignIn>
{
    /// <summary>Keeps the code out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(EnterCodeCommand)} {{ VerificationId = {VerificationId} }}";
}

/// <summary>Only the shape: six digits. Anything else is asked for again without counting an attempt, as on a booking.</summary>
public sealed class EnterCodeValidator : AbstractValidator<EnterCodeCommand>
{
    public EnterCodeValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Code)
            .Must(code => PhoneVerificationRules.TryNormaliseCode(code, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.CodeInvalid"]);
    }
}

public sealed class PendingCodeHandler(IClientPortalService portal) : IRequestHandler<PendingCodeQuery, ClientPortalPendingCode?>
{
    public Task<ClientPortalPendingCode?> HandleAsync(PendingCodeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return portal.FindPendingAsync(request.VerificationId, cancellationToken);
    }
}

/// <summary>Runs inside the pipeline's transaction: the counted attempt, or the stamp that spends the row, is committed with the answer.</summary>
public sealed class EnterCodeHandler(IClientPortalService portal) : IRequestHandler<EnterCodeCommand, ClientPortalSignIn>
{
    public Task<ClientPortalSignIn> HandleAsync(EnterCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return portal.VerifyAsync(request.VerificationId, request.Code, cancellationToken);
    }
}