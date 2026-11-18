using NodaTime;
using Salon.Core.Settings;

namespace Salon.Tests.Unit.Settings;

/// <summary>
/// The privacy notice (ADR 0063): a salon's own text must say something and fit; it is shown as the paragraphs the
/// owner wrote; "last updated" is the day the owner saved, or the day the standard text was last changed.
/// </summary>
public class PrivacyNoticeRulesTests
{
    [Fact]
    public void A_salons_own_text_says_something_and_fits()
    {
        PrivacyNoticeRules.IsOwnText("We keep your name.").ShouldBeTrue();
        PrivacyNoticeRules.IsOwnText(new string('a', PrivacyNoticeRules.OwnTextMaxLength)).ShouldBeTrue();
        PrivacyNoticeRules.IsOwnText(new string('a', PrivacyNoticeRules.OwnTextMaxLength + 1)).ShouldBeFalse();
        PrivacyNoticeRules.IsOwnText("   \n ").ShouldBeFalse();
        PrivacyNoticeRules.IsOwnText(null).ShouldBeFalse();
    }

    [Fact]
    public void A_blank_line_ends_a_paragraph_and_a_single_line_break_stays_inside_it()
    {
        var text = "  Who we are.\nStudio 20, Rochdale.\r\n\r\n\r\nWhat we keep.\n\n   \n\nYour rights.  \n";

        PrivacyNoticeRules.Paragraphs(text).ShouldBe(["Who we are.\nStudio 20, Rochdale.", "What we keep.", "Your rights."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n \n ")]
    public void No_text_is_no_paragraphs(string? text)
    {
        PrivacyNoticeRules.Paragraphs(text).ShouldBeEmpty();
    }

    [Fact]
    public void Last_updated_is_the_day_the_owner_saved_or_the_day_of_the_standard_text()
    {
        PrivacyNoticeRules.UpdatedOn(new PrivacyNoticeSettings()).ShouldBe(PrivacyNoticeRules.StandardTextDate);
        PrivacyNoticeRules.UpdatedOn(new PrivacyNoticeSettings { UpdatedOn = "2027-02-03" }).ShouldBe(new LocalDate(2027, 2, 3));
        PrivacyNoticeRules.UpdatedOn(new PrivacyNoticeSettings { UpdatedOn = "last week" }).ShouldBe(PrivacyNoticeRules.StandardTextDate);
    }

    [Fact]
    public void A_salon_shows_the_standard_notice_until_the_owner_says_otherwise()
    {
        var settings = new PrivacyNoticeSettings();

        (settings.UseOwnText, settings.OwnText, settings.UpdatedOn).ShouldBe((false, null, null));
    }
}