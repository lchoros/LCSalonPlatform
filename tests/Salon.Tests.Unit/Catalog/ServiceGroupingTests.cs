using NodaTime;

using Salon.Core.Catalog;

namespace Salon.Tests.Unit.Catalog;

/// <summary>Both service lists group by category: categories in their sort order, services in theirs, uncategorised last, empty categories left out (ADR 0013).</summary>
public class ServiceGroupingTests
{
    private static readonly ServiceCategory Hair = new() { Name = "Hair", SortOrder = 1 };
    private static readonly ServiceCategory Nails = new() { Name = "Nails", SortOrder = 3 };
    private static readonly ServiceCategory Barbering = new() { Name = "Barbering", SortOrder = 2 };

    [Fact]
    public void No_services_means_no_groups()
    {
        ServiceGrouping.Group([], [Hair, Nails]).ShouldBeEmpty();
    }

    [Fact]
    public void Services_without_a_category_form_one_group_with_no_category()
    {
        var groups = ServiceGrouping.Group([Service("Cut", 2), Service("Colour", 1)], [Hair]);

        var group = groups.ShouldHaveSingleItem();
        group.Category.ShouldBeNull();
        group.Services.Select(s => s.Name).ShouldBe(["Colour", "Cut"], "services keep their sort order");
    }

    [Fact]
    public void Categories_come_in_sort_order_and_the_uncategorised_group_last()
    {
        var services = new[]
        {
            Service("Manicure", 5, Nails.Id),
            Service("Walk-in fringe trim", 6),
            Service("Beard trim", 4, Barbering.Id),
            Service("Balayage", 3, Hair.Id),
            Service("Cut & blow-dry", 1, Hair.Id),
        };

        var groups = ServiceGrouping.Group(services, [Nails, Hair, Barbering]);

        groups.Select(g => g.Category?.Name).ShouldBe(["Hair", "Barbering", "Nails", null]);
        groups[0].Services.Select(s => s.Name).ShouldBe(["Cut & blow-dry", "Balayage"]);
        groups[3].Services.Select(s => s.Name).ShouldBe(["Walk-in fringe trim"]);
    }

    [Fact]
    public void An_empty_category_is_left_out()
    {
        var groups = ServiceGrouping.Group([Service("Manicure", 1, Nails.Id)], [Hair, Nails]);

        groups.Select(g => g.Category?.Name).ShouldBe(["Nails"]);
    }

    [Fact]
    public void Categories_with_the_same_sort_order_fall_back_to_the_name()
    {
        var a = new ServiceCategory { Name = "Waxing", SortOrder = 1 };
        var b = new ServiceCategory { Name = "brows", SortOrder = 1 };

        var groups = ServiceGrouping.Group([Service("Full leg", 1, a.Id), Service("Tint", 1, b.Id)], [a, b]);

        groups.Select(g => g.Category?.Name).ShouldBe(["brows", "Waxing"], "ordinal, case-insensitive");
    }

    [Fact]
    public void Services_with_the_same_sort_order_fall_back_to_the_name()
    {
        var groups = ServiceGrouping.Group([Service("Pedicure", 1, Nails.Id), Service("Manicure", 1, Nails.Id)], [Nails]);

        groups.Single().Services.Select(s => s.Name).ShouldBe(["Manicure", "Pedicure"]);
    }

    [Fact]
    public void A_service_pointing_at_an_unknown_category_counts_as_uncategorised()
    {
        var groups = ServiceGrouping.Group([Service("Orphan", 1, Guid.CreateVersion7()), Service("Cut", 1, Hair.Id)], [Hair]);

        groups.Select(g => g.Category?.Name).ShouldBe(["Hair", null]);
        groups[1].Services.Single().Name.ShouldBe("Orphan");
    }

    [Fact]
    public void Inactive_categories_still_group_the_services_that_point_at_them()
    {
        var retired = new ServiceCategory { Name = "Retired", SortOrder = 0, IsActive = false };

        var groups = ServiceGrouping.Group([Service("Perm", 1, retired.Id)], [retired, Hair]);

        groups.Single().Category.ShouldBe(retired);
    }

    private static Service Service(string name, int sortOrder, Guid? categoryId = null)
        => new() { Name = name, Currency = "GBP", Duration = Duration.FromMinutes(30), SortOrder = sortOrder, CategoryId = categoryId };
}