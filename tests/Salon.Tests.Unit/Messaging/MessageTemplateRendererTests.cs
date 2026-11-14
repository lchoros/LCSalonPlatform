using System;
using System.Collections.Generic;
using Salon.Core.Messaging;
using Shouldly;
using Xunit;

namespace Salon.Tests.Unit.Messaging;

public class MessageTemplateRendererTests
{
    [Fact]
    public void SimpleReplacement()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John");
        var result = MessageTemplateRenderer.Render("Hello, {{Name}}!", tokens);
        result.Text.ShouldBe("Hello, John!");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void TwoTokens()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John").Add("Age", "30");
        var result = MessageTemplateRenderer.Render("Hello, {{Name}}! You are {{Age}} years old.", tokens);
        result.Text.ShouldBe("Hello, John! You are 30 years old.");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void SpacesInsideBraces()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John");
        var result = MessageTemplateRenderer.Render("Hello, {{ Name }}!", tokens);
        result.Text.ShouldBe("Hello, John!");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void CaseInsensitiveName()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John");
        var result = MessageTemplateRenderer.Render("Hello, {{name}}!", tokens);
        result.Text.ShouldBe("Hello, John!");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void UnknownToken()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John");
        var result = MessageTemplateRenderer.Render("Hello, {{Name}}! You are {{Age}} years old.", tokens);
        result.Text.ShouldBe("Hello, John! You are  years old.");
        result.MissingTokens.ShouldBe(new[] { "Age" });
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void MixedKnownAndUnknown()
    {
        var tokens = MessageTokens.Empty.Add("Name", "John");
        var result = MessageTemplateRenderer.Render("Hello, {{Name}}! You are {{Age}} years old. {{Name}}.", tokens);
        result.Text.ShouldBe("Hello, John! You are  years old. John.");
        result.MissingTokens.ShouldBe(new[] { "Age" });
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void EscapeRendersLiterally()
    {
        var result = MessageTemplateRenderer.Render("Hello, \\{{Name}}!", MessageTokens.Empty);
        result.Text.ShouldBe("Hello, {{Name}}!");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void MalformedPlaceholdersUntouched()
    {
        var result = MessageTemplateRenderer.Render("{{ {{ }} {{1x}} {{a b}} }}", MessageTokens.Empty);
        result.Text.ShouldBe("{{ {{ }} {{1x}} {{a b}} }}");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void ValueContainingPlaceholderNotReExpanded()
    {
        var tokens = MessageTokens.Empty.Add("Name", "{{Other}}");
        var result = MessageTemplateRenderer.Render("Hello, {{Name}}!", tokens);
        result.Text.ShouldBe("Hello, {{Other}}!");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void EmptyTemplate()
    {
        var result = MessageTemplateRenderer.Render(string.Empty, MessageTokens.Empty);
        result.Text.ShouldBe(string.Empty);
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Should.Throw<ArgumentNullException>(() => MessageTemplateRenderer.Render(null!, MessageTokens.Empty));
        Should.Throw<ArgumentNullException>(() => MessageTemplateRenderer.Render("Hello, {{Name}}!", null!));
    }

    [Fact]
    public void MessageTokensAddReplacesInPlace()
    {
        var tokens1 = MessageTokens.Empty.Add("Name", "John");
        var tokens2 = tokens1.Add("Name", "Jane");
        tokens1.TryGet("Name", out var value1).ShouldBeTrue();
        value1.ShouldBe("John");
        tokens2.TryGet("Name", out var value2).ShouldBeTrue();
        value2.ShouldBe("Jane");
    }

    [Fact]
    public void IsValidName()
    {
        MessageTokens.IsValidName("A").ShouldBeTrue();
        MessageTokens.IsValidName("Client.FirstName").ShouldBeTrue();
        MessageTokens.IsValidName("x_1").ShouldBeTrue();
        MessageTokens.IsValidName("1a").ShouldBeFalse();
        MessageTokens.IsValidName("a b").ShouldBeFalse();
        MessageTokens.IsValidName("a-b").ShouldBeFalse();
        MessageTokens.IsValidName(string.Empty).ShouldBeFalse();
        MessageTokens.IsValidName(null).ShouldBeFalse();
        MessageTokens.IsValidName(new string('a', 65)).ShouldBeFalse();
    }
}