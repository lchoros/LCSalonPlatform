using Salon.Core.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>The catalogue (ADR 0008) is the single list of template keys, their channels and the tokens they may use.</summary>
public class MessageTemplateCatalogTests
{
    private static readonly string[] BookingTokens = ["SalonName", "ClientFirstName", "ServiceName", "StaffName", "AppointmentStart"];

    [Fact]
    public void Lists_exactly_the_four_booking_templates_and_the_code()
    {
        MessageTemplateCatalog.All.Select(d => d.Key).Order(StringComparer.Ordinal)
            .ShouldBe(["Booking.Cancelled", "Booking.Code", "Booking.Confirmation", "Booking.Reminder", "Booking.Rescheduled"]);
    }

    [Fact]
    public void The_code_template_is_sms_only_with_the_salon_name_and_the_code()
    {
        var definition = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingCode);

        definition.Channels.ShouldBe([MessageChannel.Sms]);
        definition.TokenNames.Order(StringComparer.Ordinal).ShouldBe(["Code", "SalonName"]);
    }

    [Theory]
    [InlineData("Booking.Confirmation")]
    [InlineData("Booking.Reminder")]
    [InlineData("Booking.Cancelled")]
    [InlineData("Booking.Rescheduled")]
    public void Every_booking_template_goes_out_by_sms_and_email(string key)
    {
        MessageTemplateCatalog.Get(key).Channels.Order().ShouldBe([MessageChannel.Sms, MessageChannel.Email]);
    }

    [Theory]
    [InlineData("Booking.Confirmation", "CancelUrl")]
    [InlineData("Booking.Reminder", "CancelUrl")]
    [InlineData("Booking.Cancelled", "BookingUrl")]
    [InlineData("Booking.Rescheduled", "CancelUrl")]
    public void Token_names_are_the_shared_booking_tokens_plus_one_link(string key, string link)
    {
        var expected = BookingTokens.Append(link).Order(StringComparer.Ordinal);

        MessageTemplateCatalog.Get(key).TokenNames.Order(StringComparer.Ordinal).ShouldBe(expected);
    }

    [Fact]
    public void Every_token_name_is_valid_for_the_renderer()
    {
        foreach (var definition in MessageTemplateCatalog.All)
        {
            definition.TokenNames.ShouldAllBe(name => MessageTokens.IsValidName(name), definition.Key);
        }
    }

    [Fact]
    public void Keys_are_unique_and_at_most_100_characters()
    {
        var keys = MessageTemplateCatalog.All.Select(d => d.Key).ToList();

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Count);
        keys.ShouldAllBe(k => k.Length <= 100);
    }

    [Fact]
    public void Get_returns_the_definition_for_a_known_key()
    {
        var definition = MessageTemplateCatalog.Get("Booking.Reminder");

        definition.Key.ShouldBe("Booking.Reminder");
        definition.Supports(MessageChannel.Email).ShouldBeTrue();
        definition.AllowsToken("clientfirstname").ShouldBeTrue("token names are case-insensitive like the renderer");
        definition.AllowsToken("BookingUrl").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Booking.Unknown")]
    [InlineData("booking.confirmation")]
    [InlineData("")]
    public void Get_throws_for_a_key_that_is_not_in_the_catalogue(string key)
    {
        var ex = Should.Throw<MessageTemplateNotFoundException>(() => MessageTemplateCatalog.Get(key));

        ex.Key.ShouldBe(key);
        ex.Message.ShouldContain(key);
    }

    [Fact]
    public void TryGet_reports_unknown_keys_without_throwing()
    {
        MessageTemplateCatalog.TryGet("Booking.Confirmation", out var found).ShouldBeTrue();
        found.ShouldNotBeNull();
        MessageTemplateCatalog.TryGet("Nope", out var missing).ShouldBeFalse();
        missing.ShouldBeNull();
    }
}