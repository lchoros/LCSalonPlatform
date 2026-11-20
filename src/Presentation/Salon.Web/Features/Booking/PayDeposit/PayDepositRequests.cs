using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Services.Payments;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Features.Booking.PayDeposit;

/// <summary>
/// Opens the payment gateway's page for the deposit of an online booking (ADR 0082). A query: nothing of the salon's
/// changes, so no transaction is held open while the gateway is asked.
/// </summary>
public sealed record StartDepositCheckoutQuery(Guid AppointmentId) : IQuery<DepositCheckoutStart>;

/// <param name="Found">False when the salon has no such online booking.</param>
/// <param name="RedirectUrl">The gateway's page; null when there is nothing to pay there now.</param>
public sealed record DepositCheckoutStart(bool Found, string? RedirectUrl = null);

/// <summary>What the gateway says about the session a client came back with. A query; the gateway is the one asked.</summary>
public sealed record DepositSessionQuery(string? SessionId) : IQuery<OnlineCheckoutReading>
{
    /// <summary>The id came out of an address; it stays out of any log line.</summary>
    public override string ToString() => nameof(DepositSessionQuery);
}

/// <summary>Writes the deposit the gateway holds on the client's account, once however often it is sent.</summary>
public sealed record RecordPaidDepositCommand(OnlineDepositPayment Payment) : ICommand<OnlineDepositOutcome>
{
    public override string ToString() => $"{nameof(RecordPaidDepositCommand)} {{ AppointmentId = {Payment.AppointmentId} }}";
}

public sealed class StartDepositCheckoutHandler(
    IPublicBookingService publicBooking,
    IClientAccountService account,
    IOnlineDepositCheckout checkout,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IPublicUrlBuilder urls,
    ILocalizationService localizer) : IRequestHandler<StartDepositCheckoutQuery, DepositCheckoutStart>
{
    public async Task<DepositCheckoutStart> HandleAsync(StartDepositCheckoutQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same rule as the confirmation page: only a booking made online has this step.
        var appointment = await publicBooking.FindAsync(request.AppointmentId, cancellationToken);
        if (appointment is null)
        {
            return new DepositCheckoutStart(false);
        }

        if (!checkout.IsAvailable || appointment.Deposit is not { } due || !appointment.IsActive)
        {
            return new DepositCheckoutStart(true);
        }

        // One deposit per visit (ADR 0023): paid here before, or taken at the desk meanwhile, and there is nothing to pay.
        var deposit = await account.GetDepositAsync(appointment.Id, cancellationToken);
        if (deposit is null || deposit.Deposit is not null)
        {
            return new DepositCheckoutStart(true);
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(StartDepositCheckoutQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");

        // Both addresses come from configuration, never from the request's Host header. The line names the service
        // and the salon; nothing about the client goes to the gateway.
        var started = await checkout.StartAsync(
            new OnlineCheckoutRequest(
                tenant.Id,
                appointment.Id,
                due,
                localizer.Format("Booking.Public.Deposit.CheckoutLine", appointment.ServiceName, tenant.Name),
                urls.SalonUrl(tenant.Slug, PublicBookingDefaults.PayDepositReturnPath(appointment.Id)),
                urls.SalonUrl(tenant.Slug, PublicBookingDefaults.DonePath(appointment.Id))),
            cancellationToken);
        return new DepositCheckoutStart(true, started.RedirectUrl);
    }
}

public sealed class DepositSessionHandler(IOnlineDepositCheckout checkout) : IRequestHandler<DepositSessionQuery, OnlineCheckoutReading>
{
    public Task<OnlineCheckoutReading> HandleAsync(DepositSessionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return checkout.ReadAsync(request.SessionId, cancellationToken);
    }
}

public sealed class RecordPaidDepositHandler(IClientAccountService account) : IRequestHandler<RecordPaidDepositCommand, OnlineDepositOutcome>
{
    public Task<OnlineDepositOutcome> HandleAsync(RecordPaidDepositCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.RecordOnlineDepositAsync(request.Payment, cancellationToken);
    }
}