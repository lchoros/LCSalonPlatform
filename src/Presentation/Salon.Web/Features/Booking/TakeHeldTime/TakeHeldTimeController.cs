using Microsoft.AspNetCore.Mvc;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.TakeHeldTime;

/// <summary>The time kept for a waiting client, by the secret of their link (ADR 0065). A query: no transaction, tenant required.</summary>
public sealed record HeldTimeQuery(string? Token) : IQuery<HeldTime?>;

/// <summary>
/// Books the time kept for a waiting client (ADR 0065). A command: the offer is taken and the visit is booked, or
/// neither is.
/// </summary>
public sealed record TakeHeldTimeCommand(string? Token) : ICommand<TakeHeldTimeResult>;

public sealed class HeldTimeHandler(IWaitlistOfferService offers) : IRequestHandler<HeldTimeQuery, HeldTime?>
{
    public Task<HeldTime?> HandleAsync(HeldTimeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return offers.FindAsync(request.Token, cancellationToken);
    }
}

/// <summary>Hands the link to <see cref="IWaitlistOfferService"/>, which books by the same rules as any booking.</summary>
public sealed class TakeHeldTimeHandler(IWaitlistOfferService offers) : IRequestHandler<TakeHeldTimeCommand, TakeHeldTimeResult>
{
    public Task<TakeHeldTimeResult> HandleAsync(TakeHeldTimeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return offers.TakeAsync(request.Token, cancellationToken);
    }
}

/// <summary>
/// <c>/w/{token}</c> (ADR 0065), "w" for waitlist: the short address in the text a waiting client gets when a time
/// has come up and is kept for them. GET shows the time and until when it is kept, with one button; POST books it for
/// the client the text went to. Nobody signs in: the link went to their phone, as the link to a visit does
/// (ADR 0014). A link this salon does not know, one whose time is no longer kept, and one already used each get an
/// explanation on the same page (200, not 404: the client followed a link from their own text).
/// </summary>
[RequireTenant]
[Route("w")]
public sealed class TakeHeldTimeController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    /// <summary>The page of one link and the address its button posts to (ADR 0043: addresses are built here, not in the view).</summary>
    public static string LinkPath(string token) => "/w/" + Uri.EscapeDataString(token);

    /// <summary>The other times of the same service; the ChooseSlot slice owns the route.</summary>
    public static string OtherTimesPath(Guid serviceId) => PublicBookingDefaults.BookPath + "/" + serviceId;

    [HttpGet("{token}")]
    public async Task<IActionResult> Show(string token, CancellationToken cancellationToken)
    {
        var time = await dispatcher.SendAsync(new HeldTimeQuery(token), cancellationToken);
        var state = time?.State switch
        {
            null => HeldTimeState.Invalid,
            WaitlistOfferState.Held => HeldTimeState.Held,
            WaitlistOfferState.Taken => HeldTimeState.Booked,
            _ => HeldTimeState.Over,
        };
        return View(await ViewModelAsync(token, time, state, cancellationToken));
    }

    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Take(string token, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new TakeHeldTimeCommand(token), cancellationToken);
        var state = result.Outcome switch
        {
            TakeHeldTimeOutcome.Booked or TakeHeldTimeOutcome.AlreadyBooked => HeldTimeState.Booked,
            TakeHeldTimeOutcome.Over => HeldTimeState.Over,
            TakeHeldTimeOutcome.Gone => HeldTimeState.Gone,
            _ => HeldTimeState.Invalid,
        };
        var time = await dispatcher.SendAsync(new HeldTimeQuery(token), cancellationToken);
        return View("Show", await ViewModelAsync(token, time, time is null ? HeldTimeState.Invalid : state, cancellationToken));
    }

    private async Task<TakeHeldTimeViewModel> ViewModelAsync(string token, HeldTime? time, HeldTimeState state, CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        return new TakeHeldTimeViewModel(
            time,
            state,
            tenant?.Name ?? string.Empty,
            LinkPath(token),
            time is null ? PublicBookingDefaults.BookPath : OtherTimesPath(time.ServiceId),
            time?.VisitToken is { } visit ? PublicBookingDefaults.CancelPath(visit) : null);
    }
}

public enum HeldTimeState
{
    /// <summary>The time is kept for the client: show the button.</summary>
    Held,

    /// <summary>The client has the visit.</summary>
    Booked,

    /// <summary>The time is no longer kept: the hold ran out or the salon let it go.</summary>
    Over,

    /// <summary>The client tried to take it and it could not be booked any more.</summary>
    Gone,

    /// <summary>Not a link of this salon.</summary>
    Invalid,
}

/// <param name="TakePath">Where the button posts to.</param>
/// <param name="OtherTimesPath">The booking page of the same service, when this time is not to be had.</param>
/// <param name="VisitPath">The client's own page of the visit they booked, to change or cancel it.</param>
public sealed record TakeHeldTimeViewModel(HeldTime? Time, HeldTimeState State, string SalonName, string TakePath, string OtherTimesPath, string? VisitPath);