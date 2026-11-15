using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Paging;
using Salon.Web.Framework.Lists;

namespace Salon.Tests.Unit.Catalog;

/// <summary>
/// How a salon's services become the entries of a list (ADR 0032): variants under one title, a category filter, a
/// search in which every word must occur, and pages that always exist.
/// </summary>
public class ServiceCatalogueTests
{
    private static readonly ServiceCategory Hair = new() { Name = "Hair", SortOrder = 1 };
    private static readonly ServiceCategory Brows = new() { Name = "Brows", SortOrder = 2 };

    private static readonly Service[] Services =
    [
        Service("Dry Cut", Hair, 1, 19m),
        Variant("Keratin", "Medium hair", Hair, 2, 70m),
        Service("Fringe", Hair, 3, 5m, description: "A quick trim between cuts."),
        Variant("Keratin", "Long hair", Hair, 4, 85m),
        Service("Brows Lamination", Brows, 5, 30m),
        Variant("Keratin", "Brow keratin", Brows, 6, 25m),
        Service("Gift wrapping", null, 7, 2m),
    ];

    [Fact]
    public void Variants_of_one_category_become_one_entry_at_the_place_of_the_first()
    {
        var entries = ServiceCatalogue.Entries(Services, [Hair, Brows]);

        entries.Select(e => (Category: e.Category?.Name, e.Title, e.IsVariantGroup, Count: e.Services.Count)).ShouldBe(
        [
            ("Hair", "Dry Cut", false, 1),
            ("Hair", "Keratin", true, 2),
            ("Hair", "Fringe", false, 1),
            ("Brows", "Brows Lamination", false, 1),
            ("Brows", "Keratin", true, 1),
            (null, "Gift wrapping", false, 1),
        ]);
        var keratin = entries[1];
        keratin.Services.Select(s => s.VariantLabel).ShouldBe(["Medium hair", "Long hair"]);
        (keratin.MinPrice, keratin.MaxPrice).ShouldBe((70m, 85m));
        (entries[0].MinPrice, entries[0].MaxPrice).ShouldBe((19m, 19m));
    }

