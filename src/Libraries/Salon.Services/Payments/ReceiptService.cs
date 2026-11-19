using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Messaging;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Locations;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>
/// The receipt of a paid visit (ADR 0070): what the client had and how it was paid, read from the visit's bill, so the
/// receipt says what the till and the reports say. It covers the visit and the products bought on it; fees from
/// earlier visits paid at the same time are not on it.
/// </summary>
/// <param name="ClientEmail">Null when the client has no email address or was forgotten (ADR 0062).</param>
/// <param name="Products">The products the client kept: units brought back and given the money for are left out.</param>
/// <param name="Payments">How the visit and the products were paid, in the order the money came in.</param>
public sealed record VisitReceipt(
    Guid AppointmentId,
    string SalonName,
    SalonLocation? Location,
    string ClientFirstName,
    string ClientName,
    string? ClientEmail,
    string ServiceName,
    string StaffName,
    ZonedDateTime Start,
    Money Price,
    IReadOnlyList<ReceiptProduct> Products,
    IReadOnlyList<ReceiptPayment> Payments,
    ZonedDateTime PaidAt)
{
    /// <summary>The visit and the products kept.</summary>
    public Money Total => Price with { Amount = Price.Amount + Products.Sum(p => p.Total.Amount) };

    /// <summary>Every payment added up; equal to <see cref="Total"/> for a bill that is settled.</summary>
    public Money Paid => Price with { Amount = Payments.Sum(p => p.Amount.Amount) };
}

public sealed record ReceiptProduct(string Name, int Quantity, Money UnitPrice)
{
    public Money Total => UnitPrice with { Amount = ProductSaleRules.LineTotal(Quantity, UnitPrice.Amount) };
}

/// <summary>One way part of the bill was paid.</summary>
/// <param name="Method">How; null for a deposit and a gift voucher, which say what they are themselves.</param>
/// <param name="MethodName">The name of the salon's own method on that day (ADR 0060); null for a built-in one.</param>
public sealed record ReceiptPayment(ReceiptPaymentKind Kind, Money Amount, PaymentMethod? Method, string? MethodName);

public enum ReceiptPaymentKind
{
    /// <summary>The deposit paid when booking (ADR 0023).</summary>
    Deposit = 1,

    /// <summary>Gift vouchers used toward the visit (ADR 0057).</summary>
    Voucher = 2,

    /// <summary>A part paid before the rest (ADR 0061).</summary>
    Part = 3,

    /// <summary>The payment that settled the visit at the checkout.</summary>
    Visit = 4,

    /// <summary>The products, per method they were paid with.</summary>
    Products = 5,
}

public enum ReceiptEmailOutcome
{
    /// <summary>The receipt was handed to the mail sender.</summary>
    Sent = 1,

    /// <summary>The salon has no such visit, or it is not paid yet.</summary>
    NoReceipt = 2,

    /// <summary>The client has no email address.</summary>
    NoEmail = 3,

    /// <summary>The salon has switched emails to clients off.</summary>
    EmailsOff = 4,
}

/// <summary>A receipt is built from the bill only (ADR 0070). Pure.</summary>
public static class ReceiptRules
{
    /// <summary>The products the client kept, in the bill's order: paid lines less the units brought back.</summary>
    public static IReadOnlyList<ReceiptProduct> Products(VisitBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        return bill.Products
            .Where(p => p.IsPaid && p.Kept > 0)
            .Select(p => new ReceiptProduct(p.Name, p.Kept, p.UnitPrice))
            .ToArray();
    }

    /// <summary>
    /// How the bill was paid: the deposit, the vouchers, each part, the payment at the checkout, then the products
    /// kept per method (by the name the method had on the day).
    /// </summary>
    public static IReadOnlyList<ReceiptPayment> Payments(VisitBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var payments = new List<ReceiptPayment>();
        if (bill.DepositHeld is { Amount: > 0m } deposit)
        {
            payments.Add(new ReceiptPayment(ReceiptPaymentKind.Deposit, deposit, null, null));
        }

        if (bill.VoucherApplied is { Amount: > 0m } voucher)
        {
            payments.Add(new ReceiptPayment(ReceiptPaymentKind.Voucher, voucher, null, null));
        }

        payments.AddRange(bill.PartPayments.Select(p => new ReceiptPayment(ReceiptPaymentKind.Part, p.Amount, p.Method, p.MethodName)));
        if (bill.Payment is { Amount.Amount: > 0m } paid)
        {
            payments.Add(new ReceiptPayment(ReceiptPaymentKind.Visit, paid.Amount, paid.Method, paid.MethodName));
        }

        payments.AddRange(bill.Products
            .Where(p => p.IsPaid && p.Kept > 0)
            .GroupBy(p => (p.PaidWith, p.PaidWithCustomMethodId, p.PaidWithName))
            .Select(g => new ReceiptPayment(
                ReceiptPaymentKind.Products,
                bill.Price with { Amount = g.Sum(p => ProductSaleRules.LineTotal(p.Kept, p.UnitPrice.Amount)) },
                g.Key.PaidWith,
                g.Key.PaidWithName)));
        return payments;
    }
}

