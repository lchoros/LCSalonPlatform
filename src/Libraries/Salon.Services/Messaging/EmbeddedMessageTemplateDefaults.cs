using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

using Salon.Core.Localization;
using Salon.Core.Messaging;

namespace Salon.Services.Messaging;

/// <summary>
/// The platform's default template texts, embedded as <c>Salon.Services.MessageTemplates.{culture}.json</c>: one object of
/// <c>"Key": { "Sms", "EmailSubject", "EmailBody" }</c> per file (ADR 0008). Loaded and validated once on first use: every
/// supported culture has a file, every file has exactly the catalogue keys with all three non-blank fields, every field
/// uses only tokens the catalogue allows for its key, and each field uses the same token set in every culture. A violation
/// throws <see cref="InvalidOperationException"/> naming culture, key and field, so a packaging mistake fails fast.
/// </summary>
internal sealed class EmbeddedMessageTemplateDefaults
{
    internal const string ResourcePrefix = "Salon.Services.MessageTemplates.";

    private const string Extension = ".json";
    private const string SmsField = "Sms";
    private const string EmailSubjectField = "EmailSubject";
    private const string EmailBodyField = "EmailBody";

    private static readonly string[] Fields = [SmsField, EmailSubjectField, EmailBodyField];

    private readonly Assembly _assembly = typeof(EmbeddedMessageTemplateDefaults).Assembly;
    private readonly Lazy<FrozenDictionary<string, FrozenDictionary<string, DefaultTexts>>> _table;

    public EmbeddedMessageTemplateDefaults()
    {
        _table = new Lazy<FrozenDictionary<string, FrozenDictionary<string, DefaultTexts>>>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The cultures a default file exists for; forcing it validates every file.</summary>
    public IReadOnlyCollection<string> Cultures => _table.Value.Keys;

    /// <summary>The default for (key, channel, culture); false when the culture has no file or the key is not in it.</summary>
    public bool TryGet(string key, MessageChannel channel, string culture, out MessageTemplateText text)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);

        if (_table.Value.TryGetValue(culture, out var templates) && templates.TryGetValue(key, out var texts))
        {
            text = channel switch
            {
                MessageChannel.Sms => new MessageTemplateText(null, texts.Sms, IsTenantOverride: false),
                MessageChannel.Email => new MessageTemplateText(texts.EmailSubject, texts.EmailBody, IsTenantOverride: false),
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown message channel."),
            };
            return true;
        }

        text = null!;
        return false;
    }

    /// <summary>Token names a text uses: rendering with no tokens reports each one as missing, honouring escapes.</summary>
    internal static HashSet<string> TokensOf(string text)
        => MessageTemplateRenderer.Render(text, MessageTokens.Empty).MissingTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private FrozenDictionary<string, FrozenDictionary<string, DefaultTexts>> Load()
    {
        var byCulture = new Dictionary<string, FrozenDictionary<string, DefaultTexts>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in _assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resourceName.EndsWith(Extension, StringComparison.Ordinal))
            {
                continue;
            }

            var culture = resourceName[ResourcePrefix.Length..^Extension.Length];
            if (!SupportedCultures.IsSupported(culture))
            {
                throw Invalid($"resource '{resourceName}' is for culture '{culture}', which is not one of: {string.Join(", ", SupportedCultures.All)}.");
            }

            byCulture[culture] = Read(resourceName, culture);
        }

        foreach (var culture in SupportedCultures.All)
        {
            if (!byCulture.ContainsKey(culture))
            {
                throw Invalid($"no default texts for culture '{culture}'; expected resource '{ResourcePrefix}{culture}{Extension}'.");
            }
        }

        var reference = byCulture[SupportedCultures.Default];
        foreach (var (culture, templates) in byCulture)
        {
            ValidateTemplates(culture, templates, reference);
        }

        return byCulture.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private FrozenDictionary<string, DefaultTexts> Read(string resourceName, string culture)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw Invalid($"resource '{resourceName}' could not be opened.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{nameof(EmbeddedMessageTemplateDefaults)}: culture '{culture}' resource '{resourceName}' is not valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"culture '{culture}' must be a JSON object of key → {{ {string.Join(", ", Fields)} }}.");
            }

            var templates = new Dictionary<string, DefaultTexts>(StringComparer.Ordinal);
            foreach (var template in document.RootElement.EnumerateObject())
            {
                if (!templates.TryAdd(template.Name, ReadTexts(culture, template)))
                {
                    throw Invalid($"culture '{culture}' defines key '{template.Name}' more than once.");
                }
            }

            return templates.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    private static DefaultTexts ReadTexts(string culture, JsonProperty template)
    {
        if (template.Value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"culture '{culture}' key '{template.Name}' must be an object with fields {string.Join(", ", Fields)}.");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in template.Value.EnumerateObject())
        {
            if (!Fields.Contains(field.Name, StringComparer.Ordinal))
            {
                throw Invalid($"culture '{culture}' key '{template.Name}' has unknown field '{field.Name}'; allowed: {string.Join(", ", Fields)}.");
            }

            if (field.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.Value.GetString()))
            {
                throw Invalid($"culture '{culture}' key '{template.Name}' field '{field.Name}' must be a non-empty string.");
            }

            fields[field.Name] = field.Value.GetString()!;
        }

        foreach (var name in Fields)
        {
            if (!fields.ContainsKey(name))
            {
                throw Invalid($"culture '{culture}' key '{template.Name}' is missing field '{name}'.");
            }
        }

        return new DefaultTexts(fields[SmsField], fields[EmailSubjectField], fields[EmailBodyField]);
    }

    private static void ValidateTemplates(string culture, FrozenDictionary<string, DefaultTexts> templates, FrozenDictionary<string, DefaultTexts> reference)
    {
        var expectedKeys = MessageTemplateCatalog.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var missing = expectedKeys.Except(templates.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unknown = templates.Keys.Except(expectedKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || unknown.Count > 0)
        {
            throw Invalid($"culture '{culture}' keys differ from the catalogue; missing: [{string.Join(", ", missing)}], not in catalogue: [{string.Join(", ", unknown)}].");
        }

        foreach (var (key, texts) in templates)
        {
            var definition = MessageTemplateCatalog.Get(key);
            foreach (var (field, text) in texts.ByField())
            {
                var tokens = TokensOf(text);
                var disallowed = tokens.Where(t => !definition.AllowsToken(t)).Order(StringComparer.Ordinal).ToList();
                if (disallowed.Count > 0)
                {
                    throw Invalid($"culture '{culture}' key '{key}' field '{field}' uses tokens the catalogue does not allow: {string.Join(", ", disallowed)}.");
                }

                var referenceTokens = TokensOf(reference[key].ByField().Single(f => f.Field == field).Text);
                if (!tokens.SetEquals(referenceTokens))
                {
                    throw Invalid(
                        $"culture '{culture}' key '{key}' field '{field}' uses tokens [{string.Join(", ", tokens.Order(StringComparer.Ordinal))}] but "
                        + $"'{SupportedCultures.Default}' uses [{string.Join(", ", referenceTokens.Order(StringComparer.Ordinal))}]; every culture must use the same tokens.");
                }
            }
        }
    }

    private static InvalidOperationException Invalid(string detail)
        => new($"{nameof(EmbeddedMessageTemplateDefaults)}: {detail}");

    private sealed record DefaultTexts(string Sms, string EmailSubject, string EmailBody)
    {
        public IEnumerable<(string Field, string Text)> ByField()
        {
            yield return (SmsField, Sms);
            yield return (EmailSubjectField, EmailSubject);
            yield return (EmailBodyField, EmailBody);
        }
    }
}