    [Fact]
    public void A_category_narrows_the_list_and_an_unknown_one_leaves_nothing()
    {
        ServiceCatalogue.Entries(Services, [Hair, Brows], Brows.Id).Select(e => e.Title).ShouldBe(["Brows Lamination", "Keratin"]);
        ServiceCatalogue.Entries(Services, [Hair, Brows], Guid.NewGuid()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("keratin", new[] { "Keratin", "Keratin" })]
    [InlineData("KERATIN long", new[] { "Keratin" })]
    [InlineData("  long   hair ", new[] { "Keratin" })]
    [InlineData("cut", new[] { "Dry Cut", "Fringe" })]
    [InlineData("brow", new[] { "Brows Lamination", "Keratin" })]
    [InlineData("keratin fringe", new string[0])]
    [InlineData("   ", new[] { "Dry Cut", "Keratin", "Fringe", "Brows Lamination", "Keratin", "Gift wrapping" })]
    public void A_search_keeps_the_services_in_which_every_word_occurs_whatever_the_case(string search, string[] titles)
    {
        ServiceCatalogue.Entries(Services, [Hair, Brows], search: search).Select(e => e.Title).ShouldBe(titles);
    }

    [Fact]
    public void A_search_that_finds_one_variant_shows_that_variant_alone_under_its_title()
    {
        var entry = ServiceCatalogue.Entries(Services, [Hair, Brows], Hair.Id, "long").ShouldHaveSingleItem();

        entry.IsVariantGroup.ShouldBeTrue();
        entry.Services.ShouldHaveSingleItem().VariantLabel.ShouldBe("Long hair");
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(" ", "", true)]
    [InlineData("Keratin", "Long hair", true)]
    [InlineData("Keratin", " ", false)]
    [InlineData(null, "Long hair", false)]
    public void A_variant_has_a_title_and_a_label_or_neither(string? group, string? label, bool complete)
    {
        ServiceRules.VariantIsComplete(group, label).ShouldBe(complete);
    }

    [Fact]
    public void Variant_text_is_trimmed_and_has_its_limits()
    {
        ServiceRules.NormalizeVariant("  Keratin ").ShouldBe("Keratin");
        ServiceRules.NormalizeVariant("   ").ShouldBeNull();
        ServiceRules.VariantGroupFits(new string('x', ServiceRules.VariantGroupMaxLength)).ShouldBeTrue();
        ServiceRules.VariantGroupFits(new string('x', ServiceRules.VariantGroupMaxLength + 1)).ShouldBeFalse();
        ServiceRules.VariantLabelFits(new string('x', ServiceRules.VariantLabelMaxLength + 1)).ShouldBeFalse();
        ServiceRules.VariantSeparator.ShouldBe(" · ");
    }

    [Theory]
    [InlineData(null, 1, 1, 10)]
    [InlineData(1, 1, 1, 10)]
    [InlineData(2, 2, 11, 20)]
    [InlineData(3, 3, 21, 25)]
    [InlineData(0, 1, 1, 10)]
    [InlineData(-4, 1, 1, 10)]
    [InlineData(99, 3, 21, 25)]
    public void A_page_that_does_not_exist_is_moved_to_the_nearest_one(int? asked, int page, int from, int to)
    {
        var paged = Pages.Cut(Enumerable.Range(1, 25).ToList(), asked, 10);

        (paged.Page, paged.From, paged.To, paged.Total, paged.TotalPages).ShouldBe((page, from, to, 25, 3));
        paged.Items.ShouldBe(Enumerable.Range(from, to - from + 1));
        paged.HasPrevious.ShouldBe(page > 1);
        paged.HasNext.ShouldBe(page < 3);
    }

    [Fact]
    public void An_empty_list_has_one_empty_page()
    {
        var paged = Pages.Cut(Array.Empty<int>(), 5, 10);

        (paged.Page, paged.TotalPages, paged.From, paged.To, paged.Total).ShouldBe((1, 1, 0, 0, 0));
        paged.Items.ShouldBeEmpty();
        paged.HasNext.ShouldBeFalse();
    }

    [Property]
    public Property Pages_hold_every_item_exactly_once(byte count, byte size)
    {
        var all = Enumerable.Range(0, count).ToList();
        var pageSize = (size % 30) + 1;
        var first = Pages.Cut(all, 1, pageSize);

        var seen = Enumerable.Range(1, first.TotalPages).SelectMany(n => Pages.Cut(all, n, pageSize).Items).ToList();

        return seen.SequenceEqual(all).ToProperty();
    }

    [Fact]
    public void A_list_address_carries_only_the_filters_that_have_a_value()
    {
        ListUrl.Build("/book").ShouldBe("/book");
        ListUrl.Build("/book", ("category", null), ("q", "  "), ("page", "1")).ShouldBe("/book");
        ListUrl.Build("/book", ("category", "abc"), ("q", " cut & blow dry "), ("page", "2")).ShouldBe("/book?category=abc&q=cut%20%26%20blow%20dry&page=2");
        ListUrl.Build("/admin/services", ("q", "<script>")).ShouldBe("/admin/services?q=%3Cscript%3E");
    }

    [Fact]
    public void A_pager_is_needed_only_for_more_than_one_page()
    {
        PagerModel.For(Pages.Cut(Enumerable.Range(1, 5).ToList(), 1, 10), n => $"/x?page={n}").IsNeeded.ShouldBeFalse();
        var pager = PagerModel.For(Pages.Cut(Enumerable.Range(1, 25).ToList(), 2, 10), n => $"/x?page={n}");
        pager.IsNeeded.ShouldBeTrue();
        (pager.Page, pager.TotalPages, pager.From, pager.To, pager.Total).ShouldBe((2, 3, 11, 20, 25));
        pager.PageUrl(3).ShouldBe("/x?page=3");
    }

    private static Service Service(string name, ServiceCategory? category, int sortOrder, decimal price, string? description = null)
        => new() { Name = name, Currency = "GBP", Duration = Duration.FromMinutes(30), Price = price, SortOrder = sortOrder, CategoryId = category?.Id, Description = description };

    private static Service Variant(string group, string label, ServiceCategory category, int sortOrder, decimal price)
        => new()
        {
            Name = group + ServiceRules.VariantSeparator + label,
            VariantGroup = group,
            VariantLabel = label,
            Currency = "GBP",
            Duration = Duration.FromMinutes(60),
            Price = price,
            SortOrder = sortOrder,
            CategoryId = category.Id,
        };
}