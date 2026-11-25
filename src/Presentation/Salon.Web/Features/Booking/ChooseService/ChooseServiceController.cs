using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Public, anonymous start of the booking flow at <c>/book</c> on the salon's host (ADR 0014): one tappable card per
/// active service or group of variants, under its category, each leading to the time picker. A salon with more than a
/// handful of services also gets category tiles, a search and pages (<c>?category=&amp;q=&amp;page=</c>, ADR 0032); an
/// address that cannot be read shows the whole first page. The platform domain has no booking page.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class ChooseServiceController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup, ILocalizationService localizer, IVisitBasket basket) : SalonControllerBase
{
    public const string CategoryParameter = "category";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? category, string? q, int? page, string? add, string? also, string? staff, string? with, string? date, string? from, string? start, CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var choice = await dispatcher.SendAsync(new ChooseServiceQuery(Guid.TryParse(category, out var id) ? id : null, q, page), cancellationToken);

        // "Add another service" (ADR 0095): the same list, but a tap adds to the booking that add, also and staff
        // describe. The day and the time already chosen ride along, so the client comes back to them.
        var adding = basket.IsOpen && Guid.TryParse(add, out var first)
            ? new AddingTo(first, basket.Read(also, first), Guid.TryParse(staff, out _) || staff == PublicBookingDefaults.AnyStaff ? staff : null)
            {
                // Who does each service so far, the day and the time: passed through as they came (ADR 0096). The service
                // added gets no answer of its own here; the time page gives it the answer of the one before where that fits.
                Kept = [(VisitBasket.StaffParameter, Short(with, 160)), ("date", Short(date)), ("from", Short(from)), ("start", Short(start))],
            }
            : null;
        return View(new ChooseServiceViewModel(tenant?.Name ?? string.Empty, choice, Filter(choice, adding), Search(choice, adding), Pager(choice, adding)) { Adding = adding });
    }

    /// <summary>A day or a time passed through to the time page, which reads it and drops what it cannot read.</summary>
    private static string? Short(string? value, int most = 40) => value is { Length: > 0 } && value.Length <= most ? value : null;

    private static string ListAddress(Guid? categoryId, string? search, AddingTo? adding, int page = 1)
        => ListUrl.Build(
            PublicBookingDefaults.BookPath,
            [(CategoryParameter, categoryId?.ToString()), (ListUrl.SearchParameter, search), (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)), .. adding?.Carried ?? []]);

    /// <summary>"All" and one tile per category in use; choosing a category keeps the search and starts at page one.</summary>
    private FilterTilesModel Filter(ServiceChoice choice, AddingTo? adding)
    {
        var tiles = new List<FilterTile> { new("all", localizer["Booking.Public.AllCategories"], ListAddress(null, choice.Search, adding), choice.CategoryId is null) };
        // Calm over carnival (ADR 0080): the public page stopped wearing the five category accents.
        tiles.AddRange(choice.Categories.Select(c => new FilterTile(c.Id.ToString(), c.Name, ListAddress(c.Id, choice.Search, adding), c.Id == choice.CategoryId)));
        return new FilterTilesModel(localizer["Booking.Public.Categories"], tiles);
    }

    private SearchBoxModel Search(ServiceChoice choice, AddingTo? adding)
        => new(
            PublicBookingDefaults.BookPath,
            choice.Search,
            localizer["Booking.Public.SearchServices"],
            [(CategoryParameter, choice.CategoryId?.ToString()), .. adding?.Carried ?? []],
            ListAddress(choice.CategoryId, null, adding));

    private static PagerModel Pager(ServiceChoice choice, AddingTo? adding)
        => PagerModel.For(choice.Entries, page => ListAddress(choice.CategoryId, choice.Search, adding, page));
}

/// <param name="Filter">The category tiles, shown when <see cref="ServiceChoice.ShowFilters"/>.</param>
public sealed record ChooseServiceViewModel(string SalonName, ServiceChoice Choice, FilterTilesModel Filter, SearchBoxModel Search, PagerModel Pager)
{
    /// <summary>The entries of the page as one list, in the order of their categories; the names of the categories are on the filter tiles only.</summary>
    public IEnumerable<IGrouping<string?, BookableEntry>> Groups => Choice.Entries.Items.GroupBy(_ => (string?)null);

    /// <summary>Set while the list is adding a service to a booking already begun (ADR 0095); null on the ordinary first step.</summary>
    public AddingTo? Adding { get; init; }

    /// <summary>Where a tap on a service leads: its own time page, or the begun booking with this service added.</summary>
    public string ServicePath(Guid serviceId) => Adding?.With(serviceId) ?? $"{PublicBookingDefaults.BookPath}/{serviceId}";

    /// <summary>A service already in the begun booking is not offered a second time.</summary>
    public bool Offers(Guid serviceId) => Adding is null || !Adding.Has(serviceId);
}

/// <summary>
/// The booking a service is being added to (ADR 0095), as the address describes it: its first service, the further
/// ones so far, and the answer to "who with" (a person's id or "any") so the client is not asked again.
/// </summary>
public sealed record AddingTo(Guid First, IReadOnlyList<Guid> Extras, string? Staff)
{
    public const string AddParameter = "add";
    public const string StaffParameter = "staff";

    /// <summary>The day and the time chosen on the time page, passed through as they came.</summary>
    public IReadOnlyList<(string Name, string? Value)> Kept { get; init; } = [];

    /// <summary>Everything the list has to keep in its own addresses (categories, search, pages) to stay in this mode.</summary>
    public IReadOnlyList<(string Name, string? Value)> Carried => [(AddParameter, First.ToString()), (VisitBasket.Parameter, VisitBasket.Write(Extras)), (StaffParameter, Staff), .. Kept];

    public bool Has(Guid serviceId) => serviceId == First || Extras.Contains(serviceId);

    /// <summary>The time page of the booking as it is.</summary>
    public string BackPath => TimePage(Extras);

    /// <summary>The time page of the booking with one more service at its end.</summary>
    public string With(Guid serviceId) => TimePage([.. Extras, serviceId]);

    private string TimePage(IEnumerable<Guid> extras)
        => ListUrl.Build($"{PublicBookingDefaults.BookPath}/{First}", [(VisitBasket.Parameter, VisitBasket.Write(extras)), (StaffParameter, Staff), .. Kept]);
}