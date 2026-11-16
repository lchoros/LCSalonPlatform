using System.Globalization;
using Salon.Core.Domain;
using Salon.Core.Products;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Products.ProductCard;

/// <summary>What a product's page says about its cost (ADR 0049, ADR 0043): the words, kept out of the view.</summary>
public static class ProductCostText
{
    /// <summary>
    /// "Costs you £6.00. You keep £6.50 of each one sold (52%)." The second sentence is left out for a product that is
    /// not for sale, and says so plainly when the product is sold for less than it costs.
    /// </summary>
    public static string Line(ILocalizationService localizer, Money cost, ProductMargin? margin, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        var costs = localizer.Format("Products.Card.Cost", cost.ToDisplayString(culture));
        if (margin is not { } kept)
        {
            return costs;
        }

        var amount = new Money(Math.Abs(kept.Amount), cost.Currency).ToDisplayString(culture);
        var keeps = kept switch
        {
            { Amount: < 0m } => localizer.Format("Products.Card.Loss", amount),
            { Percent: { } percent } => localizer.Format("Products.Card.Margin", amount, percent.ToString("0.#", culture)),
            _ => localizer.Format("Products.Card.MarginNoPercent", amount),
        };
        return costs + " " + keeps;
    }
}