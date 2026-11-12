using System.Globalization;

using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;

using Salon.Services;
using Salon.Services.Localization;
using Salon.Web.Features.Staff.CreateStaff;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// Pure rules check (no database, no host) against the host's real embedded resources, so a renamed key or a missing
/// Polish message fails here. Lives in this project because only it references Salon.Web.
/// </summary>
public class CreateStaffValidatorTests
{
    private static readonly ILocalizationService Localizer = new ServiceCollection()
        .AddLogging()
        .AddSalonServices()
        .AddLocalizationResources(typeof(Program).Assembly)
        .BuildServiceProvider()
        .GetRequiredService<ILocalizationService>();

    private static readonly CreateStaffCommand Valid = new("Anna Kowalska", "anna@example.com", "+44 7700 900123", "#be185d", true);

    [Fact]
    public void A_valid_command_passes_and_so_does_the_empty_form_with_a_name()
    {
        Validate(Valid, "en-GB").IsValid.ShouldBeTrue();
        Validate(CreateStaffCommand.Empty with { DisplayName = "Tom" }, "pl-PL").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Enter the staff member's name.")]
    [InlineData("pl-PL", "Podaj imię i nazwisko pracownika.")]
    public void The_name_is_required_in_the_request_culture(string culture, string message)
    {
        foreach (var name in new[] { "", "   ", null })
        {
            var result = Validate(Valid with { DisplayName = name! }, culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(CreateStaffCommand.DisplayName));
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Theory]
    [InlineData("en-GB", "The name can have at most 100 characters.")]
    [InlineData("pl-PL", "Imię i nazwisko może mieć najwyżej 100 znaków.")]
    public void The_name_is_measured_after_trimming(string culture, string message)
    {
        Validate(Valid with { DisplayName = "  " + new string('a', 100) + "  " }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { DisplayName = new string('a', 101) }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("  Anna.Kowalska+salon@example.co.uk ")]
    public void The_email_is_optional_and_a_plain_address_passes(string? email)
    {
        Validate(Valid with { Email = email }, "en-GB").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Enter a valid email address.")]
    [InlineData("pl-PL", "Podaj poprawny adres e-mail.")]
    public void A_malformed_email_fails_with_one_message(string culture, string message)
    {
        foreach (var email in new[] { "anna", "anna@", "@example.com", "Anna <anna@example.com>", "anna@example.com extra" })
        {
            var result = Validate(Valid with { Email = email }, culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(CreateStaffCommand.Email));
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Fact]
    public void An_overlong_email_fails_on_length_only()
    {
        var result = Validate(Valid with { Email = new string('a', 250) + "@example.com" }, "en-GB");

        result.Errors.Select(e => e.ErrorMessage).ShouldContain("The email can have at most 254 characters.");
    }

    [Theory]
    [InlineData("en-GB", "The phone number can have at most 30 characters.")]
    [InlineData("pl-PL", "Numer telefonu może mieć najwyżej 30 znaków.")]
    public void The_phone_is_optional_but_capped(string culture, string message)
    {
        Validate(Valid with { Phone = null }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { Phone = new string('1', 30) }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Phone = new string('1', 31) }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Theory]
    [InlineData("en-GB", "Choose a colour.")]
    [InlineData("pl-PL", "Wybierz kolor.")]
    public void The_colour_must_be_hash_RRGGBB(string culture, string message)
    {
        Validate(Valid with { Colour = "#78716C" }, culture).IsValid.ShouldBeTrue();

        foreach (var colour in new[] { "", "red", "#fff", null })
        {
            var result = Validate(Valid with { Colour = colour! }, culture);

            result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(CreateStaffCommand.Colour));
            result.Errors[0].ErrorMessage.ShouldBe(message);
        }
    }

    [Fact]
    public void Every_failing_rule_is_reported_at_once()
    {
        var result = Validate(new CreateStaffCommand("", "nope", new string('1', 31), "blue", false), "en-GB");

        result.Errors.Select(e => e.PropertyName).Distinct().Order().ShouldBe(["Colour", "DisplayName", "Email", "Phone"]);
    }

    private static ValidationResult Validate(CreateStaffCommand command, string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var requested = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = requested;
        CultureInfo.CurrentUICulture = requested;
        try
        {
            return new CreateStaffValidator(Localizer).Validate(command);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }
}