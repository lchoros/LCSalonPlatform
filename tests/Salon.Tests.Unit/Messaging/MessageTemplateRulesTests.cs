using Salon.Core.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>What a salon's own wording of a message must respect (ADR 0030).</summary>
public class MessageTemplateRulesTests
{
    private static readonly MessageTemplateDefinition Confirmation = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingConfirmation);
    private static readonly MessageTemplateDefinition Code = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingCode);

    [Fact]
    public void Placeholders_are_listed_once_each_in_the_order_they_first_appear()
    {
        MessageTemplateRules.Placeholders("Hi {{ClientFirstName}}, {{SalonName}} here. See you, {{ clientfirstname }}! {{CancelUrl}}")
            .ShouldBe(["ClientFirstName", "SalonName", "CancelUrl"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No placeholders here, only {single} braces and {{ }}.")]
    [InlineData(@"An escaped \{{SalonName}} is text.")]
    public void Text_without_a_real_placeholder_has_none(string? text)
    {
        MessageTemplateRules.Placeholders(text).ShouldBeEmpty();
    }

    [Fact]
    public void A_message_may_only_use_the_placeholders_its_catalogue_entry_lists()
    {
        MessageTemplateRules.NotAllowed(Confirmation, "Hi {{ClientFirstName}}, see {{CancelUrl}}").ShouldBeEmpty();
        MessageTemplateRules.NotAllowed(Confirmation, "{{clientfirstname}}").ShouldBeEmpty("names are matched without regard to case, like the renderer");
        MessageTemplateRules.NotAllowed(Confirmation, "It costs {{Price}}, code {{Code}}, {{SalonName}}").ShouldBe(["Price", "Code"]);
        MessageTemplateRules.NotAllowed(Code, "{{CancelUrl}}").ShouldBe(["CancelUrl"]);
    }

    [Fact]
    public void A_verification_text_must_keep_the_code()
    {
        Code.RequiredTokenNames.ShouldBe(["Code"]);
        MessageTemplateRules.MissingRequired(Code, "Welcome to {{SalonName}}!").ShouldBe(["Code"]);
        MessageTemplateRules.MissingRequired(Code, "{{SalonName}}: {{ code }}").ShouldBeEmpty();
        MessageTemplateRules.MissingRequired(Code, @"\{{Code}}").ShouldBe(["Code"], "an escaped placeholder is not the code");
    }

    [Fact]
    public void Other_messages_have_nothing_they_must_keep()
    {
        MessageTemplateCatalog.All.Where(d => d.Key != MessageTemplateCatalog.BookingCode).ShouldAllBe(d => d.RequiredTokenNames.Count == 0);
        MessageTemplateRules.MissingRequired(Confirmation, "See you soon.").ShouldBeEmpty();
    }

    [Fact]
    public void Every_required_placeholder_is_one_the_message_offers()
    {
        MessageTemplateCatalog.All.ShouldAllBe(d => d.RequiredTokenNames.All(d.AllowsToken));
    }

    [Fact]
    public void A_text_is_shorter_than_an_email()
    {
        MessageTemplateRules.BodyMaxLength(MessageChannel.Sms).ShouldBe(480);
        MessageTemplateRules.BodyMaxLength(MessageChannel.Email).ShouldBe(4000);
    }
}