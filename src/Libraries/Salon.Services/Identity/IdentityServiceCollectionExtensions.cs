using Microsoft.Extensions.DependencyInjection;

namespace Salon.Services.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers users and sign-in (ADR 0009). Both need an <see cref="IPasswordService"/>, which the service layer cannot
    /// provide itself (the framework's hasher ships with ASP.NET Core), so the host calls this from
    /// <c>AddSalonAuthentication</c> right after registering the hasher; it is not part of <c>AddSalonServices</c>.
    /// </summary>
    public static IServiceCollection AddSalonIdentity(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISignInService, SignInService>();
        return services;
    }
}