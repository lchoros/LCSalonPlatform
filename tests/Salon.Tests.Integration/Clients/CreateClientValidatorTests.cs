using System.Globalization;

using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Clients;
using Salon.Services;
using Salon.Services.Localization;
using Salon.Web.Features.Clients.CreateClient;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// Pure rules check (no database, no host) against the host's real embedded resources, so a renamed key or a missing
/// Polish message fails here. The phone rule needs the tenant's country and lives in its own validator.
/// </summary>
public class CreateClientValidatorTests
{
    private static readonly ILocalizationService Localizer = new ServiceCollection()
        .AddLogging()
        .AddSalonServices()
        .AddLocalizationResources(typeof(Program).Assembly)
        .BuildServiceProvider()
        .GetRequiredService<ILocalizationService>();

    private static readonly CreateClientCommand Valid = new("Olivia", "Brown", "07700 900123", "olivia@example.com", "Prefers afternoons.", "Allergic to PPD", "2026-10-01", true);

    [Fact]
    public void A_valid_command_passes()
    {
        Validate(Valid, "en-GB").IsValid.ShouldBeTrue();
        Validate(new CreateClientCommand("Anna", "Nowak", null, null, null, null, null, false), "pl-PL").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Enter the first name.", "Enter the last name.")]
    [InlineData("pl-PL", "Podaj imię.", "Podaj nazwisko.")]
    public void Both_names_are_required_in_the_request_culture(string culture, string firstMessage, string lastMessage)
    {
        foreach (var blank in new[] { "", "   ", null })
        {
            var result = Validate(Valid with { FirstName = blank!, LastName = blank! }, culture);

            result.Errors.Select(e => e.ErrorMessage).ShouldBe([firstMessage, lastMessage]);
        }
    }

    [Fact]
    public void Names_are_measured_after_trimming()
    {
        Validate(Valid with { FirstName = "  " + new string('a', Client.NameMaxLength) + "  " }, "en-GB").IsValid.ShouldBeTrue();

        var result = Validate(Valid with { LastName = new string('a', Client.NameMaxLength + 1) }, "en-GB");

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("The last name can have at most 100 characters.");
    }

    [Theory]
    [InlineData("en-GB", "Enter a valid email address.")]
    [InlineData("pl-PL", "Podaj prawidłowy adres e-mail.")]
    public void Email_is_optional_but_must_be_an_address(string culture, string message)
    {
        Validate(Valid with { Email = null }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { Email = " " }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Email = "not-an-email" }, culture);

        result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
    }

    [Fact]
    public void The_phone_is_not_judged_here_because_it_needs_the_salon_country()
    {
        Validate(Valid with { Phone = "call me" }, "en-GB").IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en-GB", "Notes can have at most 4000 characters.", "Health notes can have at most 2000 characters.")]
    [InlineData("pl-PL", "Notatki mogą mieć najwyżej 4000 znaków.", "Uwagi zdrowotne mogą mieć najwyżej 2000 znaków.")]
    public void Notes_and_health_notes_are_capped(string culture, string notesMessage, string healthMessage)
    {
        Validate(Valid with { Notes = new string('n', Client.NotesMaxLength), HealthNotes = new string('h', Client.HealthNotesMaxLength) }, culture).IsValid.ShouldBeTrue();

        var result = Validate(Valid with { Notes = new string('n', Client.NotesMaxLength + 1), HealthNotes = new string('h', Client.HealthNotesMaxLength + 1) }, culture);

        result.Errors.Select(e => e.ErrorMessage).ShouldBe([notesMessage, healthMessage]);
    }

    [Theory]
    [InlineData("en-GB", "Enter the patch test date as YYYY-MM-DD.")]
    [InlineData("pl-PL", "Podaj datę testu uczuleniowego w formacie RRRR-MM-DD.")]
    public void The_patch_test_date_is_optional_but_must_be_an_ISO_date(string culture, string message)
    {
        Validate(Valid with { LastPatchTestOn = null }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { LastPatchTestOn = "" }, culture).IsValid.ShouldBeTrue();
        Validate(Valid with { LastPatchTestOn = "2026-10-01" }, culture).IsValid.ShouldBeTrue();

        foreach (var bad in new[] { "01/10/2026", "2026-13-01", "yesterday" })
        {
            Validate(Valid with { LastPatchTestOn = bad }, culture).Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(message);
        }
    }

    [Fact]
    public void Every_failing_rule_is_reported_at_once()
    {
        var result = Validate(new CreateClientCommand("", "", null, "nope", new string('n', 4001), new string('h', 2001), "bad", false), "en-GB");

        result.Errors.Select(e => e.PropertyName).Distinct().Order().ShouldBe(["Email", "FirstName", "HealthNotes", "LastName", "LastPatchTestOn", "Notes"]);
    }

    [Fact]
    public void The_command_never_prints_contact_details_or_health_notes()
    {
        var text = Valid.ToString();

        text.ShouldContain("Olivia");
        text.ShouldNotContain("7700");
        text.ShouldNotContain("example.com");
        text.ShouldNotContain("PPD");
    }

    private static ValidationResult Validate(CreateClientCommand command, string culture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var requested = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = requested;
        CultureInfo.CurrentUICulture = requested;
        try
        {
            return new CreateClientValidator(Localizer).Validate(command);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }
}