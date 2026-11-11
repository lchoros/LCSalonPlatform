using Salon.Core.Cqrs;
using Salon.Services.Identity;

namespace Salon.Web.Features.Identity.Login;

/// <summary>
/// One sign-in attempt for the current salon. <see cref="LoginValidator"/> only checks both fields are present; whether
/// they match is the handler's answer, which is the same for an unknown email and a wrong password (ADR 0009).
/// </summary>
/// <param name="Email">As typed; the sign-in service trims and lower-cases it.</param>
/// <param name="Password">Plain text for this request only; never stored or logged.</param>
public sealed record LoginCommand(string Email, string Password) : ICommand<SignInResult>
{
    /// <summary>Keeps the password out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(LoginCommand)} {{ Email = {Email} }}";
}