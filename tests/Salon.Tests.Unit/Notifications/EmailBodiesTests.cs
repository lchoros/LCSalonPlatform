using Salon.Services.Notifications;

namespace Salon.Tests.Unit.Notifications;

/// <summary>The HTML part of an email is built from the plain template text (ADR 0025): encoded, line breaks kept, addresses linked.</summary>
public class EmailBodiesTests
{
    [Fact]
    public void Lines_become_breaks_inside_one_paragraph()
    {
        EmailBodies.ToHtml("Hi Olivia,\nsee you soon.").ShouldBe("<p>Hi Olivia,<br>\nsee you soon.</p>");
        EmailBodies.ToHtml("One\r\nTwo").ShouldBe("<p>One<br>\nTwo</p>");
        EmailBodies.ToHtml(string.Empty).ShouldBe("<p></p>");
    }

    [Fact]
    public void A_web_address_becomes_a_link_without_the_full_stop_that_ends_the_sentence()
    {
        EmailBodies.ToHtml("Cancel here: https://studio-20.example/book/cancel/abc123. Thanks")
            .ShouldBe("<p>Cancel here: <a href=\"https://studio-20.example/book/cancel/abc123\">https://studio-20.example/book/cancel/abc123</a>. Thanks</p>");
        EmailBodies.ToHtml("http://a.example/x?y=1&z=2")
            .ShouldBe("<p><a href=\"http://a.example/x?y=1&amp;z=2\">http://a.example/x?y=1&amp;z=2</a></p>");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    [InlineData("Tom & \"Anna\"", "<p>Tom &amp; &quot;Anna&quot;</p>")]
    [InlineData("see <https://a.example/x>", "<p>see &lt;<a href=\"https://a.example/x\">https://a.example/x</a>&gt;</p>")]
    [InlineData("https://a.example/\"onmouseover=\"x", "<p><a href=\"https://a.example/\">https://a.example/</a>&quot;onmouseover=&quot;x</p>")]
    public void Nothing_in_a_template_or_a_token_becomes_markup(string text, string expected)
    {
        EmailBodies.ToHtml(text).ShouldBe(expected);
    }
}