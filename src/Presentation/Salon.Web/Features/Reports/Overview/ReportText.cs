using System.Globalization;
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
}