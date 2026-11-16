using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Payments;

namespace Salon.Web.Features.Payments.BillProducts;

/// <summary>
/// Puts a product on a visit's bill (ADR 0037). Nothing is paid and no stock moves until the bill is paid. A command,
/// so the line is written in the pipeline's transaction (ADR 0003).
/// </summary>
/// <param name="ByUserId">The login that rings it up; set by the controller, never from the form.</param>
public sealed record AddBillProductCommand(Guid AppointmentId, Guid ProductId, int Quantity, Guid? ByUserId) : ICommand<VisitBill>;

/// <summary>Takes an unpaid product line off a visit's bill (ADR 0037).</summary>
public sealed record RemoveBillProductCommand(Guid AppointmentId, Guid LineId) : ICommand<VisitBill>;

/// <summary>Hands the change to <see cref="IClientAccountService"/>, which owns the bill; nothing about money is decided in a slice.</summary>
public sealed class AddBillProductHandler(IClientAccountService account) : IRequestHandler<AddBillProductCommand, VisitBill>
{
    public Task<VisitBill> HandleAsync(AddBillProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.AddProductAsync(request.AppointmentId, request.ProductId, request.Quantity, request.ByUserId, cancellationToken);
    }
}

public sealed class RemoveBillProductHandler(IClientAccountService account) : IRequestHandler<RemoveBillProductCommand, VisitBill>
{
    public Task<VisitBill> HandleAsync(RemoveBillProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.RemoveProductAsync(request.AppointmentId, request.LineId, cancellationToken);
    }
}
/// <summary>
/// Records that the client brought back units of a paid product line and was given the money (ADR 0040). A command,
/// so the money line, the bill line and the shelf change together in the pipeline's transaction (ADR 0003).
/// </summary>
/// <param name="BackOnShelf">True when the units can be sold again and go back on their shelf.</param>
/// <param name="ByUserId">The login that records; set by the controller, never from the form.</param>
public sealed record ReturnBillProductCommand(Guid AppointmentId, Guid LineId, int Quantity, PaymentMethod Method, bool BackOnShelf, Guid? ByUserId) : ICommand<VisitBill>;

public sealed class ReturnBillProductHandler(IClientAccountService account) : IRequestHandler<ReturnBillProductCommand, VisitBill>
{
    public Task<VisitBill> HandleAsync(ReturnBillProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.ReturnProductAsync(request.AppointmentId, request.LineId, request.Quantity, request.Method, request.BackOnShelf, request.ByUserId, cancellationToken);
    }
}