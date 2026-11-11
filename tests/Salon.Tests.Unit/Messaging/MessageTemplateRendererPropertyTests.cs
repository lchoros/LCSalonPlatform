using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Salon.Core.Messaging;

namespace Salon.Tests.Unit.Messaging;

/// <summary>Properties of the token renderer (ADR 0008); drafted by the local model, reshaped to the FsCheck 3 API.</summary>
public class MessageTemplateRendererPropertyTests
{
    private static readonly Gen<string> TokenNameGen = Gen.Elements("A", "Client.FirstName", "salon_name", "X1");

    private static readonly Gen<string> TokenValueGen = Gen.Choose(0, 10)
        .SelectMany(length => Gen.ArrayOf(Gen.Elements('a', 'b', 'c', ' ', '-', '{', '}'), length))
        .Select(chars => new string(chars));

    private static readonly Gen<string[]> TokenNamesGen = Gen.Choose(1, 5).SelectMany(length => Gen.ArrayOf(TokenNameGen, length));

    [Property]
    public Property A_template_without_a_placeholder_opening_renders_to_itself()
    {
        var templates = TokenValueGen.Where(text => !text.Contains("{{", StringComparison.Ordinal)).ToArbitrary();

        return Prop.ForAll(templates, template =>
        {
            var result = MessageTemplateRenderer.Render(template, MessageTokens.Empty);
            return result.Text == template && result.IsComplete;
        });
    }

    [Property]
    public Property Known_tokens_render_to_their_values_in_order()
    {
        var pairs = TokenNamesGen
            .SelectMany(names => Gen.ArrayOf(TokenValueGen, names.Length).Select(values => (Names: names, Values: values)))
            .ToArbitrary();

        return Prop.ForAll(pairs, pair =>
        {
            var tokens = MessageTokens.Empty;
            for (var i = 0; i < pair.Names.Length; i++)
            {
                tokens = tokens.Add(pair.Names[i], pair.Values[i]);
            }

            // A repeated name keeps its last value, so build the expectation from the final token set.
            var expected = string.Join("|", pair.Names.Select(name => tokens.TryGet(name, out var value) ? value : string.Empty));
            var template = string.Join("|", pair.Names.Select(name => "{{" + name + "}}"));

            var result = MessageTemplateRenderer.Render(template, tokens);
            return result.Text == expected && result.IsComplete;
        });
    }

    [Property]
    public Property Rendering_with_no_tokens_reports_each_distinct_name_once_in_order()
    {
        return Prop.ForAll(TokenNamesGen.ToArbitrary(), names =>
        {
            var template = string.Join("|", names.Select(name => "{{" + name + "}}"));
            var expectedMissing = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var result = MessageTemplateRenderer.Render(template, MessageTokens.Empty);
            return result.MissingTokens.SequenceEqual(expectedMissing) && !result.IsComplete;
        });
    }

    [Property]
    public Property Rendering_is_deterministic()
    {
        var inputs = Gen.Zip(TokenNameGen, TokenValueGen).ToArbitrary();

        return Prop.ForAll(inputs, input =>
        {
            var tokens = MessageTokens.Empty.Add(input.Item1, input.Item2);
            var template = "Hello, {{" + input.Item1 + "}}!";

            var first = MessageTemplateRenderer.Render(template, tokens);
            var second = MessageTemplateRenderer.Render(template, tokens);
            return first.Text == second.Text && first.MissingTokens.SequenceEqual(second.MissingTokens);
        });
    }

    [Property]
    public Property An_escaped_placeholder_renders_literally_and_is_not_missing()
    {
        return Prop.ForAll(TokenNameGen.ToArbitrary(), name =>
        {
            var result = MessageTemplateRenderer.Render("\\{{" + name + "}}", MessageTokens.Empty);
            return result.Text == "{{" + name + "}}" && result.IsComplete;
        });
    }
}