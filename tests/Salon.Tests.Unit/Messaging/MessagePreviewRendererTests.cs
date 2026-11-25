using Salon.Core.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>
/// The preview of a message in pieces (ADR 0098): a tag the message offers is filled with its example value, a tag it
/// does not offer is marked as unknown exactly when saving would refuse it, and the words in between stay as typed.
/// </summary>
public class MessagePreviewRendererTests
{
    private static readonly MessageTokens Samples = MessageTokens.Empty
        .Add("SalonName", "Studio 20")
        .Add("ClientFirstName", "Olivia")
        .Add("ServiceName", "Cut and blow-dry")
        .Add("CancelUrl", "https://studio-20.example/v/abc")
        .Add("BookingUrl", "https://studio-20.example/book")
        .Add("Code", "111111");

    private static MessageTemplateDefinition Confirmation => MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingConfirmation);

    [Fact]
    public void Known_tags_are_filled_with_their_example_and_the_words_stay_as_typed()
    {
        var preview = MessagePreviewRenderer.Render(Confirmation, "Hi {{ClientFirstName}}, see you at {{SalonName}}.", Samples);

        preview.Parts.ShouldBe(new[]
        {
            new PreviewPart(PreviewPartKind.Text, "Hi "),
            new PreviewPart(PreviewPartKind.Value, "Olivia"),
            new PreviewPart(PreviewPartKind.Text, ", see you at "),
            new PreviewPart(PreviewPartKind.Value, "Studio 20"),
            new PreviewPart(PreviewPartKind.Text, "."),
        });
        preview.Sent.ShouldBe("Hi Olivia, see you at Studio 20.");
        preview.HasUnknown.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{{Price}}")]
    [InlineData("{{ClientFristName}}")]
    [InlineData("{{Code}}")]
    [InlineData("{{BookingUrl}}")]
    public void A_tag_the_message_does_not_offer_is_marked_unknown_and_sent_empty(string tag)
    {
        // A misspelt name, or one that belongs to another message: both are what saving refuses.
        var preview = MessagePreviewRenderer.Render(Confirmation, "Hi " + tag + "!", Samples);

        preview.Parts.ShouldBe(new[]
        {
            new PreviewPart(PreviewPartKind.Text, "Hi "),
            new PreviewPart(PreviewPartKind.Unknown, tag),
            new PreviewPart(PreviewPartKind.Text, "!"),
        });
        preview.Sent.ShouldBe("Hi !");
        preview.HasUnknown.ShouldBeTrue();
        MessageTemplateRules.NotAllowed(Confirmation, "Hi " + tag + "!").ShouldNotBeEmpty("the preview marks exactly what saving refuses");
    }

    [Fact]
    public void What_the_preview_marks_is_what_saving_refuses_for_every_message()
    {
        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var tag in new[] { "SalonName", "ClientFirstName", "ServiceName", "StaffName", "AppointmentStart", "CancelUrl", "BookingUrl", "Code", "HeldUntil", "Price" })
            {
                var text = "x {{" + tag + "}} y";
                MessagePreviewRenderer.Render(definition, text, Samples).HasUnknown
                    .ShouldBe(MessageTemplateRules.NotAllowed(definition, text).Count > 0, $"{definition.Key} {tag}");
            }
        }
    }

    [Fact]
    public void Names_are_matched_like_the_renderer_does_ignoring_case_and_inner_spaces()
    {
        var preview = MessagePreviewRenderer.Render(Confirmation, "{{ clientfirstname }}", Samples);

        preview.Parts.ShouldBe(new[] { new PreviewPart(PreviewPartKind.Value, "Olivia") });
    }

    [Fact]
    public void An_escaped_tag_and_broken_brackets_are_words()
    {
        var preview = MessagePreviewRenderer.Render(Confirmation, @"Type \{{ClientFirstName}} or {{Client Name}} or {ClientFirstName}", Samples);

        preview.Parts.ShouldBe(new[] { new PreviewPart(PreviewPartKind.Text, "Type {{ClientFirstName}} or {{Client Name}} or {ClientFirstName}") });
        preview.HasUnknown.ShouldBeFalse("the renderer does not treat them as tags and saving does not refuse them");
    }

    [Fact]
    public void A_known_tag_without_an_example_comes_out_empty()
    {
        var preview = MessagePreviewRenderer.Render(Confirmation, "With {{StaffName}}", Samples);

        preview.Parts.ShouldBe(new[] { new PreviewPart(PreviewPartKind.Text, "With "), new PreviewPart(PreviewPartKind.Value, string.Empty) });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_previews_as_nothing(string? template)
    {
        var preview = MessagePreviewRenderer.Render(Confirmation, template, Samples);

        preview.Parts.ShouldBeEmpty();
        preview.Sent.ShouldBe(string.Empty);
    }

    [Fact]
    public void The_text_count_is_taken_after_the_examples_are_filled_in()
    {
        // 153 letters and a tag read as 161 characters typed, but 159 once the six-digit example is in: one text.
        var template = new string('a', 153) + "{{Code}}";
        var code = MessageTemplateCatalog.Get(MessageTemplateCatalog.BookingCode);

        var preview = MessagePreviewRenderer.Render(code, template, Samples);

        SmsLengthRules.Measure(preview.Sent).ShouldBe(new SmsLength(159, 1, false));
        SmsLengthRules.Measure(template).Segments.ShouldBe(2, "the raw tag would have said two");
    }

    [Fact]
    public void The_pattern_handed_to_the_page_is_the_one_the_renderer_uses()
    {
        // The page gives its script this pattern; it must keep the two group names the script reads.
        MessageTemplateRenderer.PlaceholderPattern.ShouldContain("(?<escape>");
        MessageTemplateRenderer.PlaceholderPattern.ShouldContain("(?<name>");
        System.Text.RegularExpressions.Regex.Count(@"{{A}} \{{B}} {{ C.d_1 }}", MessageTemplateRenderer.PlaceholderPattern).ShouldBe(3);
    }
}