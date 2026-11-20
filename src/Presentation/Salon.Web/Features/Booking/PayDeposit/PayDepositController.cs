using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.PayDeposit;

/// <summary>
/// The pay step of an online booking that asks for a deposit (ADR 0082), behind <c>Features:StripeDeposits</c>.
/// <c>/book/pay/{id}</c> sends the client to the gateway's own payment page; <c>/book/pay/{id}/return</c> is where the
/// gateway sends them back. Coming back proves nothing: the gateway is asked, server to server, whether that session
/// was paid, and only then is the deposit written on the client's account, once however often the address is opened.
/// Whatever happens, the client ends on the confirmation of a booking that stands: paid, or with the deposit still due.
/// </summary>
[RequireTenant]
[FeatureGate(nameof(FeaturesOptions.StripeDeposits))]
[Route("book/pay")]
public sealed class PayDepositController(IDispatcher dispatcher, ITenantContext tenantContext, ILogger<PayDepositController> logger) : SalonControllerBase
{
    [HttpGet("{id:guid}")]
    [EnableRateLimiting(PublicBookingDefaults.DepositPayRateLimitPolicy)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken cancellationToken)
    {
        var start = await dispatcher.SendAsync(new StartDepositCheckoutQuery(id), cancellationToken);
        if (!start.Found)
        {
            return NotFound();
        }

        // Nothing to pay online (none asked, already received, gateway away): the confirmation says how things stand.
        return Redirect(start.RedirectUrl ?? PublicBookingDefaults.DonePath(id));
    }

    [HttpGet("{id:guid}/return")]
    [EnableRateLimiting(PublicBookingDefaults.DepositPayRateLimitPolicy)]
    public async Task<IActionResult> Return(Guid id, [FromQuery(Name = OnlineDepositCheckoutDefaults.SessionIdQueryKey)] string? sessionId, CancellationToken cancellationToken)
    {
        var reading = await dispatcher.SendAsync(new DepositSessionQuery(sessionId), cancellationToken);

        // A session of another visit or another salon is not this page's business, whoever pasted it into the address.
        if (reading is { State: OnlineCheckoutState.Paid, Payment: { } payment }
            && payment.AppointmentId == id
            && payment.TenantId == tenantContext.TenantId)
        {
            var outcome = await dispatcher.SendAsync(new RecordPaidDepositCommand(payment), cancellationToken);
            logger.LogInformation("Client came back from the payment gateway for appointment {AppointmentId}: {Outcome}", payment.AppointmentId, outcome);
        }
        else
        {
            logger.LogInformation("Client came back from the payment gateway without a payment to record: {State}", reading.State);
        }

        return Redirect(PublicBookingDefaults.DonePath(id));
    }
}