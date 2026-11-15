using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Products;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Products;

/// <summary>
/// Products and stock over HTTP (ADR 0036): the list with its low mark, search, filter and pages; adding and changing
/// a product; the four ways a shelf changes, each with its line in the history and the running total kept equal to
/// the lines; what is refused and changes nothing; who may do what; and nothing of another salon.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ProductsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";
    private const string ListPath = "/admin/products";
    private const string NewPath = "/admin/products/new";

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_empty_shelf_invites_the_owner_to_add_a_product_and_tells_staff_who_does()
    {
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);
        var forOwner = await GetAsync(owner, ListPath);
        forOwner.ShouldContain("data-testid=\"empty-state\"");
        forOwner.ShouldContain("No products yet. Add the first one");
        forOwner.ShouldContain($"href=\"{NewPath}\" data-testid=\"add-product\"");
        forOwner.ShouldContain("data-testid=\"nav-products\" aria-current=\"true\"");
        forOwner.ShouldNotContain("data-testid=\"product-filters\"");

        using var staff = await factory.SignedInClientAsync(Host, _studio.Id, Role.Staff);
        var forStaff = await GetAsync(staff, ListPath);
        forStaff.ShouldContain("The owner or a manager adds them.");
        forStaff.ShouldNotContain("data-testid=\"add-product\"");
    }

    [Fact]
    public async Task Adding_a_product_with_a_count_stores_it_trimmed_in_the_salons_currency_with_its_first_line()
    {
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var response = await PostAsync(owner, NewPath, NewPath, new()
        {
            ["Name"] = "  Shampoo 250 ml ",
            ["Brand"] = " Salon Care ",
            ["Code"] = "5012345678900",
            ["RetailPrice"] = "12.50",
            ["LowStockAt"] = "3",
            ["IsActive"] = "true",
            ["OpeningStock"] = "12",
        });

        var product = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{ListPath}/{product.Id}");
        product.Name.ShouldBe("Shampoo 250 ml");
        product.Brand.ShouldBe("Salon Care");
        product.Code.ShouldBe("5012345678900");
        product.RetailPrice.ShouldBe(12.50m);
        product.Currency.ShouldBe("GBP");
        product.LowStockAt.ShouldBe(3);
        product.IsActive.ShouldBeTrue();
        product.StockOnHand.ShouldBe(12);
        var line = (await LinesAsync(_studio.Id, product.Id)).ShouldHaveSingleItem();
        line.Kind.ShouldBe(StockMovementKind.Counted);
        line.Quantity.ShouldBe(12);
        line.OnHandAfter.ShouldBe(12);
        line.ByUserId.ShouldNotBeNull();

        var card = WebUtility.HtmlDecode(await GetAsync(owner, $"{ListPath}/{product.Id}"));
        card.ShouldContain("data-testid=\"flash\" class=\"notice notice-ok\">“Shampoo 250 ml” has been added.<");
        card.ShouldContain("data-testid=\"on-hand\">12<");
        card.ShouldContain("data-testid=\"product-price\">£12.50<");
        card.ShouldContain("data-testid=\"line-kind\">Stock count<");
        card.ShouldContain("data-testid=\"line-quantity\">+12<");
        card.ShouldContain("Warns at 3 or fewer.");
    }

    [Fact]
    public async Task A_product_without_a_price_or_a_count_is_for_salon_use_and_starts_empty()
    {
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        await PostAsync(owner, NewPath, NewPath, new() { ["Name"] = "Colour tube 6.0", ["IsActive"] = "true" });

        var product = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        product.RetailPrice.ShouldBeNull();
        product.LowStockAt.ShouldBeNull();
        product.StockOnHand.ShouldBe(0);
        (await LinesAsync(_studio.Id, product.Id)).ShouldBeEmpty();
        var card = await GetAsync(owner, $"{ListPath}/{product.Id}");
        card.ShouldContain("Salon use only");
        card.ShouldContain("data-testid=\"history-empty\"");
        card.ShouldContain("data-low=\"false\"", Case.Sensitive, "no threshold, no warning, even at zero");
    }

    [Theory]
    [InlineData("", "12", "3", "Enter the product name.")]
    [InlineData("shampoo 250 ML", "12", "3", "A product with this name already exists.")]
    [InlineData("Conditioner", "12.505", "3", "Enter a price from 0 to 100000, with at most two decimal places, or leave it empty.")]
    [InlineData("Conditioner", "-1", "3", "Enter a price from 0 to 100000, with at most two decimal places, or leave it empty.")]
    [InlineData("Conditioner", "12", "-1", "Enter a whole number from 0 to 10000, or leave it empty.")]
    public async Task A_form_with_a_mistake_comes_back_with_the_reason_and_saves_nothing(string name, string price, string lowAt, string message)
    {
        await AddProductAsync(_studio.Id, "Shampoo 250 ml");
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var response = await PostAsync(owner, NewPath, NewPath, new() { ["Name"] = name, ["RetailPrice"] = price, ["LowStockAt"] = lowAt, ["IsActive"] = "true" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"product-form\"");
        html.ShouldContain(message);
        (await ProductsAsync(_studio.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Changing_a_product_keeps_its_shelf_and_may_keep_its_own_name()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", price: 12.50m, lowAt: 3, onHand: 8);
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);
        var editPath = $"{ListPath}/{id}/edit";

        var form = await GetAsync(owner, editPath);
        form.ShouldContain("value=\"Shampoo 250 ml\"");
        form.ShouldContain("data-testid=\"product-active\"");
        form.ShouldNotContain("data-testid=\"product-opening-input\"", Case.Sensitive, "an existing shelf changes on its own page");

        var response = await PostAsync(owner, editPath, editPath, new()
        {
            ["Name"] = "Shampoo 250 ML",
            ["RetailPrice"] = "13",
            ["LowStockAt"] = "",
            ["IsActive"] = "false",
            ["OpeningStock"] = "99",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{ListPath}/{id}");
        var product = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        product.Name.ShouldBe("Shampoo 250 ML");
        product.RetailPrice.ShouldBe(13m);
        product.LowStockAt.ShouldBeNull();
        product.IsActive.ShouldBeFalse();
        product.StockOnHand.ShouldBe(8, "the form never touches the shelf");
        (await LinesAsync(_studio.Id, id)).Count.ShouldBe(1);
        var card = WebUtility.HtmlDecode(await GetAsync(owner, $"{ListPath}/{id}"));
        card.ShouldContain("Changes to “Shampoo 250 ML” saved.");
        card.ShouldContain("data-testid=\"badge-not-in-use\"");

        (await owner.GetAsync(new Uri($"{ListPath}/{Guid.NewGuid()}/edit", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_delivery_a_use_a_write_off_and_a_count_each_add_a_line_and_move_the_total()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);
        var card = $"{ListPath}/{id}";

        (await RecordAsync(owner, id, "received", "6", "Invoice 1042")).ShouldContain("6 units added. The shelf now holds 18.");
        (await RecordAsync(owner, id, "used", "1", null)).ShouldContain("1 unit taken off as used. The shelf now holds 17.");
        (await RecordAsync(owner, id, "written-off", "2", "Broken in the delivery")).ShouldContain("2 units written off. The shelf now holds 15.");
        (await RecordAsync(owner, id, "counted", "14", null)).ShouldContain("The count is recorded. It differed by 1 unit; the shelf holds 14.");
        (await RecordAsync(owner, id, "counted", "14", null)).ShouldContain("The count matches the record: 14 on the shelf. Nothing was changed.");

        var lines = await LinesAsync(_studio.Id, id);
        lines.Select(l => (l.Kind, l.Quantity, l.OnHandAfter)).ShouldBe(
        [
            (StockMovementKind.Counted, 12, 12),
            (StockMovementKind.Received, 6, 18),
            (StockMovementKind.UsedInSalon, -1, 17),
            (StockMovementKind.WrittenOff, -2, 15),
            (StockMovementKind.Counted, -1, 14),
        ]);
        lines[1].Note.ShouldBe("Invoice 1042");
        lines[3].Note.ShouldBe("Broken in the delivery");
        var product = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        product.StockOnHand.ShouldBe(14);
        product.StockOnHand.ShouldBe(StockRules.OnHand(lines.Select(l => l.Quantity)), "the total is the sum of the lines");

        var html = WebUtility.HtmlDecode(await GetAsync(owner, card));
        html.ShouldContain("data-testid=\"stock-history\" data-total=\"5\"");
        html.IndexOf(">Stock count<", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf(">Written off<", StringComparison.Ordinal), "newest first");
        html.ShouldContain("data-testid=\"line-quantity\">−2<");
        html.ShouldContain("data-testid=\"line-note\">Broken in the delivery<");
        html.ShouldContain("then 15 on the shelf");
        html.ShouldContain(" · owner@studio-20.localhost<", Case.Sensitive, "each line names who added it (a test user's display name is its email)");
        html.ShouldContain(" · unknown user<", Case.Sensitive, "the seeded first count has no login");
    }

    [Theory]
    [InlineData("used", "13", "", "The record shows only 12 on the shelf. If there are more, count the shelf first.")]
    [InlineData("written-off", "13", "Lost", "The record shows only 12 on the shelf. If there are more, count the shelf first.")]
    [InlineData("written-off", "1", "  ", "Say why the units are written off.")]
    [InlineData("received", "0", "", "Enter a whole number: 1 or more units, or 0 or more for a count.")]
    [InlineData("received", "abc", "", "Enter a whole number: 1 or more units, or 0 or more for a count.")]
    [InlineData("used", "", "", "Enter a whole number: 1 or more units, or 0 or more for a count.")]
    [InlineData("counted", "-1", "", "Enter a whole number: 1 or more units, or 0 or more for a count.")]
    [InlineData("received", "10001", "", "Enter a whole number: 1 or more units, or 0 or more for a count.")]
    public async Task A_change_that_cannot_be_right_is_refused_with_the_reason_and_changes_nothing(string kind, string number, string note, string message)
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var html = await RecordAsync(owner, id, kind, number, note);

        html.ShouldContain("data-testid=\"stock-problem\" class=\"notice notice-error\">" + message + "<");
        html.ShouldNotContain("data-testid=\"flash\"");
        (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem().StockOnHand.ShouldBe(12);
        (await LinesAsync(_studio.Id, id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_list_marks_low_products_and_narrows_by_filter_and_search()
    {
        await AddProductAsync(_studio.Id, "Shampoo 250 ml", brand: "Salon Care", price: 12.50m, lowAt: 3, onHand: 3);
        await AddProductAsync(_studio.Id, "Conditioner 250 ml", brand: "Salon Care", price: 13m, lowAt: 3, onHand: 9);
        await AddProductAsync(_studio.Id, "Colour tube 6.0", lowAt: 0, onHand: 0);
        await AddProductAsync(_studio.Id, "Old hairspray", lowAt: 5, onHand: 1, active: false);
        using var staff = await factory.SignedInClientAsync(Host, _studio.Id, Role.Staff);

        var all = WebUtility.HtmlDecode(await GetAsync(staff, ListPath));
        Names(all).ShouldBe(["Colour tube 6.0", "Conditioner 250 ml", "Shampoo 250 ml"], "by name, without the product no longer used");
        Regex.Count(all, "data-testid=\"badge-low\"").ShouldBe(2);
        all.ShouldContain(">Low stock (2)<", Case.Sensitive, "a product no longer used is not low");
        all.ShouldContain(">No longer used (1)<");
        all.ShouldContain("data-testid=\"product-on-hand\">9<");
        all.ShouldContain("Salon use only");
        all.ShouldContain("£12.50");

        Names(await GetAsync(staff, ListPath + "?show=low")).ShouldBe(["Colour tube 6.0", "Shampoo 250 ml"]);
        Names(await GetAsync(staff, ListPath + "?show=not-in-use")).ShouldBe(["Old hairspray"]);
        Names(await GetAsync(staff, ListPath + "?q=care+SHAMPOO")).ShouldBe(["Shampoo 250 ml"]);
        Names(await GetAsync(staff, ListPath + "?show=low&q=salon")).ShouldBe(["Shampoo 250 ml"]);

        var nothing = await GetAsync(staff, ListPath + "?q=wax");
        nothing.ShouldContain("data-testid=\"no-match\"");
        nothing.ShouldContain("No products match.");
        nothing.ShouldContain("name=\"q\" type=\"search\" value=\"wax\"");
    }

    [Fact]
    public async Task A_long_list_is_cut_into_pages_and_the_filter_rides_along()
    {
        for (var i = 1; i <= 26; i++)
        {
            await AddProductAsync(_studio.Id, $"Product {i:00}", lowAt: 2, onHand: 1);
        }

        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var first = await GetAsync(owner, ListPath + "?show=low");
        Names(first).Count.ShouldBe(24);
        first.ShouldContain("data-testid=\"product-list\" data-total=\"26\"");
        first.ShouldContain("href=\"/admin/products?show=low&amp;page=2\"");

        var second = await GetAsync(owner, ListPath + "?show=low&page=2");
        Names(second).ShouldBe(["Product 25", "Product 26"]);

        Names(await GetAsync(owner, ListPath + "?show=low&page=99")).ShouldBe(["Product 25", "Product 26"], "a page that does not exist moves to the last one");
    }

    [Fact]
    public async Task The_history_is_cut_into_pages_newest_first()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml");
        for (var i = 0; i < 22; i++)
        {
            await StockAsync(_studio.Id, id, StockMovementKind.Received, 1);
        }

        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        var first = await GetAsync(owner, $"{ListPath}/{id}");
        Regex.Count(first, "data-testid=\"stock-line\"").ShouldBe(20);
        first.ShouldContain("then 22 on the shelf");
        first.ShouldContain($"href=\"{ListPath}/{id}?page=2\"");
        var second = await GetAsync(owner, $"{ListPath}/{id}?page=2");
        Regex.Count(second, "data-testid=\"stock-line\"").ShouldBe(2);
        second.ShouldContain("then 1 on the shelf");
    }

    [Fact]
    public async Task Everybody_at_the_chair_records_deliveries_and_uses_but_only_a_manager_counts_writes_off_and_edits()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        using var staff = await factory.SignedInClientAsync(Host, _studio.Id, Role.Staff);
        using var manager = await factory.SignedInClientAsync(Host, _studio.Id, Role.Manager);

        var card = await GetAsync(staff, $"{ListPath}/{id}");
        card.ShouldContain("data-testid=\"form-received\"");
        card.ShouldContain("data-testid=\"form-used\"");
        card.ShouldNotContain("data-testid=\"form-counted\"");
        card.ShouldNotContain("data-testid=\"form-written-off\"");
        card.ShouldNotContain("data-testid=\"edit-product\"");

        (await RecordAsync(staff, id, "used", "2", null)).ShouldContain("2 units taken off as used. The shelf now holds 10.");
        (await RecordAsync(staff, id, "received", "1", null)).ShouldContain("1 unit added. The shelf now holds 11.");

        foreach (var kind in new[] { "counted", "written-off" })
        {
            var refused = await PostAsync(staff, $"{ListPath}/{id}", $"{ListPath}/{id}/stock/{kind}", new() { ["number"] = "1", ["note"] = "Lost" });
            refused.StatusCode.ShouldBe(HttpStatusCode.Redirect, kind);
            refused.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", kind);
        }

        foreach (var path in new[] { NewPath, $"{ListPath}/{id}/edit" })
        {
            var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);
        }

        (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem().StockOnHand.ShouldBe(11);

        var forManager = await GetAsync(manager, $"{ListPath}/{id}");
        forManager.ShouldContain("data-testid=\"form-counted\"");
        forManager.ShouldContain("data-testid=\"form-written-off\"");
        forManager.ShouldContain("data-testid=\"edit-product\"");
        (await RecordAsync(manager, id, "counted", "10", null)).ShouldContain("It differed by 1 unit; the shelf holds 10.");
    }

    [Fact]
    public async Task The_pages_need_a_sign_in_a_salon_a_token_and_a_known_kind()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        using var anonymous = factory.ClientFor(Host);
        using var owner = await factory.SignedInClientAsync(Host, _studio.Id, Role.Owner);

        foreach (var path in new[] { ListPath, NewPath, $"{ListPath}/{id}", $"{ListPath}/{id}/edit" })
        {
            var login = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
            login.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login", path);
        }

        using var platform = factory.CreateClient();
        platform.DefaultRequestHeaders.Host = "localhost";
        (await platform.GetAsync(new Uri(ListPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var noToken = await owner.PostAsync(new Uri($"{ListPath}/{id}/stock/received", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["number"] = "5" }));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await PostAsync(owner, $"{ListPath}/{id}", $"{ListPath}/{id}/stock/sold", new() { ["number"] = "1" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem().StockOnHand.ShouldBe(12);
    }

    [Fact]
    public async Task Another_salon_sees_and_changes_nothing_and_may_use_the_same_name()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        using var other = await factory.SignedInClientAsync("salon-pl.localhost", _polish.Id, Role.Owner);

        var list = await GetAsync(other, ListPath);
        list.ShouldContain("data-testid=\"empty-state\"");
        list.ShouldNotContain("Shampoo");
        (await other.GetAsync(new Uri($"{ListPath}/{id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync(new Uri($"{ListPath}/{id}/edit", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, ListPath, $"{ListPath}/{id}/stock/used", new() { ["number"] = "12" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, NewPath, $"{ListPath}/{id}/edit", new() { ["Name"] = "Taken", ["IsActive"] = "true" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var same = await PostAsync(other, NewPath, NewPath, new() { ["Name"] = "Shampoo 250 ml", ["RetailPrice"] = "45,50", ["IsActive"] = "true", ["OpeningStock"] = "2" });
        same.StatusCode.ShouldBe(HttpStatusCode.Redirect, "a name is unique per salon, not across salons");

        var studio = (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem();
        studio.Name.ShouldBe("Shampoo 250 ml");
        studio.StockOnHand.ShouldBe(12);
        var polish = (await ProductsAsync(_polish.Id)).ShouldHaveSingleItem();
        polish.Currency.ShouldBe("PLN");
        polish.RetailPrice.ShouldBe(45.50m, "a Polish salon types a decimal comma");
        polish.StockOnHand.ShouldBe(2);

        var polishList = await GetAsync(other, ListPath);
        polishList.ShouldContain("<html lang=\"pl-PL\"");
        polishList.ShouldContain("Produkty");
        polishList.ShouldContain("na półce");
    }

    [Fact]
    public async Task Two_changes_to_one_shelf_at_the_same_moment_keep_the_first_and_refuse_the_second()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 12);
        await using var slow = factory.CreateTenantScope(_studio.Id);
        var slowDb = slow.ServiceProvider.GetRequiredService<SalonDbContext>();

        // The slow request has read the shelf; a fast one then takes two units and commits.
        (await slowDb.Products.SingleAsync(p => p.Id == id)).StockOnHand.ShouldBe(12);
        (await StockAsync(_studio.Id, id, StockMovementKind.UsedInSalon, 2)).Outcome.ShouldBe(StockOutcome.Recorded);

        var late = await slow.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(id, StockMovementKind.UsedInSalon, 11, null, null);

        late.Outcome.ShouldBe(StockOutcome.ChangedMeanwhile);
        late.OnHand.ShouldBe(10, "the answer carries the shelf as it is now");
        var lines = await LinesAsync(_studio.Id, id);
        lines.Count.ShouldBe(2);
        (await ProductsAsync(_studio.Id)).ShouldHaveSingleItem().StockOnHand.ShouldBe(10);
        StockRules.OnHand(lines.Select(l => l.Quantity)).ShouldBe(10);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_shelf_below_zero_and_an_empty_line()
    {
        var id = await AddProductAsync(_studio.Id, "Shampoo 250 ml", onHand: 2);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();

        var product = await db.Products.SingleAsync(p => p.Id == id);
        product.StockOnHand = -1;
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ck_products_stock_on_hand_not_negative");
        db.ChangeTracker.Clear();

        db.StockMovements.Add(new StockMovement { ProductId = id, Kind = StockMovementKind.Received, Quantity = 0, OnHandAfter = 2 });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ck_stock_movements_quantity_not_zero");
    }

    private static List<string> Names(string html)
        => NamePattern().Matches(WebUtility.HtmlDecode(html)).Select(m => m.Groups["name"].Value).ToList();

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GETs <paramref name="tokenPage"/> for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(await GetAsync(client, tokenPage));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    /// <summary>Posts one of the product page's stock forms and returns the page it lands on, decoded.</summary>
    private static async Task<string> RecordAsync(HttpClient client, Guid id, string kind, string number, string? note)
    {
        var card = $"{ListPath}/{id}";
        var fields = new Dictionary<string, string> { ["number"] = number };
        if (note is not null)
        {
            fields["note"] = note;
        }

        var response = await PostAsync(client, card, $"{card}/stock/{kind}", fields);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, kind);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(card);
        return WebUtility.HtmlDecode(await GetAsync(client, card));
    }

    /// <summary>A product put on the shelf the way the app does it: the row, then a first count through the service.</summary>
    private async Task<Guid> AddProductAsync(Guid tenantId, string name, string? brand = null, decimal? price = null, int? lowAt = null, int onHand = 0, bool active = true)
    {
        Guid id;
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var product = new Product { Name = name, Brand = brand, RetailPrice = price, Currency = "GBP", LowStockAt = lowAt, IsActive = active };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        }

        if (onHand > 0)
        {
            (await StockAsync(tenantId, id, StockMovementKind.Counted, onHand)).Outcome.ShouldBe(StockOutcome.Recorded);
        }

        return id;
    }

    private async Task<StockResult> StockAsync(Guid tenantId, Guid productId, StockMovementKind kind, int number)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(productId, kind, number, kind == StockMovementKind.WrittenOff ? "Test" : null, null);
    }

    private async Task<List<Product>> ProductsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    private async Task<List<StockMovement>> LinesAsync(Guid tenantId, Guid productId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId).OrderBy(m => m.At).ThenBy(m => m.Id).ToListAsync();
    }

    [GeneratedRegex("data-testid=\"product-name\">(?<name>[^<]+)<")]
    private static partial Regex NamePattern();
}