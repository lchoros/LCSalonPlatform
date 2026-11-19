using Salon.Core.Cqrs;

namespace Salon.Web.Features.Identity.SignUp;

/// <summary>Where the salon is: the choice sets the time zone, the language and the currency in one step.</summary>
public enum SignUpCountry
{
    England = 1,
    Poland = 2,
}

/// <summary>
/// Creates a salon and its owner's login in one step (ADR 0075). Bound straight from the public form;
/// <see cref="SignUpValidator"/> holds the rules, the handler normalizes the slug and the email and writes the
/// tenant and the user in one transaction.
/// </summary>
/// <param name="SalonName">The name clients see, e.g. "Studio 20".</param>
/// <param name="Slug">The salon's web address: "studio-20" becomes studio-20 on the platform's domain.</param>
/// <param name="DisplayName">The owner's own name, shown in the admin header.</param>
public sealed record SignUpCommand(string SalonName, string Slug, SignUpCountry Country, string DisplayName, string Email, string Password) : ICommand<SignUpResult>, IPlatformRequest
{
    /// <summary>The empty form: England, because the platform starts there.</summary>
    public static SignUpCommand Empty { get; } = new(string.Empty, string.Empty, SignUpCountry.England, string.Empty, string.Empty, string.Empty);
}

/// <param name="Slug">The new salon's address part, normalized.</param>
public sealed record SignUpResult(string Slug);