using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Services.Settings;

namespace Salon.Services.Payments;

/// <summary>One line of a "how was it paid" list: what the form posts and what the desk reads.</summary>
public sealed record PaymentOption(PaymentChoice Choice, string Name)
{
    public string Value => Choice.Value;
}

/// <summary>A choice the salon really has, as a ledger line keeps it (ADR 0060).</summary>
/// <param name="MethodName">The name of the salon's own method today; null for a built-in one.</param>
public sealed record ResolvedPaymentMethod(PaymentMethod Method, Guid? CustomMethodId, string? MethodName);

/// <param name="IsUsed">Money was recorded with it: it can be taken out of use, not removed.</param>
public sealed record OwnPaymentMethod(Guid Id, string Name, bool IsActive, bool IsUsed);

/// <summary>What the settings page shows: which built-in methods the salon takes, and its own.</summary>
public sealed record PaymentMethodList(PaymentMethodSettings BuiltIn, IReadOnlyList<OwnPaymentMethod> Own);

public enum PaymentMethodOutcome
{
    Saved = 1,

    /// <summary>The name is empty or longer than <see cref="SalonPaymentMethod.NameMaxLength"/>.</summary>
    NameInvalid = 2,

    /// <summary>The salon already has a method called that, or it is the name of a built-in one.</summary>
    NameTaken = 3,

    /// <summary>The salon already has <see cref="SalonPaymentMethodRules.MaxMethods"/> methods of its own in use.</summary>
    TooMany = 4,

    /// <summary>The salon has no such method.</summary>
    NotFound = 5,
}

public sealed record PaymentMethodResult(PaymentMethodOutcome Outcome, Guid? Id = null, string? Name = null);

/// <summary>
/// The ways a salon takes money (ADR 0060): the built-in methods it has not switched off, and the ones it named
/// itself. Every "how was it paid" list comes from here, and every service that records money asks here whether a
/// posted choice is one the salon has. Nothing is charged through a method: it names how money the salon took itself
/// changed hands.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>
    /// The methods to offer, built-in first, then the salon's own in its order.
    /// </summary>
    /// <param name="forVoucher">Leave out "voucher": a gift voucher is not bought or given back with another one.</param>
    Task<IReadOnlyList<PaymentOption>> OptionsAsync(bool forVoucher = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// What to write on a ledger line for a posted choice; null when it is not a method of this salon: a malformed
    /// choice, an id the salon does not have, or one of its own taken out of use. A built-in method the salon
    /// switched off is still recorded: switching off only takes it out of the lists.
    /// </summary>
    Task<ResolvedPaymentMethod?> ResolveAsync(PaymentChoice choice, CancellationToken cancellationToken = default);

    Task<PaymentMethodList> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Which built-in methods the salon takes.</summary>
    Task SaveBuiltInAsync(PaymentMethodSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Adds a method of the salon's own; one it had before and took out of use comes back instead.</summary>
    Task<PaymentMethodResult> AddAsync(string? name, CancellationToken cancellationToken = default);

    /// <summary>Renames a method. Lines already recorded keep the name of their day.</summary>
    Task<PaymentMethodResult> RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default);

    /// <summary>Takes a method out of the lists. One no money was ever recorded with is removed altogether.</summary>
    Task<PaymentMethodResult> RetireAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings a method taken out of use back.</summary>
    Task<PaymentMethodResult> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>How a method is written for a person: the salon's own by its name, a built-in one by its resource.</summary>
public static class PaymentMethodNames
{
    public static string ResourceKey(PaymentMethod method) => $"Payments.Method.{method}";

    /// <summary>In the culture of the request.</summary>
    public static string Name(ILocalizationService localizer, PaymentMethod? method, string? ownName)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return !string.IsNullOrEmpty(ownName) ? ownName : localizer[ResourceKey(method ?? PaymentMethod.Cash)];
    }

    /// <summary>In a given culture, for a message that is not part of a request.</summary>
    public static string Name(ILocalizationService localizer, PaymentMethod? method, string? ownName, string culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return !string.IsNullOrEmpty(ownName) ? ownName : localizer.Get(ResourceKey(method ?? PaymentMethod.Cash), culture);
    }
}

