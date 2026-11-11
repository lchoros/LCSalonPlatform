using System.Text.Json;

using Salon.Core.Localization;
using Salon.Core.Messaging;

namespace Salon.Tests.Architecture;

/// <summary>
/// CLAUDE.md rule 6 applied to message templates (ADR 0008): the embedded platform defaults in Salon.Services exist for
/// both cultures, cover exactly the catalogue's keys, use the same tokens per field in every culture (and only tokens the
/// catalogue allows), and the SMS texts fit one segment with realistic token values.
/// </summary>
public class MessageTemplateDefaultsTests
{
    private const string ResourcePrefix = "Salon.Services.MessageTemplates.";
    private const int SmsSegmentLength = 160;
    private const int SampleTokenLength = 20;

    private static readonly string[] Fields = ["Sms", "EmailSubject", "EmailBody"];

    // Lazy so a packaging error surfaces as its own message, not as a TypeInitializationException.
    private static readonly Lazy<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> Defaults = new(Load);

    [Fact]
    public void Defaults_are_embedded_for_both_supported_cultures()
    {
        Defaults.Value.Keys.Order(StringComparer.Ordinal).ShouldBe(SupportedCultures.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_culture_has_exactly_the_catalogue_keys()
    {
        var expected = MessageTemplateCatalog.All.Select(d => d.Key).Order(StringComparer.Ordinal).ToList();

        foreach (var (culture, templates) in Defaults.Value)
        {
            templates.Keys.Order(StringComparer.Ordinal).ShouldBe(expected, Case.Sensitive, $"{culture} keys differ from the catalogue");
        }
    }

    [Fact]
    public void Every_template_has_all_three_fields_and_none_is_empty()
    {
        foreach (var (culture, templates) in Defaults.Value)
        {
            foreach (var (key, fields) in templates)
            {
                fields.Keys.Order(StringComparer.Ordinal).ShouldBe(Fields.Order(StringComparer.Ordinal), Case.Sensitive, $"{culture}/{key} fields");
                foreach (var (field, text) in fields)
                {
                    text.ShouldNotBeNullOrWhiteSpace($"{culture}/{key}/{field} is empty");
                }
            }
        }
    }

    [Fact]
    public void Every_field_uses_only_tokens_the_catalogue_allows()
    {
        foreach (var (culture, templates) in Defaults.Value)
        {
            foreach (var (key, fields) in templates)
            {
                var definition = MessageTemplateCatalog.Get(key);
                foreach (var (field, text) in fields)
                {
                    var unknown = Tokens(text).Where(t => !definition.AllowsToken(t)).ToList();
                    unknown.ShouldBeEmpty($"{culture}/{key}/{field} uses tokens outside the catalogue: {string.Join(", ", unknown)}");
                }
            }
        }
    }

    [Fact]
    public void Each_field_uses_the_same_token_set_in_every_culture()
    {
        var reference = Defaults.Value[SupportedCultures.Default];

        foreach (var (culture, templates) in Defaults.Value.Where(c => c.Key != SupportedCultures.Default))
        {
            foreach (var (key, fields) in templates)
            {
                foreach (var field in Fields)
                {
                    var expected = Tokens(reference[key][field]);
                    var actual = Tokens(fields[field]);
                    actual.SetEquals(expected).ShouldBeTrue(
                        $"{culture}/{key}/{field} tokens [{string.Join(", ", actual.Order())}] differ from {SupportedCultures.Default} [{string.Join(", ", expected.Order())}]");
                }
            }
        }
    }

    [Fact]
    public void Sms_defaults_fit_one_segment_with_20_character_token_values()
    {
        var sample = new string('x', SampleTokenLength);

        foreach (var (culture, templates) in Defaults.Value)
        {
            foreach (var (key, fields) in templates)
            {
                var tokens = MessageTemplateCatalog.Get(key).TokenNames.Aggregate(MessageTokens.Empty, (set, name) => set.Add(name, sample));
                var rendered = MessageTemplateRenderer.Render(fields["Sms"], tokens);

                rendered.IsComplete.ShouldBeTrue($"{culture}/{key}/Sms: missing {string.Join(", ", rendered.MissingTokens)}");
                rendered.Text.Length.ShouldBeLessThanOrEqualTo(SmsSegmentLength, $"{culture}/{key}/Sms renders to {rendered.Text.Length} characters");
            }
        }
    }

    /// <summary>Token names a text uses: rendering with no tokens reports each one as missing.</summary>
    private static HashSet<string> Tokens(string text)
        => MessageTemplateRenderer.Render(text, MessageTokens.Empty).MissingTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in Assemblies.Services.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var culture = name[ResourcePrefix.Length..^".json".Length];
            using var stream = Assemblies.Services.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);

            var templates = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var template in document.RootElement.EnumerateObject())
            {
                templates[template.Name] = template.Value.EnumerateObject()
                    .ToDictionary(f => f.Name, f => f.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
            }

            result[culture] = templates;
        }

        result.ShouldNotBeEmpty($"no embedded resources named {ResourcePrefix}*.json in Salon.Services");
        return result;
    }
}