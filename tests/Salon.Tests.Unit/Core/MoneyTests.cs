using Salon.Core.Domain;

namespace Salon.Tests.Unit.Core;

public class MoneyTests
{
    [Fact]
    public void Adding_same_currency_sums_amounts()
    {
        var total = new Money(12.50m, "GBP").Add(new Money(7.50m, "GBP"));

        total.ShouldBe(new Money(20.00m, "GBP"));
    }

    [Fact]
    public void Adding_different_currencies_is_rejected()
    {
        var gbp = new Money(10m, "GBP");

        Should.Throw<InvalidOperationException>(() => gbp.Add(new Money(10m, "PLN")));
    }

    [Fact]
    public void Formats_with_two_decimals_and_currency_code()
    {
        new Money(150m, "PLN").ToString().ShouldBe("150.00 PLN");
    }
}