internal sealed class PaymentMethodService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ISettingService settings,
    ILocalizationService localizer,
    IClock clock,
    ILogger<PaymentMethodService> logger) : IPaymentMethodService
{
    public async Task<IReadOnlyList<PaymentOption>> OptionsAsync(bool forVoucher = false, CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var builtIn = await settings.LoadAsync<PaymentMethodSettings>(cancellationToken).ConfigureAwait(false);
        var own = await db.SalonPaymentMethods.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var options = new List<PaymentOption>();
        foreach (var method in SalonPaymentMethodRules.BuiltIn)
        {
            if (forVoucher && method == PaymentMethod.Voucher)
            {
                continue;
            }

            if (SalonPaymentMethodRules.IsOffered(method, builtIn))
            {
                options.Add(new PaymentOption(method, localizer[PaymentMethodNames.ResourceKey(method)]));
            }
        }

        options.AddRange(own.Select(m => new PaymentOption(PaymentChoice.Own(m.Id), m.Name)));
        return options;
    }

    public async Task<ResolvedPaymentMethod?> ResolveAsync(PaymentChoice choice, CancellationToken cancellationToken = default)
    {
        RequireTenant();
        if (!choice.IsWellFormed)
        {
            return null;
        }

        if (choice.CustomMethodId is not { } id)
        {
            return new ResolvedPaymentMethod(choice.Method, null, null);
        }

        var name = await db.SalonPaymentMethods.AsNoTracking()
            .Where(m => m.Id == id && m.IsActive)
            .Select(m => m.Name)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return name is null ? null : new ResolvedPaymentMethod(PaymentMethod.Other, id, name);
    }

    public async Task<PaymentMethodList> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var builtIn = await settings.LoadAsync<PaymentMethodSettings>(cancellationToken).ConfigureAwait(false);
        var own = await db.SalonPaymentMethods.AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ThenBy(m => m.Id)
            .Select(m => new OwnPaymentMethod(m.Id, m.Name, m.IsActive, db.ClientAccountEntries.Any(e => e.CustomMethodId == m.Id)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PaymentMethodList(builtIn, own);
    }

    public async Task SaveBuiltInAsync(PaymentMethodSettings builtIn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        var tenantId = RequireTenant();
        await settings.SaveAsync(builtIn, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Built-in payment methods of tenant {TenantId} set: card {Card}, bank transfer {BankTransfer}, voucher {Voucher}",
            tenantId,
            builtIn.CardAtSalonTerminal,
            builtIn.BankTransfer,
            builtIn.Voucher);
    }

    public async Task<PaymentMethodResult> AddAsync(string? name, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (SalonPaymentMethodRules.Normalize(name) is not { } clean || !SalonPaymentMethodRules.IsName(clean))
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NameInvalid);
        }

        if (IsBuiltInName(clean))
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NameTaken);
        }

        var all = await db.SalonPaymentMethods.ToListAsync(cancellationToken).ConfigureAwait(false);
        var same = all.FirstOrDefault(m => SalonPaymentMethodRules.SameName(m.Name, clean));
        if (same is { IsActive: true })
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NameTaken);
        }

        if (all.Count(m => m.IsActive) >= SalonPaymentMethodRules.MaxMethods)
        {
            return new PaymentMethodResult(PaymentMethodOutcome.TooMany);
        }

        if (same is not null)
        {
            // The salon had it before: the same method comes back, so its history is one line in the reports.
            same.IsActive = true;
            same.Name = clean;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Payment method {MethodId} of tenant {TenantId} brought back", same.Id, tenantId);
            return new PaymentMethodResult(PaymentMethodOutcome.Saved, same.Id, same.Name);
        }

        var method = new SalonPaymentMethod
        {
            TenantId = tenantId,
            Name = clean,
            SortOrder = all.Count == 0 ? 1 : all.Max(m => m.SortOrder) + 1,
            CreatedAt = clock.GetCurrentInstant(),
        };
        db.SalonPaymentMethods.Add(method);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Payment method {MethodId} added for tenant {TenantId}", method.Id, tenantId);
        return new PaymentMethodResult(PaymentMethodOutcome.Saved, method.Id, method.Name);
    }

    public async Task<PaymentMethodResult> RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (SalonPaymentMethodRules.Normalize(name) is not { } clean || !SalonPaymentMethodRules.IsName(clean))
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NameInvalid);
        }

        var all = await db.SalonPaymentMethods.ToListAsync(cancellationToken).ConfigureAwait(false);
        var method = all.FirstOrDefault(m => m.Id == id);
        if (method is null)
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NotFound);
        }

        if (IsBuiltInName(clean) || all.Any(m => m.Id != id && SalonPaymentMethodRules.SameName(m.Name, clean)))
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NameTaken);
        }

        method.Name = clean;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Payment method {MethodId} of tenant {TenantId} renamed", method.Id, tenantId);
        return new PaymentMethodResult(PaymentMethodOutcome.Saved, method.Id, method.Name);
    }

    public async Task<PaymentMethodResult> RetireAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var method = await db.SalonPaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (method is null)
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NotFound);
        }

        var used = await db.ClientAccountEntries.AnyAsync(e => e.CustomMethodId == id, cancellationToken).ConfigureAwait(false);
        if (used)
        {
            method.IsActive = false;
        }
        else
        {
            db.SalonPaymentMethods.Remove(method);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Payment method {MethodId} of tenant {TenantId} taken out of use (removed: {Removed})", id, tenantId, !used);
        return new PaymentMethodResult(PaymentMethodOutcome.Saved, id, method.Name);
    }

    public async Task<PaymentMethodResult> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var all = await db.SalonPaymentMethods.ToListAsync(cancellationToken).ConfigureAwait(false);
        var method = all.FirstOrDefault(m => m.Id == id);
        if (method is null)
        {
            return new PaymentMethodResult(PaymentMethodOutcome.NotFound);
        }

        if (!method.IsActive)
        {
            if (all.Count(m => m.IsActive) >= SalonPaymentMethodRules.MaxMethods)
            {
                return new PaymentMethodResult(PaymentMethodOutcome.TooMany);
            }

            method.IsActive = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Payment method {MethodId} of tenant {TenantId} brought back", id, tenantId);
        }

        return new PaymentMethodResult(PaymentMethodOutcome.Saved, id, method.Name);
    }

    /// <summary>"Cash" or "Gotówka" as a method of the salon's own would be two lines the desk cannot tell apart.</summary>
    private bool IsBuiltInName(string name)
        => SalonPaymentMethodRules.BuiltIn.Append(PaymentMethod.Other).Any(method =>
            localizer.Cultures.Any(culture => SalonPaymentMethodRules.SameName(localizer.Get(PaymentMethodNames.ResourceKey(method), culture), name)));

    private Guid RequireTenant()
        => tenantContext.TenantId ?? throw new TenantViolationException("Payment methods belong to a salon, but no tenant is set in the current scope.");
}