/// <summary>The receipt of a paid visit, on the desk's screen and by email to the client (ADR 0070).</summary>
public interface IReceiptService
{
    /// <summary>The receipt; null when the salon has no such visit or it has not been paid at the checkout.</summary>
    Task<VisitReceipt?> GetAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Emails the receipt to the client, in the salon's language. Nothing is stored.</summary>
    Task<ReceiptEmailOutcome> EmailAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

internal sealed class ReceiptService(
    SalonDbContext db,
    IClientAccountService account,
    ILocationService locations,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    ILocalizationService localizer,
    IEmailSender email,
    ILogger<ReceiptService> logger) : IReceiptService
{
    private const string Keys = "Payments.Receipt.";

    public async Task<VisitReceipt?> GetAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var bill = await account.GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false);
        if (bill?.Payment is not { } payment)
        {
            return null;
        }

        var tenant = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == bill.ClientId)
            .Select(c => new { c.FirstName, c.Email, c.ErasedAt })
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        return new VisitReceipt(
            bill.AppointmentId,
            tenant.Name,
            await locations.GetMainAsync(cancellationToken).ConfigureAwait(false),
            client.FirstName,
            bill.ClientName,
            client.ErasedAt is null && !string.IsNullOrWhiteSpace(client.Email) ? client.Email : null,
            bill.ServiceName,
            bill.StaffName,
            bill.Start,
            bill.Price,
            ReceiptRules.Products(bill),
            ReceiptRules.Payments(bill),
            payment.At.InZone(zone));
    }

    public async Task<ReceiptEmailOutcome> EmailAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var receipt = await GetAsync(appointmentId, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return ReceiptEmailOutcome.NoReceipt;
        }

        if (receipt.ClientEmail is not { } to)
        {
            return ReceiptEmailOutcome.NoEmail;
        }

        if (!(await settings.LoadAsync<NotificationSettings>(cancellationToken).ConfigureAwait(false)).EmailEnabled)
        {
            return ReceiptEmailOutcome.EmailsOff;
        }

        var tenant = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var (subject, text) = Compose(receipt, tenant.Culture);
        await email.SendAsync(new EmailMessage(to, subject, EmailBodies.ToHtml(text), text), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Receipt email for appointment {AppointmentId} handed to the mail sender", appointmentId);
        return ReceiptEmailOutcome.Sent;
    }

    /// <summary>The email in the salon's language: the same lines as the page, then where the salon is.</summary>
    private (string Subject, string Text) Compose(VisitReceipt receipt, string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        string Text(string key, params object?[] args) => string.Format(culture, localizer.Get(Keys + key, cultureName), args);
        string Amount(Money money) => money.ToDisplayString(culture);

        var text = new StringBuilder();
        text.Append(Text("Email.Greeting", receipt.ClientFirstName)).Append("\n\n");
        text.Append(Text("Visit", receipt.Start.Date.ToDayHeading(culture), receipt.Start.TimeOfDay.ToShortTime(culture), receipt.StaffName)).Append("\n\n");
        text.Append(receipt.ServiceName).Append(": ").Append(Amount(receipt.Price)).Append('\n');
        foreach (var product in receipt.Products)
        {
            text.Append(Text("ProductLine", product.Quantity, product.Name)).Append(": ").Append(Amount(product.Total)).Append('\n');
        }

        text.Append(Text("Total", Amount(receipt.Total))).Append("\n\n");
        text.Append(Text("PaidHeading")).Append('\n');
        foreach (var payment in receipt.Payments)
        {
            text.Append("- ").Append(Text("Paid." + payment.Kind, PaymentMethodNames.Name(localizer, payment.Method, payment.MethodName, cultureName)))
                .Append(" — ").Append(Amount(payment.Amount)).Append('\n');
        }

        text.Append('\n').Append(Text("Covers")).Append('\n');
        var body = AppointmentNotifier.WithFooter(text.ToString(), receipt.SalonName, receipt.Location);
        return (Text("Email.Subject", receipt.SalonName, receipt.PaidAt.Date.ToDayHeading(culture)), body);
    }

    private async Task<TenantInfo> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A receipt belongs to one salon; a tenant must be in scope.");
        return await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
    }
}