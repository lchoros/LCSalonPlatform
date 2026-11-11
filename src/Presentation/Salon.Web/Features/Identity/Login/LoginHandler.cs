using Salon.Core.Cqrs;
using Salon.Services.Identity;

namespace Salon.Web.Features.Identity.Login;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003), so the failed-attempt counter or the lockout the sign-in service
/// records is committed with the attempt. The HTTP side (cookie, redirect) stays in the controller.
/// </summary>
public sealed class LoginHandler(ISignInService signIn) : IRequestHandler<LoginCommand, SignInResult>
{
    public Task<SignInResult> HandleAsync(LoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return signIn.AttemptAsync(request.Email, request.Password, cancellationToken);
    }
}