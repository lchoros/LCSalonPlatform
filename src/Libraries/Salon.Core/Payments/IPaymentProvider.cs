using Salon.Core.Domain;
using Salon.Core.Plugins;

namespace Salon.Core.Payments;

/// <summary>
/// Payment plugin contract. Phase 1 ships only <c>ManualPaymentProvider</c>, which records money taken at the salon's own
/// terminal or in cash and never charges anything. Stripe Connect drops in at M3 behind the same contract.
/// </summary>
public interface IPaymentProvider : IPlugin
{
    /// <summary>True only for gateways that can charge a card online. Manual is always false.</summary>
    bool SupportsOnlineCharge { get; }

    Task<PaymentResult> RecordDepositAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResult> RecordInSalonPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResult> RecordNoShowFeeAsync(PaymentRequest request, CancellationToken cancellationToken = default);
}

public enum PaymentMethod
{
    Cash = 0,
    CardAtSalonTerminal = 1,
    BankTransfer = 2,
    Voucher = 3,
}

public sealed record PaymentRequest(
    Guid TenantId,
    Guid AppointmentId,
    Money Amount,
    PaymentMethod Method,
    string? Note = null);

public sealed record PaymentResult(bool Succeeded, string ProviderReference, string? Error = null)
{
    public static PaymentResult Success(string reference) => new(true, reference);

    public static PaymentResult Failure(string error) => new(false, string.Empty, error);
}