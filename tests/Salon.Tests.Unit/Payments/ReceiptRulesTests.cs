using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Services.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>A receipt is built from the bill only (ADR 0070): what was kept, and how every part of it was paid.</summary>
public class ReceiptRulesTests
{
    private static readonly Guid Visit = Guid.NewGuid();
    private static readonly Guid OwnMethod = Guid.NewGuid();

    [Fact]
    public void A_deposit_a_voucher_a_part_the_payment_and_products_per_method_add_up_to_what_was_kept()
    {
        // A £60 colour: £10 deposit, a £15 voucher, £5 earlier by card, £30 at the checkout by "Monzo link" (the
        // salon's own method). Two shampoos by cash, one of them brought back; an oil by Monzo link; a mask not paid.
        var bill = Bill(60m) with
        {
            DepositHeld = Gbp(10m),
            Payment = Paid(30m, PaymentMethod.Other, "Monzo link"),
            Products =
            [
                Product("Shampoo", 2, 12.50m, PaymentMethod.Cash, returned: 1),
                Product("Oil", 1, 18m, PaymentMethod.Other, ownName: "Monzo link"),
                Product("Mask", 1, 9m, null),
            ],
            VoucherApplied = Gbp(15m),
            PartPayments = [Paid(5m, PaymentMethod.CardAtSalonTerminal, null) with { IsPart = true }],
        };

        ReceiptRules.Products(bill).Select(p => (p.Name, p.Quantity, p.Total.Amount)).ShouldBe([("Shampoo", 1, 12.50m), ("Oil", 1, 18m)]);
        var payments = ReceiptRules.Payments(bill);
        payments.Select(p => (p.Kind, p.Amount.Amount, p.Method, p.MethodName)).ShouldBe(
        [
            (ReceiptPaymentKind.Deposit, 10m, (PaymentMethod?)null, (string?)null),
            (ReceiptPaymentKind.Voucher, 15m, null, null),
            (ReceiptPaymentKind.Part, 5m, PaymentMethod.CardAtSalonTerminal, null),
            (ReceiptPaymentKind.Visit, 30m, PaymentMethod.Other, "Monzo link"),
            (ReceiptPaymentKind.Products, 12.50m, PaymentMethod.Cash, null),
            (ReceiptPaymentKind.Products, 18m, PaymentMethod.Other, "Monzo link"),
        ]);
        payments.Sum(p => p.Amount.Amount).ShouldBe(60m + 12.50m + 18m);
    }

    [Fact]
    public void A_visit_paid_in_full_at_the_checkout_has_one_payment_and_a_product_given_back_in_full_is_not_on_it()
    {
        var bill = Bill(20m) with
        {
            Payment = Paid(20m, PaymentMethod.Cash, null),
            Products = [Product("Spray", 1, 9m, PaymentMethod.Cash, returned: 1)],
        };

        ReceiptRules.Products(bill).ShouldBeEmpty();
        ReceiptRules.Payments(bill).ShouldHaveSingleItem().Kind.ShouldBe(ReceiptPaymentKind.Visit);
    }

    private static VisitBill Bill(decimal price)
        => new(
            Visit,
            Guid.NewGuid(),
            "Olivia Brown",
            "Colour",
            "Anna",
            new LocalDateTime(2026, 11, 19, 10, 0).InUtc(),
            AppointmentStatus.Completed,
            Gbp(price),
            null,
            null,
            [],
            []);

    private static Money Gbp(decimal amount) => new(amount, "GBP");

    private static VisitPaymentLine Paid(decimal amount, PaymentMethod method, string? ownName)
        => new(Guid.NewGuid(), Visit, Gbp(amount), Instant.FromUtc(2026, 11, 19, 11, 0), "Desk", method, "manual") { MethodName = ownName };

    private static BillProductLine Product(string name, int quantity, decimal price, PaymentMethod? paidWith, int returned = 0, string? ownName = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), name, quantity, Gbp(price), paidWith, paidWith is not null, returned)
        {
            PaidWithCustomMethodId = ownName is null ? null : OwnMethod,
            PaidWithName = ownName,
        };
}