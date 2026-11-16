using Salon.Core.Cqrs;
using Salon.Core.Paging;
using Salon.Services.Appointments;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Appointments.Waitlist;

/// <summary>
/// The clients waiting for a visit (ADR 0041), the one who asked first on top, cut into pages (ADR 0032). A query: no
/// transaction, tenant required.
/// </summary>
/// <param name="Page">1-based; out of range moves to the nearest page.</param>
public sealed record WaitlistQuery(int? Page = null) : IQuery<Paged<WaitlistItem>>
{
    public const int PageSize = 20;
}

/// <summary>Takes a waiting client off the list by hand (ADR 0041). False when the salon has no such waiting entry.</summary>
/// <param name="ByUserId">The login that removes; set by the controller, never from the form.</param>
public sealed record RemoveFromWaitlistCommand(Guid EntryId, Guid? ByUserId) : ICommand<bool>;

/// <summary>The waitlist service picks and names the entries; the page is cut here.</summary>
public sealed class WaitlistHandler(IWaitlistService waitlist) : IRequestHandler<WaitlistQuery, Paged<WaitlistItem>>
{
    public async Task<Paged<WaitlistItem>> HandleAsync(WaitlistQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Pages.Cut(await waitlist.ListWaitingAsync(cancellationToken), request.Page, WaitlistQuery.PageSize);
    }
}

public sealed class RemoveFromWaitlistHandler(IWaitlistService waitlist) : IRequestHandler<RemoveFromWaitlistCommand, bool>
{
    public Task<bool> HandleAsync(RemoveFromWaitlistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return waitlist.RemoveAsync(request.EntryId, request.ByUserId, cancellationToken);
    }
}

/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record WaitlistViewModel(Paged<WaitlistItem> Waiting, PagerModel Pager, string? Flash);