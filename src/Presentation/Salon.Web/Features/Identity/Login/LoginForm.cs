namespace Salon.Web.Features.Identity.Login;

/// <summary>The login form as posted and as re-rendered. The password is blanked before the model goes back to the view.</summary>
/// <param name="ReturnUrl">Where to go after signing in; only a local path is honoured.</param>
public sealed record LoginForm(string Email, string Password, string? ReturnUrl);