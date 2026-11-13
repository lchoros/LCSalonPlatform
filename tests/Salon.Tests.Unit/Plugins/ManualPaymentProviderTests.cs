using Microsoft.Extensions.Logging.Abstractions;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Plugin.Payments.Manual;

namespace Salon.Tests.Unit.Plugins;

public class ManualPaymentProviderTests
{
    private readonly ManualPaymentProvider _provider = new(NullLogger<ManualPaymentProvider>.Instance);

    private static PaymentRequest Request(decimal amount = 20m)
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), new Money(amount, "GBP"), PaymentMethod.CardAtSalonTerminal);

    [Fact]
    public void Never_supports_online_charges()
    {
        _provider.SupportsOnlineCharge.ShouldBeFalse();
        _provider.SystemName.ShouldBe("Payments.Manual");
    }

    [Fact]
    public async Task Records_a_deposit_with_a_manual_reference()
    {
        var result = await _provider.RecordDepositAsync(Request());

        result.Succeeded.ShouldBeTrue();
        result.ProviderReference.ShouldStartWith("manual:deposit:");
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Records_in_salon_payment_and_no_show_fee_with_distinct_kinds()
    {
        var payment = await _provider.RecordInSalonPaymentAsync(Request());
        var fee = await _provider.RecordNoShowFeeAsync(Request(15m));

        payment.ProviderReference.ShouldStartWith("manual:payment:");
        fee.ProviderReference.ShouldStartWith("manual:no-show-fee:");
        payment.ProviderReference.ShouldNotBe(fee.ProviderReference);
    }

    [Fact]
    public async Task Rejects_negative_amounts()
    {
        var result = await _provider.RecordDepositAsync(Request(-1m));

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }
}