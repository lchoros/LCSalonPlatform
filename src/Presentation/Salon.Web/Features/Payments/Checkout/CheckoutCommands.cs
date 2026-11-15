using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Services.Settings;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Payments.Checkout;

/// <summary>
/// The bill of one visit (ADR 0024): price, deposit held, what is left, the client's open fees. Null when the salon has
/// no such appointment. A query: no transaction, tenant required.
/// </summary>
public sealed record CheckoutQuery(Guid AppointmentId) : IQuery<VisitBill?>;

/// <summary>
/// Records that the client paid the bill with one method: what is left of the visit and the open fees ticked on the
/// form (<paramref name="Fees"/>: the visits those fees are about). An arrived visit is completed in the same
/// transaction. Nothing is charged (Phase 1).
/// </summary>
public sealed record PayBillCommand(Guid AppointmentId, PaymentMethod? Method, IReadOnlyList<Guid> Fees, Guid? ByUserId) : ICommand<VisitBill>;

public sealed class PayBillValidator : AbstractValidator<PayBillCommand>
{
    public PayBillValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Method)
            .Must(method => method is { } chosen && Enum.IsDefined(chosen))
            .WithMessage(_ => localizer["Payments.Checkout.Validation.MethodRequired"]);
    }
}

public sealed class CheckoutHandler(IClientAccountService account) : IRequestHandler<CheckoutQuery, VisitBill?>
{
    public Task<VisitBill?> HandleAsync(CheckoutQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.GetBillAsync(request.AppointmentId, cancellationToken);
    }
}

/// <summary>The products the desk can ring up on a bill (ADR 0037): in use and priced, by name. A query: no transaction, tenant required.</summary>
public sealed record SellableProductsQuery : IQuery<IReadOnlyList<SellableProduct>>;

/// <param name="OnHand">What the record says is on the shelf; shown so the desk sees a shelf that reads empty.</param>
public sealed record SellableProduct(Guid Id, string Name, Money Price, int OnHand);

/// <summary>Reads through the tenant filter and picks with <see cref="ProductSaleRules"/>: a salon has tens of retail products.</summary>
public sealed class SellableProductsHandler(SalonDbContext db) : IRequestHandler<SellableProductsQuery, IReadOnlyList<SellableProduct>>
{
    public async Task<IReadOnlyList<SellableProduct>> HandleAsync(SellableProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.RetailPrice != null).ToListAsync(cancellationToken);
        return products
            .Where(ProductSaleRules.IsForSale)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new SellableProduct(p.Id, p.Name, new Money(p.RetailPrice!.Value, p.Currency), p.StockOnHand))
            .ToArray();
    }
}

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the payment lines and the visit's move to "completed" commit
/// together or not at all.
/// </summary>
public sealed class PayBillHandler(IClientAccountService account, IBookingService booking) : IRequestHandler<PayBillCommand, VisitBill>
{
    public async Task<VisitBill> HandleAsync(PayBillCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var method = request.Method ?? throw new ArgumentException("A payment needs its method.", nameof(request));
        var bill = await account.PayBillAsync(request.AppointmentId, method, request.Fees, request.ByUserId, cancellationToken);
        if (bill.Status != AppointmentStatus.Arrived)
        {
            return bill;
        }

        // Paying is how a visit ends at the desk: no second tap on "Complete".
        await booking.CompleteAsync(request.AppointmentId, request.ByUserId, cancellationToken);
        return bill with { Status = AppointmentStatus.Completed };
    }
}
/// <summary>
/// The one-tap choices for a client's next visit (ADR 0039): the same service with the same person, so many weeks
/// after this visit, each as an address of the booking form. A query: no transaction, tenant required.
/// </summary>
public sealed record RebookChoicesQuery(Guid ClientId, Guid ServiceId, Guid StaffMemberId, LocalDate VisitDay, DateTimeZone Zone) : IQuery<IReadOnlyList<RebookChoice>>;

/// <param name="Day">The day the booking form opens on.</param>
/// <param name="Url">The booking form with the client, the service, the person and the day filled in.</param>
public sealed record RebookChoice(int Weeks, LocalDate Day, string Url);

/// <summary>Reads the salon's weeks from <see cref="RebookSettings"/> and lets <see cref="RebookRules"/> pick the days; nothing is booked here.</summary>
public sealed class RebookChoicesHandler(ISettingService settings, IClock clock) : IRequestHandler<RebookChoicesQuery, IReadOnlyList<RebookChoice>>
{
    public async Task<IReadOnlyList<RebookChoice>> HandleAsync(RebookChoicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var today = clock.GetCurrentInstant().InZone(request.Zone).Date;
        var weeks = RebookRules.Choices(await settings.LoadAsync<RebookSettings>(cancellationToken));
        return weeks
            .Select(w =>
            {
                var day = RebookRules.Day(request.VisitDay, today, w);
                var url = ListUrl.Build(
                    CheckoutController.BookPath,
                    ("clientId", request.ClientId.ToString()),
                    ("serviceId", request.ServiceId.ToString()),
                    ("staffId", request.StaffMemberId.ToString()),
                    ("date", LocalDatePattern.Iso.Format(day)));
                return new RebookChoice(w, day, url);
            })
            .ToArray();
    }
}