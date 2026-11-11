using System.Globalization;

using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;

using Salon.Services;
using Salon.Services.Localization;
using Salon.Web.Features.Catalog.CreateService;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// Pure rules check (no database, no host) against the host's real embedded resources, so a renamed key or a missing
/// Polish message fails here. Lives in this project because only it references Salon.Web.
/// </summary>
public class CreateServiceValidatorTests
{
    private static readonly ILocalizationService Localizer = new ServiceCollection()
        .AddLogging()
        .AddSalonServices()
        .AddLocalizationResources(typeof(Program).Assembly)
        .BuildServiceProvider()
        .GetRequiredService<ILocalizationService>();

    private static readonly CreateServiceCommand Valid = new("Cut & blow-dry", "Wash, cut and finish.", 45, 45.00m);

    [Fact]
    public void A_valid_command_passes()
    {
        Validate(Valid, "en-GB").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Enter the service name.")]
    [InlineData("pl-PL", "Podaj nazwę usługi.")]
    public void Name_is_required_in_the_request_culture(string culture, string message)
    {
        foreach (var name in new[] { "", "   ", null })
        {
            var result = Validate(Valid with { Name = name! }, culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(CreateServiceCommand.Name));
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Fact]
    public void Name_is_measured_after_trimming()
    {
        Validate(Valid with { Name = "  " + new string('a', 200) + "  " }, "en-GB").IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Name = new string('a', 201) }, "en-GB");

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("The name can have at most 200 characters.");
    }

    [Theory]
    [InlineData("en-GB", "The description can have at most 2000 characters.")]
    [InlineData("pl-PL", "Opis może mieć najwyżej 2000 znaków.")]
    public void Description_is_optional_but_capped(string culture, string message)
    {
        Validate(Valid with { Description = null }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { Description = new string('x', 2000) }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Description = new string('x', 2001) }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(605)]
    public void Duration_must_be_between_5_and_600_minutes(int minutes)
    {
        var result = Validate(Valid with { DurationMinutes = minutes }, "pl-PL");

        result.Errors.Select(e => e.ErrorMessage).ShouldContain("Czas trwania musi wynosić od 5 do 600 minut.");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(600)]
    [InlineData(45)]
    public void Duration_on_a_five_minute_step_passes(int minutes)
    {
        Validate(Valid with { DurationMinutes = minutes }, "en-GB").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Duration must be a multiple of 5 minutes.")]
    [InlineData("pl-PL", "Czas trwania musi być wielokrotnością 5 minut.")]
    public void Duration_off_the_five_minute_step_fails(string culture, string message)
    {
        var result = Validate(Valid with { DurationMinutes = 47 }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100000.01")]
    public void Price_must_be_between_0_and_100000(string price)
    {
        var result = Validate(Valid with { Price = decimal.Parse(price, CultureInfo.InvariantCulture) }, "en-GB");

        result.Errors.Select(e => e.ErrorMessage).ShouldContain("Price must be between 0 and 100000.");
    }

    [Theory]
    [InlineData("en-GB", "Price can have at most two decimal places.")]
    [InlineData("pl-PL", "Cena może mieć najwyżej dwa miejsca po przecinku.")]
    public void Price_with_more_than_two_decimals_fails(string culture, string message)
    {
        Validate(Valid with { Price = 0m }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { Price = 100_000m }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Price = 45.005m }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Fact]
    public void Every_failing_rule_is_reported_at_once()
    {
        var result = Validate(new CreateServiceCommand("", new string('x', 2001), 3, 1.234m), "en-GB");

        result.Errors.Select(e => e.PropertyName).Distinct().Order().ShouldBe(["Description", "DurationMinutes", "Name", "Price"]);
    }

    private static ValidationResult Validate(CreateServiceCommand command, string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var requested = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = requested;
        CultureInfo.CurrentUICulture = requested;
        try
        {
            return new CreateServiceValidator(Localizer).Validate(command);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }
}