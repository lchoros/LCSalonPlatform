using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Screens.Services;
using Salon.Tests.Mobile.Fakes;

namespace Salon.Tests.Mobile.Screens;

public class ServicesScreenTests
{
    [Fact]
    public async Task The_list_is_loaded_once_and_the_salon_is_remembered()
    {
        var app = new Harness();
        var screen = app.ServicesScreen();

        await screen.LoadAsync();
        await screen.LoadAsync();
        await app.ServicesScreen().LoadAsync();

        app.Api.ServicesCalls.ShouldBe(1);
        app.Session.Salon!.Name.ShouldBe("Studio Anna");
        screen.SalonName.ShouldBe("Studio Anna");
        screen.Title.ShouldBe("Choose a service");
        screen.IsBusy.ShouldBeFalse();
        screen.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Rows_show_name_description_price_with_from_and_duration_and_options_share_one_row()
    {
        var screen = await LoadedAsync(new Harness());

        screen.Rows.Select(r => r.Name).ShouldBe(["Cut", "Colour", "Gel nails", "Beard trim"]);

        var cut = screen.Rows[0];
        cut.IsGroup.ShouldBeFalse();
        cut.Description.ShouldBe("A wash, a cut and a blow-dry.");
        cut.PriceText.ShouldBe("£45.00");
        cut.DurationText.ShouldBe("45 min");
        cut.OnlyOption!.ServiceId.ShouldBe(Sample.Cut);
        screen.Rows[1].PriceText.ShouldBe("from £60.00");

        var gel = screen.Rows[2];
        gel.IsGroup.ShouldBeTrue();
        gel.OnlyOption.ShouldBeNull();
        gel.Options.Select(o => o.Label).ShouldBe(["Short", "Long"]);
        gel.Options.Select(o => o.ServiceId).ShouldBe([Sample.GelShort, Sample.GelLong]);
        gel.PriceText.ShouldBe("from £25.00");
        gel.DurationText.ShouldBe("30–60 min");
        gel.OptionsSummary.ShouldBe("Options: Short, Long");
        gel.Description.ShouldBe("Long-lasting colour.");
        gel.IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task Options_at_one_price_show_no_from()
    {
        var app = new Harness();
        app.Server.Services =
        [
            new ServiceItem(Sample.GelShort, "Gel short", null, 30, 25m, false, "Nails", "Gel", "Short", false),
            new ServiceItem(Sample.GelLong, "Gel long", null, 30, 25m, false, "Nails", "Gel", "Long", false),
        ];

        var screen = await LoadedAsync(app);

        screen.Rows.ShouldHaveSingleItem().PriceText.ShouldBe("£25.00");
        screen.Rows[0].DurationText.ShouldBe("30 min");
    }

    [Fact]
    public async Task A_group_opens_and_closes_and_stays_open_through_a_search()
    {
        var screen = await LoadedAsync(new Harness());
        var gel = screen.Rows[2];

        await screen.TapRowAsync(gel);
        gel.IsExpanded.ShouldBeTrue();

        screen.SearchText = "gel";
        screen.Rows.ShouldHaveSingleItem().IsExpanded.ShouldBeTrue();

        await screen.TapRowAsync(screen.Rows[0]);
        screen.Rows[0].IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task Chips_are_all_then_popular_then_each_category_in_order()
    {
        var screen = await LoadedAsync(new Harness());

        screen.Chips.Select(c => c.Label).ShouldBe(["All", "Popular", "Hair", "Nails"]);
        screen.Chips.Single(c => c.IsSelected).Key.ShouldBe(FilterChip.AllKey);
    }

    [Fact]
    public async Task There_is_no_popular_chip_when_nothing_is_ticked()
    {
        var app = new Harness();
        app.Server.Services = app.Server.Services.Select(s => s with { IsPopular = false }).ToList();

        var screen = await LoadedAsync(app);

        screen.Chips.Select(c => c.Key).ShouldNotContain(FilterChip.PopularKey);
    }

    [Fact]
    public async Task The_popular_chip_narrows_across_categories()
    {
        var screen = await LoadedAsync(new Harness());

        screen.SelectChip(screen.Chips.Single(c => c.Key == FilterChip.PopularKey));

        screen.Rows.Select(r => r.Name).ShouldBe(["Cut"]);
        screen.Chips.Single(c => c.IsSelected).Label.ShouldBe("Popular");
    }

    [Fact]
    public async Task A_category_chip_shows_its_services_only()
    {
        var screen = await LoadedAsync(new Harness());

        screen.SelectChip(screen.Chips.Single(c => c.Label == "Nails"));

        screen.Rows.Select(r => r.Name).ShouldBe(["Gel nails"]);
    }

    [Theory]
    [InlineData("CUT", new[] { "Cut" })]
    [InlineData("long", new[] { "Gel nails long" })]
    [InlineData("gel", new[] { "Gel nails" })]
    [InlineData("blow-dry", new[] { "Cut" })]
    [InlineData("hair", new[] { "Cut", "Colour" })]
    [InlineData("  beard ", new[] { "Beard trim" })]
    public async Task Search_matches_name_option_description_and_category_ignoring_case(string search, string[] expected)
    {
        var screen = await LoadedAsync(new Harness());

        screen.SearchText = search;

        screen.Rows.Select(r => r.Name).ShouldBe(expected);
    }

    [Fact]
    public async Task Search_and_a_chip_combine_and_show_all_clears_both()
    {
        var screen = await LoadedAsync(new Harness());
        screen.SelectChip(screen.Chips.Single(c => c.Label == "Hair"));

        screen.SearchText = "gel";

        screen.Rows.ShouldBeEmpty();
        screen.HasNoMatch.ShouldBeTrue();
        screen.IsEmpty.ShouldBeFalse();

        screen.ClearFilters();

        screen.SearchText.ShouldBe(string.Empty);
        screen.Chips.Single(c => c.IsSelected).Key.ShouldBe(FilterChip.AllKey);
        screen.Rows.Count.ShouldBe(4);
        screen.HasNoMatch.ShouldBeFalse();
    }

    [Theory]
    [InlineData("zel", "Żel hybrydowy")]
    [InlineData("ŻEL", "Żel hybrydowy")]
    [InlineData("lydek", "Depilacja łydek")]
    [InlineData("swiateczny", "Świąteczny manicure")]
    public async Task Search_ignores_polish_accents_in_a_polish_salon(string search, string expected)
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon("pl-PL", "PLN");
        app.Server.Services =
        [
            new ServiceItem(Sample.GelShort, "Żel hybrydowy", null, 60, 120m, false, "Paznokcie", null, null, false),
            new ServiceItem(Sample.GelLong, "Depilacja łydek", null, 30, 80m, false, "Depilacja", null, null, false),
            new ServiceItem(Sample.Cut, "Świąteczny manicure", null, 45, 90m, false, "Paznokcie", null, null, false),
        ];

        var screen = await LoadedAsync(app);
        screen.SearchText = search;

        screen.Rows.ShouldHaveSingleItem().Name.ShouldBe(expected);
    }

    [Fact]
    public async Task A_polish_salon_speaks_polish_and_shows_zloty()
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon("pl-PL", "PLN");

        var screen = await LoadedAsync(app);

        app.Text.Language.ShouldBe(AppText.Polish);
        screen.Title.ShouldBe("Wybierz usługę");
        screen.Chips[0].Label.ShouldBe("Wszystkie");
        screen.Chips[1].Label.ShouldBe("Popularne");
        screen.Rows[0].PriceText.ShouldBe("45,00 zł");
        screen.Rows[1].PriceText.ShouldBe("od 60,00 zł");
    }

    [Fact]
    public async Task A_salon_with_no_services_says_so()
    {
        var app = new Harness();
        app.Server.Services = [];

        var screen = await LoadedAsync(app);

        screen.IsEmpty.ShouldBeTrue();
        screen.HasNoMatch.ShouldBeFalse();
        screen.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Choosing_a_service_starts_a_visit_with_nobody_chosen_and_opens_the_time_screen()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);
        app.Session.Date = "2026-11-30";

        await screen.TapRowAsync(screen.Rows[0]);

        app.Session.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, null)]);
        app.Session.Date.ShouldBeNull();
        app.Navigator.Shown.ShouldBe([FakeNavigator.Time]);
    }

    [Fact]
    public async Task Choosing_an_option_of_a_group_books_that_option()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);

        await screen.ChooseServiceAsync(screen.Rows[2].Options[1]);

        app.Session.Parts.ShouldHaveSingleItem().ServiceId.ShouldBe(Sample.GelLong);
    }

    [Fact]
    public async Task Adding_mode_hides_what_is_in_the_visit_and_extends_it_keeping_the_day_and_time()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        app.Session.SetStaff(0, Sample.Anna.ToString());
        app.Session.Date = "2026-11-27";
        app.Session.Start = "2026-11-27T09:00:00Z";
        app.Session.IsAdding = true;
        var screen = app.ServicesScreen();

        await screen.LoadAsync();

        screen.IsAdding.ShouldBeTrue();
        screen.Title.ShouldBe("Add another service");
        screen.Lead.ShouldNotBeNull();
        screen.Rows.Select(r => r.Name).ShouldNotContain("Cut");

        await screen.TapRowAsync(screen.Rows.Single(r => r.Name == "Beard trim"));

        app.Session.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, Sample.Anna.ToString()), new VisitPartRequest(Sample.Beard, null)]);
        app.Session.Date.ShouldBe("2026-11-27");
        app.Session.Start.ShouldBe("2026-11-27T09:00:00Z");
        app.Session.IsAdding.ShouldBeFalse();
        app.Navigator.Last.ShouldBe(FakeNavigator.Time);
    }

    [Fact]
    public async Task Adding_mode_hides_a_group_whose_options_are_all_in_the_visit()
    {
        var app = new Harness();
        app.Visit(Sample.GelShort, Sample.GelLong);
        app.Session.IsAdding = true;
        var screen = app.ServicesScreen();

        await screen.LoadAsync();

        screen.Rows.Select(r => r.Name).ShouldBe(["Cut", "Colour", "Beard trim"]);
    }

    [Fact]
    public async Task Adding_to_a_full_visit_says_so_and_stays()
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon(maxServices: 2);
        app.Visit(Sample.Cut, Sample.Colour);
        app.Session.IsAdding = true;
        var screen = app.ServicesScreen();
        await screen.LoadAsync();

        await screen.TapRowAsync(screen.Rows.Single(r => r.Name == "Beard trim"));

        screen.Message.ShouldBe("This booking already holds as many services as it can.");
        app.Session.Parts.Count.ShouldBe(2);
        app.Navigator.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Back_to_the_booking_leaves_adding_mode_with_the_visit_as_it_was()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        app.Session.IsAdding = true;
        var screen = app.ServicesScreen();
        await screen.LoadAsync();

        await screen.BackToBookingAsync();

        app.Session.IsAdding.ShouldBeFalse();
        screen.IsAdding.ShouldBeFalse();
        app.Session.Parts.ShouldHaveSingleItem().ServiceId.ShouldBe(Sample.Cut);
        app.Navigator.Last.ShouldBe(FakeNavigator.Time);
    }

    [Fact]
    public async Task Offline_shows_an_error_and_retry_loads()
    {
        var app = new Harness();
        app.Api.OnServices = ApiResult.Offline<ServiceList>;
        var screen = app.ServicesScreen();

        await screen.LoadAsync();

        screen.HasError.ShouldBeTrue();
        screen.ErrorMessage.ShouldBe(app.Text[TextKeys.Offline]);
        screen.Rows.ShouldBeEmpty();
        screen.IsEmpty.ShouldBeFalse();

        app.Api.OnServices = () => ApiResult.Ok(app.Server.ServiceList());
        await screen.RetryAsync();

        screen.HasError.ShouldBeFalse();
        screen.Rows.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_refusal_shows_the_servers_sentence_or_our_own()
    {
        var app = new Harness();
        app.Api.OnServices = () => ApiResult.Refused<ServiceList>(404, new ApiProblem("notFound", "This salon is not taking bookings."));
        var screen = app.ServicesScreen();

        await screen.LoadAsync();
        screen.ErrorMessage.ShouldBe("This salon is not taking bookings.");

        app.Api.OnServices = () => ApiResult.Refused<ServiceList>(500, new ApiProblem("unexpected", string.Empty));
        await screen.LoadAsync();
        screen.ErrorMessage.ShouldBe(app.Text[TextKeys.Unexpected]);
    }

    private static async Task<ServicesScreen> LoadedAsync(Harness app)
    {
        var screen = app.ServicesScreen();
        await screen.LoadAsync();
        return screen;
    }
}