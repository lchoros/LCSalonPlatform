using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>Which deposit a booking asks for (ADR 0023): the most specific rule wins, zero opts out, the amount is to the penny.</summary>
public class DepositRulesTests
{
    [Theory]
    [InlineData(0, null, null, 0)]
    [InlineData(20, null, null, 20)]
    [InlineData(20, 50, null, 50)]
    [InlineData(20, 0, null, 0)]
    [InlineData(20, 50, 0, 0)]
    [InlineData(20, 50, 100, 100)]
    [InlineData(0, null, 30, 30)]
    [InlineData(0, 0, 30, 30)]
    [InlineData(150, null, null, 100)]
    [InlineData(20, -5, null, 0)]
    public void The_clients_rule_beats_the_services_and_the_services_beats_the_salons(int salon, int? service, int? client, int expected)
    {
        DepositRules.Percent(salon, service, client).ShouldBe(expected);
    }

    [Theory]
    [InlineData(45, 20, 9)]
    [InlineData(19.99, 50, 10.00)]
    [InlineData(45, 100, 45)]
    [InlineData(45, 0, 0)]
    [InlineData(0, 50, 0)]
    public void The_deposit_is_a_percentage_of_the_price_to_the_penny(decimal price, decimal percent, decimal expected)
    {
        DepositRules.Amount(price, percent).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void A_percentage_is_between_nought_and_a_hundred_or_left_empty(int? percent, bool valid)
    {
        DepositRules.IsPercentInRange(percent).ShouldBe(valid);
    }
}