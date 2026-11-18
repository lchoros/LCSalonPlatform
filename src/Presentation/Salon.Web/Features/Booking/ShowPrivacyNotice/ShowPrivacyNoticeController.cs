using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Services.Localization;
using Salon.Services.Privacy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ShowPrivacyNotice;

/// <summary>The salon's privacy notice as a client reads it (ADR 0063).</summary>
public sealed record PrivacyNoticeQuery : IQuery<PrivacyNotice>;

public sealed class PrivacyNoticeHandler(IPrivacyNoticeService notices) : IRequestHandler<PrivacyNoticeQuery, PrivacyNotice>
{
    public Task<PrivacyNotice> HandleAsync(PrivacyNoticeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return notices.GetAsync(cancellationToken);
    }
}

/// <summary>
/// <c>/privacy</c> on a salon's address (ADR 0063): what the salon does with a client's details, for anybody to read
/// before they give them. No sign-in. The standard notice says what this system does; a salon may show its own text
/// in its place. Linked from every page a client sees and from the forms that ask for their details.
/// </summary>
[RequireTenant]
[Route("privacy")]
public sealed class ShowPrivacyNoticeController(IDispatcher dispatcher) : SalonControllerBase
{
    /// <summary>The salon's price list, the way back; the ListServices slice owns the route.</summary>
    public const string ServicesPath = "/services";

    [HttpGet("")]
    public async Task<IActionResult> Show(CancellationToken cancellationToken)
        => View(new PrivacyNoticeViewModel(await dispatcher.SendAsync(new PrivacyNoticeQuery(), cancellationToken)));
}

public sealed record PrivacyNoticeViewModel(PrivacyNotice Notice);

/// <summary>What the notice says about the salon itself (ADR 0063, ADR 0043): the words, kept out of the view.</summary>
public static class PrivacyNoticeText
{
    /// <summary>"Studio 20, 20 Drake Street, Rochdale, OL16 1NT, decides how your details are used." With or without an address.</summary>
    public static string Who(ILocalizationService localizer, PrivacyNotice notice)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(notice);
        return notice.Address is { Length: > 0 } address
            ? localizer.Format("Booking.Privacy.Who.WithAddress", notice.SalonName, address)
            : localizer.Format("Booking.Privacy.Who", notice.SalonName);
    }

    /// <summary>"Call us on 01632 960020 or write to hello@example.test." With whichever the salon entered.</summary>
    public static string Contact(ILocalizationService localizer, PrivacyNotice notice)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(notice);
        return (notice.Phone, notice.Email) switch
        {
            ({ } phone, { } email) => localizer.Format("Booking.Privacy.Contact.Both", phone, email),
            ({ } phone, null) => localizer.Format("Booking.Privacy.Contact.Phone", phone),
            (null, { } email) => localizer.Format("Booking.Privacy.Contact.Email", email),
            _ => localizer["Booking.Privacy.Contact.AtSalon"],
        };
    }
}