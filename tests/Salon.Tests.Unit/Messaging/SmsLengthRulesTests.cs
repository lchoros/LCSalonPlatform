using Salon.Core.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>How many texts a message costs (GSM 03.38): 160 or 153 each in the basic alphabet, 70 or 67 each in Unicode.</summary>
public class SmsLengthRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_costs_nothing(string? text)
    {
        SmsLengthRules.Measure(text).ShouldBe(new SmsLength(0, 0, false));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    [InlineData(459, 3)]
    [InlineData(460, 4)]
    public void A_text_in_the_basic_alphabet_holds_160_characters_or_153_each_when_split(int characters, int segments)
    {
        SmsLengthRules.Measure(new string('a', characters)).ShouldBe(new SmsLength(characters, segments, false));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void One_letter_outside_the_basic_alphabet_makes_the_whole_text_hold_70_or_67_each(int characters, int segments)
    {
        var text = "ą" + new string('a', characters - 1);

        SmsLengthRules.Measure(text).ShouldBe(new SmsLength(characters, segments, true));
    }

    [Fact]
    public void Signs_of_the_extension_table_take_two_places()
    {
        // 159 letters and a euro sign read as 160 characters but need 161 places: two texts.
        SmsLengthRules.Measure(new string('a', 159) + "€").ShouldBe(new SmsLength(160, 2, false));
        SmsLengthRules.Measure(new string('a', 158) + "€").ShouldBe(new SmsLength(159, 1, false));
        SmsLengthRules.Measure("[ok]").ShouldBe(new SmsLength(4, 1, false));
    }

    [Fact]
    public void The_usual_signs_of_an_english_text_are_basic()
    {
        SmsLengthRules.Measure("Hi Olivia, your Cut & blow-dry at Studio 20 is booked for Tue 18 Nov, 10:00. Need to cancel? https://studio-20.example/book/cancel/abc_DEF-123 £45.00 @ #1 (now)!").IsUnicode.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Dzień dobry")]
    [InlineData("Do zobaczenia w środę")]
    [InlineData("See you soon 🙂")]
    [InlineData("“Smart quotes”")]
    public void Polish_letters_emoji_and_typographic_quotes_are_not(string text)
    {
        SmsLengthRules.Measure(text).IsUnicode.ShouldBeTrue();
    }

    [Fact]
    public void An_emoji_counts_as_the_two_units_the_network_sends()
    {
        SmsLengthRules.Measure(new string('a', 68) + "🙂").ShouldBe(new SmsLength(70, 1, true));
        SmsLengthRules.Measure(new string('a', 69) + "🙂").ShouldBe(new SmsLength(71, 2, true));
    }

    [Fact]
    public void A_line_break_from_a_browser_counts_once()
    {
        SmsLengthRules.Measure("one\r\ntwo").ShouldBe(new SmsLength(7, 1, false));
        SmsLengthRules.Measure("one\ntwo").ShouldBe(new SmsLength(7, 1, false));
    }
}