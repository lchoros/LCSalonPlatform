using System.Globalization;
using Microsoft.Extensions.Logging;
using Salon.Services.Localization;
using Salon.Tests.Unit.Infrastructure;

namespace Salon.Tests.Unit.Localization;

public class LocalizationServiceTests
{
    private static readonly InMemoryResourceSource Host = InMemoryResourceSource.Of(
        "host",
        ("en-GB", "Home.Title", "Home"),
        ("en-GB", "Home.Welcome", "Welcome, {0}!"),
        ("en-GB", "Home.EnglishOnly", "Only in English"),
        ("pl-PL", "Home.Title", "Start"),
        ("pl-PL", "Home.Welcome", "Witaj, {0}!"));

    private readonly ListLogger<LocalizationService> _logger = new();

    [Fact]
    public void Get_returns_the_value_for_the_current_UI_culture()
    {
        var service = CreateService(Host);

        WithUICulture("pl-PL", () => service.Get("Home.Title").ShouldBe("Start"));
        WithUICulture("en-GB", () => service.Get("Home.Title").ShouldBe("Home"));
    }

    [Fact]
    public void A_neutral_current_UI_culture_reads_the_matching_supported_culture()
    {
        var service = CreateService(Host);

        WithUICulture("pl", () => service.Get("Home.Title").ShouldBe("Start"));

        _logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Culture_names_are_matched_ignoring_case()
    {
        var service = CreateService(Host);

        service.Get("Home.Title", "PL-pl").ShouldBe("Start");
    }

    [Fact]
    public void Cultures_lists_the_loaded_culture_names()
    {
        var service = CreateService(Host);

        service.Cultures.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(["en-GB", "pl-PL"]);
    }

    [Fact]
    public void A_key_missing_in_Polish_falls_back_to_English_and_warns()
    {
        var service = CreateService(Host);

        var value = service.Get("Home.EnglishOnly", "pl-PL");

        value.ShouldBe("Only in English");
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Home.EnglishOnly") && e.Message.Contains("pl-PL"));
    }

    [Fact]
    public void A_key_missing_everywhere_returns_the_key_and_warns_exactly_once()
    {
        var service = CreateService(Host);

        WithUICulture("en-GB", () =>
        {
            service.Get("Nope.Missing").ShouldBe("Nope.Missing");
            service.Get("Nope.Missing").ShouldBe("Nope.Missing");
            service.Get("Nope.Missing", "en-GB").ShouldBe("Nope.Missing");
        });

        _logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Nope.Missing")).ShouldBe(1);
    }

    [Fact]
    public void Each_missing_culture_and_key_pair_warns_separately_but_only_once()
    {
        var service = CreateService(Host);

        service.Get("Nope.Missing", "en-GB");
        service.Get("Nope.Missing", "pl-PL");
        service.Get("Nope.Missing", "pl-PL");
        service.Get("Nope.Other", "pl-PL");

        _logger.Entries.Count(e => e.Level == LogLevel.Warning).ShouldBe(3);
    }

    [Fact]
    public void An_unsupported_current_culture_reads_the_default_without_warning()
    {
        var service = CreateService(Host);

        WithUICulture("de-DE", () => service.Get("Home.Title").ShouldBe("Home"));

        _logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Exists_reports_only_the_exact_culture()
    {
        var service = CreateService(Host);

        service.Exists("Home.EnglishOnly", "en-GB").ShouldBeTrue();
        service.Exists("Home.EnglishOnly", "pl-PL").ShouldBeFalse();
        service.Exists("Nope.Missing", "en-GB").ShouldBeFalse();
    }

    [Fact]
    public void Format_substitutes_arguments_in_the_current_culture()
    {
        var service = CreateService(Host);

        WithUICulture("pl-PL", () => service.Format("Home.Welcome", "Anna").ShouldBe("Witaj, Anna!"));
        WithUICulture("en-GB", () => service.Format("Home.Welcome", "Anna").ShouldBe("Welcome, Anna!"));
    }

    [Fact]
    public void Indexer_equals_Get()
    {
        var service = CreateService(Host);

        WithUICulture("pl-PL", () => service["Home.Title"].ShouldBe(service.Get("Home.Title")));
    }

    [Fact]
    public void Sources_are_merged_so_a_plugin_can_add_its_own_keys()
    {
        var plugin = InMemoryResourceSource.Of(
            "plugin",
            ("en-GB", "Plugins.Manual.Name", "Manual payments"),
            ("pl-PL", "Plugins.Manual.Name", "Płatności ręczne"));
        var service = CreateService(Host, plugin);

        service.Get("Plugins.Manual.Name", "pl-PL").ShouldBe("Płatności ręczne");
        service.Get("Home.Title", "pl-PL").ShouldBe("Start");
    }

    [Fact]
    public void A_key_defined_by_two_sources_for_the_same_culture_throws_naming_both_sources_and_the_key()
    {
        var plugin = InMemoryResourceSource.Of("plugin", ("en-GB", "Home.Title", "Overridden"));
        var service = CreateService(Host, plugin);

        var ex = Should.Throw<InvalidOperationException>(() => service.Get("Home.Title", "en-GB"));

        ex.Message.ShouldContain("Home.Title");
        ex.Message.ShouldContain("host");
        ex.Message.ShouldContain("plugin");
    }

    [Fact]
    public void An_unsupported_culture_from_any_source_fails_the_load_naming_the_source_and_culture()
    {
        var german = InMemoryResourceSource.Of("german-plugin", ("de-DE", "Home.Title", "Startseite"));
        var service = CreateService(Host, german);

        var ex = Should.Throw<InvalidOperationException>(() => service.Cultures);

        ex.Message.ShouldContain("de-DE");
        ex.Message.ShouldContain("german-plugin");
    }

    [Fact]
    public void The_same_key_in_different_cultures_from_different_sources_is_not_a_conflict()
    {
        var english = InMemoryResourceSource.Of("english", ("en-GB", "Shared.Key", "Shared"));
        var polish = InMemoryResourceSource.Of("polish", ("pl-PL", "Shared.Key", "Wspólny"));
        var service = CreateService(english, polish);

        service.Get("Shared.Key", "pl-PL").ShouldBe("Wspólny");
    }

    [Fact]
    public void Sources_are_loaded_once_even_under_concurrent_first_use()
    {
        var loads = 0;
        var service = CreateService(new CountingSource(() => Interlocked.Increment(ref loads)));

        Parallel.For(0, 50, _ => service.Get("Count.Key", "en-GB").ShouldBe("counted"));

        loads.ShouldBe(1);
    }

    [Fact]
    public void Get_rejects_an_empty_key()
    {
        var service = CreateService(Host);

        Should.Throw<ArgumentException>(() => service.Get(" ", "en-GB"));
    }

    private LocalizationService CreateService(params IResourceSource[] sources) => new(sources, _logger);

    private static void WithUICulture(string culture, Action action)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    private sealed class CountingSource(Action onLoad) : IResourceSource
    {
        public string Name => "counting";

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
        {
            onLoad();
            return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en-GB"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Count.Key"] = "counted" },
            };
        }
    }
}