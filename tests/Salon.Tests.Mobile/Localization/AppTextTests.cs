using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Salon.Mobile.Core.Localization;

namespace Salon.Tests.Mobile.Localization;

public partial class AppTextTests
{
    public static TheoryData<string> Languages => new(AppText.Languages);

    [Fact]
    public void There_is_a_dictionary_for_english_and_polish()
    {
        AppText.Languages.ShouldBe([AppText.English, AppText.Polish], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_key_has_words_in_every_language(string language)
    {
        var words = AppText.DictionaryFor(language);

        var missing = Keys().Where(k => !words.TryGetValue(k, out var value) || string.IsNullOrWhiteSpace(value)).ToList();

        missing.ShouldBeEmpty($"{language} lacks: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_dictionary_holds_a_key_the_app_does_not_use(string language)
    {
        var known = Keys().ToHashSet(StringComparer.Ordinal);

        var orphans = AppText.DictionaryFor(language).Keys.Where(k => !known.Contains(k)).ToList();

        orphans.ShouldBeEmpty($"{language} has keys TextKeys does not name: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Both_languages_fill_in_the_same_placeholders()
    {
        var english = AppText.DictionaryFor(AppText.English);
        var polish = AppText.DictionaryFor(AppText.Polish);

        var different = Keys()
            .Where(k => !Placeholders(english[k]).SequenceEqual(Placeholders(polish[k])))
            .ToList();

        different.ShouldBeEmpty($"placeholders differ in: {string.Join(", ", different)}");
    }

    [Fact]
    public void Polish_is_not_english_copied_across()
    {
        var english = AppText.DictionaryFor(AppText.English);
        var polish = AppText.DictionaryFor(AppText.Polish);

        // Words that are the same in both on purpose: pure patterns of numbers and times.
        var copied = Keys()
            .Where(k => english[k] == polish[k] && Regex.Replace(english[k], @"\{\d\}|[\s–:,]|min", string.Empty).Length > 0)
            .ToList();

        copied.ShouldBeEmpty($"the same in both: {string.Join(", ", copied)}");
    }

    [Fact]
    public void It_speaks_english_by_default_and_polish_for_a_polish_salon()
    {
        var text = new AppText();
        text.Language.ShouldBe(AppText.English);
        text[TextKeys.TimeContinue].ShouldBe("Continue");

        text.UseCulture("pl-PL");

        text.Language.ShouldBe(AppText.Polish);
        text.Culture.Name.ShouldBe("pl-PL");
        text[TextKeys.TimeContinue].ShouldBe("Dalej");
    }

    [Theory]
    [InlineData("pl", AppText.Polish, "pl-PL")]
    [InlineData("en-IE", AppText.English, "en-IE")]
    [InlineData("de-DE", AppText.English, "de-DE")]
    [InlineData("xx-not-a-culture", AppText.English, "en-GB")]
    [InlineData("", AppText.English, "en-GB")]
    [InlineData(null, AppText.English, "en-GB")]
    public void Other_cultures_fall_back_to_english_words_and_keep_their_own_numbers(string? culture, string language, string formatting)
    {
        var text = new AppText(culture);

        text.Language.ShouldBe(language);
        text.Culture.Name.ShouldBe(formatting);
    }

    [Fact]
    public void An_unknown_key_shows_itself()
    {
        new AppText()["No.Such.Key"].ShouldBe("No.Such.Key");
    }

    [Fact]
    public void Format_fills_in_with_the_cultures_numbers()
    {
        new AppText("pl-PL").Format(TextKeys.CodeWrongMany, 3).ShouldBe("Ten kod jest nieprawidłowy. Pozostało prób: 3.");
    }

    [Fact]
    public void Changing_the_language_tells_the_bindings()
    {
        var text = new AppText();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)text).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        text.UseCulture("pl-PL");
        text.UseCulture("pl-PL");

        changed.ShouldBe([nameof(AppText.Culture), nameof(AppText.Language), "Item[]"]);
    }

    private static IEnumerable<string> Keys()
    {
        return typeof(TextKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);
    }

    private static List<string> Placeholders(string words)
    {
        return PlaceholderPattern().Matches(words).Select(m => m.Value).Order(StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}