using System.Globalization;
using Salon.Core.Domain;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Reports.Overview;

/// <summary>How the report writes a rate (ADR 0026, ADR 0043): the words, kept out of the view.</summary>
public static class ReportText
{
    /// <summary>A rate as "12.5%", or "No data yet" when there is nothing to divide by.</summary>
    public static string Percent(ILocalizationService localizer, decimal? rate, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        return rate is { } value ? localizer.Format("Reports.Percent", value.ToString("0.#", culture)) : localizer["Reports.NotKnown"];
    }

    /// <summary>
    /// The commission line's label with the rates it used (ADR 0059): "Commission (40% services, 10% products)", only
    /// the rates the person has.
    /// </summary>
    public static string CommissionLabel(ILocalizationService localizer, Salon.Services.Reports.StaffLine person, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(culture);
        var rates = new List<string>(2);
        if (person.ServiceRate is { } services)
        {
            rates.Add(localizer.Format("Reports.Staff.ServiceRate", services.ToString("0.##", culture)));
        }

        if (person.ProductRate is { } products)
        {
            rates.Add(localizer.Format("Reports.Staff.ProductRate", products.ToString("0.##", culture)));
        }

        return localizer.Format("Reports.Staff.Commission", string.Join(", ", rates));
    }

    /// <summary>
    /// A change against the period before in words (ADR 0046), so it reads without colour or an arrow: "Up 35%",
    /// "Down 12.5%", "No change", or "Nothing before" when there is nothing to be more or less than.
    /// </summary>
    public static string Change(ILocalizationService localizer, decimal? change, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        return change switch
        {
            null => localizer["Reports.Compare.NothingBefore"],
            0m => localizer["Reports.Compare.NoChange"],
            > 0m => localizer.Format("Reports.Compare.Up", change.Value.ToString("0.#", culture)),
            _ => localizer.Format("Reports.Compare.Down", (-change.Value).ToString("0.#", culture)),
        };
    }

    /// <summary>
    /// The profit on products (ADR 0052) as "£13.00 (52%)", or the amount alone when there is nothing to take a
    /// share of. A loss keeps its minus.
    /// </summary>
    public static string Profit(ILocalizationService localizer, Money profit, decimal? share, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        var amount = profit.ToDisplayString(culture);
        if (share is not { } percent)
        {
            return amount;
        }

        // The same real minus sign the money beside it has, not a hyphen.
        var written = percent < 0m ? "−" + (-percent).ToString("0.#", culture) : percent.ToString("0.#", culture);
        return localizer.Format("Reports.Retail.ProfitWithShare", amount, written);
    }
}