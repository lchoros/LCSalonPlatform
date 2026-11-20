using System.Text;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Payments;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.StripeWebhook;

/// <summary>
/// <c>POST /webhooks/stripe</c> (ADR 0082), behind <c>Features:StripeDeposits</c>: Stripe's own word that a Checkout
/// Session was completed, for the client who paid and never came back to the site. It arrives on the platform's
/// address, so there is no salon in the request. A body whose signature does not verify is refused and nothing of it
/// is read. A genuine one only names a session: whether it was paid is asked from the gateway, which also says which
/// salon and visit it is for, and the deposit is written by the same recorder the client's return uses, so both
/// together still write one line. Without a webhook secret the address does not exist.
/// </summary>
[FeatureGate(nameof(FeaturesOptions.StripeDeposits))]
[Route("webhooks/stripe")]
public sealed class StripeWebhookController(
    IOnlineDepositCheckout checkout,
    ITenantLookup tenants,
    ITenantScopeRunner tenantScopes,
    ILogger<StripeWebhookController> logger) : SalonControllerBase
{
    private const string SignatureHeader = "Stripe-Signature";

    /// <summary>Stripe's events are a few kilobytes; anything much larger is not one.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        var webhook = checkout.ReadWebhook(payload, Request.Headers[SignatureHeader].ToString());
        switch (webhook.State)
        {
            case OnlineWebhookState.NotConfigured:
                return NotFound();
            case OnlineWebhookState.BadSignature:
                logger.LogWarning("A Stripe webhook was refused: its signature does not verify");
                return BadRequest();
            case OnlineWebhookState.Ignored:
                return Ok();
            case OnlineWebhookState.CheckoutCompleted:
                break;
            default:
                throw new InvalidOperationException($"Unexpected webhook state {webhook.State}.");
        }

        var reading = await checkout.ReadAsync(webhook.SessionId, cancellationToken);
        if (reading.State == OnlineCheckoutState.Unavailable)
        {
            // Not an answer about the session: Stripe sends the event again later.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (reading is not { State: OnlineCheckoutState.Paid, Payment: { } payment })
        {
            return Ok();
        }

        var tenant = await tenants.FindByIdAsync(payment.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            logger.LogError("A paid Stripe session names tenant {TenantId}, which is unknown or closed; nothing recorded for appointment {AppointmentId}", payment.TenantId, payment.AppointmentId);
            return Ok();
        }

        // The salon comes from the session the gateway holds, and the work runs in a scope of that salon like a job's.
        var outcome = OnlineDepositOutcome.NotFound;
        await tenantScopes.RunAsync(
            tenant.Id,
            async (services, token) => outcome = await services.GetRequiredService<IDispatcher>().SendAsync(new RecordWebhookDepositCommand(payment), token),
            cancellationToken);
        logger.LogInformation("Stripe webhook for appointment {AppointmentId} of tenant {TenantId}: {Outcome}", payment.AppointmentId, tenant.Id, outcome);

        // Lost a race with another deposit being written: nothing was decided here, so Stripe is asked to send it again.
        return outcome == OnlineDepositOutcome.BeingRecorded ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok();
    }
}

/// <summary>Writes the deposit the gateway holds on the client's account, once however often the event is sent.</summary>
public sealed record RecordWebhookDepositCommand(OnlineDepositPayment Payment) : ICommand<OnlineDepositOutcome>
{
    public override string ToString() => $"{nameof(RecordWebhookDepositCommand)} {{ AppointmentId = {Payment.AppointmentId} }}";
}

public sealed class RecordWebhookDepositHandler(IClientAccountService account) : IRequestHandler<RecordWebhookDepositCommand, OnlineDepositOutcome>
{
    public Task<OnlineDepositOutcome> HandleAsync(RecordWebhookDepositCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.RecordOnlineDepositAsync(request.Payment, cancellationToken);
    }
}