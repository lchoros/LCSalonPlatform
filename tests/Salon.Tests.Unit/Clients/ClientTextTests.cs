using Salon.Core.Clients;
using Salon.Services.Clients;

namespace Salon.Tests.Unit.Clients;

/// <summary>Name and email rules shared by the form validator and the client service (ADR 0011).</summary>
public class ClientTextTests
{
    [Theory]
    [InlineData("  Olivia ", "Olivia")]
    [InlineData("Anne-Marie", "Anne-Marie")]
    [InlineData("Zażółć", "Zażółć")]
    public void A_name_is_trimmed(string raw, string expected)
    {
        ClientName.TryNormalise(raw, out var name).ShouldBeTrue();
        name.ShouldBe(expected);
    }

    [Fact]
    public void A_name_may_fill_the_column_but_not_more()
    {
        ClientName.TryNormalise(" " + new string('a', Client.NameMaxLength) + " ", out _).ShouldBeTrue();
        ClientName.TryNormalise(new string('a', Client.NameMaxLength + 1), out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_name_is_refused(string? raw)
    {
        ClientName.TryNormalise(raw, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Theory]
    [InlineData("  Olivia@Example.COM ", "olivia@example.com")]
    [InlineData("a@b.c", "a@b.c")]
    public void An_email_is_trimmed_and_lower_cased(string raw, string expected)
    {
        EmailAddress.TryNormalise(raw, out var email).ShouldBeTrue();
        email.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_email_is_fine_and_means_none(string? raw)
    {
        EmailAddress.TryNormalise(raw, out var email).ShouldBeTrue();
        email.ShouldBeNull();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("olivia@")]
    [InlineData("oli via@example.com")]
    [InlineData("two@@example.com")]
    public void A_malformed_email_is_refused(string raw)
    {
        EmailAddress.TryNormalise(raw, out var email).ShouldBeFalse();
        email.ShouldBeNull();
    }

    [Fact]
    public void An_email_longer_than_the_column_is_refused()
    {
        var local = new string('a', Client.EmailMaxLength - "@example.com".Length);
        EmailAddress.TryNormalise(local + "@example.com", out _).ShouldBeTrue();
        EmailAddress.TryNormalise(local + "a@example.com", out _).ShouldBeFalse();
    }
}