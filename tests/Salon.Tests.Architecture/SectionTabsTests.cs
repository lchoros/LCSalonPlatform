using Salon.Core.Localization;
using Salon.Services.Localization;
using Salon.Web.Framework.Ui;

namespace Salon.Tests.Architecture;

/// <summary>
/// The rows of section tabs (ADR 0089) are declared in code, where the scan of the views for resource keys cannot see
/// them — so they are held to the same rules here: every label exists in both languages, a key names one tab of its
/// row, and a tab leads into the admin pages and nowhere else.
/// </summary>
public class SectionTabsTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Resources =
        new(() => new EmbeddedJsonResourceSource(Assemblies.Web).Load());

    [Fact]
    public void Every_tab_and_every_row_has_its_words_in_both_languages()
    {
        var keys = AdminSections.All
            .SelectMany(section => section.Tabs.Select(tab => tab.LabelKey).Append(section.LabelKey))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = SupportedCultures.All
            .SelectMany(culture => keys.Where(key => !Resources.Value[culture].ContainsKey(key)).Select(key => $"{culture}: {key}"))
            .ToList();

        keys.ShouldNotBeEmpty();
        missing.ShouldBeEmpty("section tabs ask for texts that do not exist: " + string.Join(", ", missing));
    }

    [Fact]
    public void A_row_names_each_tab_once_and_every_tab_stays_inside_the_admin_pages()
    {
        foreach (var section in AdminSections.All)
        {
            section.Tabs.Count.ShouldBeGreaterThan(1, $"{section.Name}: one page needs no tabs");
            section.Tabs.Select(tab => tab.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(section.Tabs.Count, section.Name);
            section.Tabs.Select(tab => tab.Url).Distinct(StringComparer.Ordinal).Count().ShouldBe(section.Tabs.Count, section.Name);
            section.Tabs.ShouldAllBe(tab => tab.Url.StartsWith("/admin/", StringComparison.Ordinal) && tab.Permissions.Length > 0);
        }

        AdminSections.All.Select(section => section.Name).ShouldBeUnique();
    }
}