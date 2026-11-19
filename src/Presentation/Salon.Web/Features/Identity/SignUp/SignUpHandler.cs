using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Identity;
using Salon.Services.Localization;

namespace Salon.Web.Features.Identity.SignUp;

/// <summary>
/// Writes the salon and its owner's login in the command's one transaction (ADR 0075). The slug decides the salon's
/// address; the country decides zone, language and currency. The new tenant is put in scope before the user is
/// created, so the login lands in the right salon; a slug taken meanwhile is the database's unique index to refuse.
/// </summary>
public sealed class SignUpHandler(
    SalonDbContext db,
    IUserService users,
    IMutableTenantContext tenantContext,
    ILocalizationService localizer,
    IClock clock,
    ILogger<SignUpHandler> logger) : IRequestHandler<SignUpCommand, SignUpResult>
{
    public async Task<SignUpResult> HandleAsync(SignUpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = TenantSlugRules.Normalize(request.Slug);
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            throw Taken();
        }

        var england = request.Country == SignUpCountry.England;
        var tenant = new Tenant
        {
            Slug = slug,
            Name = request.SalonName.Trim(),
            TimeZoneId = england ? "Europe/London" : "Europe/Warsaw",
            Culture = england ? "en-GB" : "pl-PL",
            Currency = england ? "GBP" : "PLN",
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.Tenants.Add(tenant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two people chose the same address at the same moment; the second is told it is taken.
            throw Taken();
        }

        // The new salon is the current one from here, so the owner's login is written into it.
        tenantContext.SetTenant(tenant.Id);
        var owner = await users.CreateAsync(request.Email, request.DisplayName.Trim(), request.Password, Role.Owner, cancellationToken);

        // Ids only (the log rule): the salon's name and address are the owner's business, not the log's.
        logger.LogInformation("Salon {TenantId} signed up; owner login {UserId}", tenant.Id, owner.Id);
        return new SignUpResult(slug);
    }

    private ValidationException Taken()
        => new([new ValidationFailure(nameof(SignUpCommand.Slug), localizer["Identity.SignUp.Validation.SlugTaken"])]);
}