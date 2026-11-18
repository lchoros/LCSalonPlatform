using System.Globalization;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;

namespace Salon.Web.Features.Payments.Vouchers;

/// <summary>What the voucher pages say about a voucher (ADR 0057, ADR 0043): the words, kept out of the views.</summary>
public static class VoucherText
{
    /// <summary>"Can be used until 16/11/2027", "Used up", "Expired on ...", "Money given back".</summary>
    public static string Status(ILocalizationService localizer, VoucherSummary voucher, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(culture);
        return voucher.Status switch
        {
            GiftVoucherStatus.Refunded => localizer["Payments.Vouchers.Status.Refunded"],
            GiftVoucherStatus.Used => localizer["Payments.Vouchers.Status.Used"],
            GiftVoucherStatus.Expired => localizer.Format("Payments.Vouchers.Status.Expired", voucher.ExpiresOn?.ToString("d", culture)),
            _ => voucher.ExpiresOn is { } last
                ? localizer.Format("Payments.Vouchers.Status.ActiveUntil", last.ToString("d", culture))
                : localizer["Payments.Vouchers.Status.Active"],
        };
    }

    /// <summary>The line on the voucher's face: its last day, or that it has none.</summary>
    public static string Expiry(ILocalizationService localizer, VoucherSummary voucher, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(culture);
        return voucher.ExpiresOn is { } last
            ? localizer.Format("Payments.Vouchers.Face.Until", last.ToString("D", culture))
            : localizer["Payments.Vouchers.Face.NoExpiry"];
    }
}