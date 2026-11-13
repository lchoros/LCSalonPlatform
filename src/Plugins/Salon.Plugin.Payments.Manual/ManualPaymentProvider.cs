using Microsoft.Extensions.Logging;

using Salon.Core.Payments;

namespace Salon.Plugin.Payments.Manual;

/// <summary>
/// Phase 1 payment provider. It only records that money changed hands at the salon (cash, the salon's own card terminal,
/// bank transfer, voucher). It never contacts a gateway and never stores card data.
/// </summary>
public sealed class ManualPaymentProvider(ILogger<ManualPaymentProvider> logger) : IPaymentProvider
{
    public const string PluginSystemName = "Payments.Manual";

    public string SystemName => PluginSystemName;

    public string DisplayNameKey => "Plugins.Payments.Manual.DisplayName";

    public bool SupportsOnlineCharge => false;

    public Task<PaymentResult> RecordDepositAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => Record("deposit", request);

    public Task<PaymentResult> RecordDepositReturnAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => Record("deposit-return", request);

    public Task<PaymentResult> RecordInSalonPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => Record("payment", request);

    public Task<PaymentResult> RecordNoShowFeeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => Record("no-show-fee", request);

    private Task<PaymentResult> Record(string kind, PaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount.Amount < 0m)
        {
            return Task.FromResult(PaymentResult.Failure("Amount must not be negative."));
        }

        var reference = $"manual:{kind}:{Guid.CreateVersion7():N}";
        logger.LogInformation(
            "Recorded manual {Kind} of {Amount} by {Method} for appointment {AppointmentId} (tenant {TenantId}) ref {Reference}",
            kind, request.Amount, request.Method, request.AppointmentId, request.TenantId, reference);

        return Task.FromResult(PaymentResult.Success(reference));
    }
}