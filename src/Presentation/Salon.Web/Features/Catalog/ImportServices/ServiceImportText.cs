using System.Globalization;
using Salon.Core.Catalog;
using Salon.Core.Domain;
using Salon.Core.Import;
using Salon.Services.Catalog.Import;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.ImportServices;

/// <summary>What the price list preview says about a row (ADR 0066, ADR 0043): the words, kept out of the view.</summary>
public static class ServiceImportText
{
    private const string Separator = " · ";

    public static string Status(ILocalizationService localizer, ServiceImportStatus status)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer[$"Catalog.Import.Status.{status}"];
    }

    public static string Problem(ILocalizationService localizer, ServiceImportProblem problem)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return problem switch
        {
            ServiceImportProblem.NameTooLong => localizer.Format("Catalog.Import.Problem.NameTooLong", ServiceRules.NameMaxLength),
            ServiceImportProblem.CategoryTooLong => localizer.Format("Catalog.Import.Problem.CategoryTooLong", ServiceRules.CategoryNameMaxLength),
            ServiceImportProblem.VariantTooLong => localizer.Format("Catalog.Import.Problem.VariantTooLong", ServiceRules.VariantLabelMaxLength, ServiceRules.VariantGroupMaxLength),
            ServiceImportProblem.InvalidMinutes => localizer.Format("Catalog.Import.Problem.InvalidMinutes", ServiceRules.MinDurationMinutes, ServiceRules.MaxDurationMinutes, ServiceRules.StepMinutes),
            _ => localizer[$"Catalog.Import.Problem.{problem}"],
        };
    }

    /// <summary>
    /// One line under the name. A service that is added: its heading, time, price and deposit as they will be saved.
    /// A service that changes: only what changes, as it is and as it will be. A service that stays: its time and price.
    /// </summary>
    public static string Details(ILocalizationService localizer, ClassifiedServiceRow row, string currency, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(culture);
        string Minutes(int minutes) => localizer.Format("Catalog.Import.Minutes", minutes);
        string Price(decimal price) => new Money(price, currency).ToDisplayString(culture);
        string Deposit(decimal? percent) => percent is { } value
            ? localizer.Format("Catalog.Import.Deposit", value.ToString("0.##", culture))
            : localizer["Catalog.Import.Deposit.SalonRule"];
        string Change(string from, string to) => localizer.Format("Catalog.Import.Change", from, to);

        if (row.Change is { } change)
        {
            var parts = change.Any
                ? new[]
                {
                    change.MinutesChange ? Change(Minutes(change.MinutesFrom), Minutes(change.MinutesTo)) : null,
                    change.PriceChanges ? Change(Price(change.PriceFrom), Price(change.PriceTo)) : null,
                    change.DepositChanges ? Change(Deposit(change.DepositFrom), Deposit(change.DepositTo)) : null,
                }
                : [Minutes(change.MinutesFrom), Price(change.PriceFrom)];
            return string.Join(Separator, parts.Where(p => p is not null));
        }

        var service = row.Row;
        var given = new[]
        {
            service.Category,
            service.Minutes is { } minutes ? Minutes(minutes) : null,
            service.Price is { } price ? Price(price) : null,
            service.DepositPercent is not null ? Deposit(service.DepositPercent) : null,
        };
        return string.Join(Separator, given.Where(p => !string.IsNullOrEmpty(p))).OrDash();
    }
}