using System.Text;
using Salon.Core.Import;
using Salon.Plugin.Import;
using Salon.Services.Catalog.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0066: a price list is read by its headings, in English or Polish and in any order. Every row adds a service,
/// changes the time, price or deposit of one the salon has, changes nothing, or is left out with the reason. An empty
/// cell means "leave it as it is"; a service that is new needs its minutes and its price.
/// </summary>
public class ServiceImportTests
{
    private static readonly IReadOnlyDictionary<string, ExistingService> Nothing = new Dictionary<string, ExistingService>(StringComparer.Ordinal);

    private readonly CsvServiceImportProvider _provider = new();

    [Fact]
    public void A_list_with_every_column_is_read_as_typed()
    {
        var result = Parse("Category,Service,Minutes,Price,Deposit %\nHair, Cut & blow-dry ,45,45.00,20\n,Fringe trim,15 min,£8,\n");

        result.IsSuccess.ShouldBeTrue();
        result.Rows.Count.ShouldBe(2);
        var cut = result.Rows[0];
        (cut.SourceRow, cut.Name, cut.Category, cut.Minutes, cut.Price, cut.DepositPercent).ShouldBe((2, "Cut & blow-dry", "Hair", 45, 45m, 20m));
        cut.Problems.ShouldBeEmpty();
        var fringe = result.Rows[1];
        (fringe.SourceRow, fringe.Name, fringe.Category, fringe.Minutes, fringe.Price, fringe.DepositPercent).ShouldBe((3, "Fringe trim", null, 15, 8m, null));
        fringe.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Polish_headings_a_semicolon_a_decimal_comma_and_a_percent_sign_are_understood()
    {
        var result = Parse("Zadatek %;Cena;Czas trwania;Usługa;Kategoria\n25%;120,50 zł;90;Koloryzacja;Włosy\n");

        result.IsSuccess.ShouldBeTrue();
        var row = result.Rows.ShouldHaveSingleItem();
        (row.Name, row.Category, row.Minutes, row.Price, row.DepositPercent).ShouldBe(("Koloryzacja", "Włosy", 90, 120.50m, 25m));
        row.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Only_a_name_column_is_needed_and_other_columns_are_ignored()
    {
        var result = Parse("Code,Treatment,Colour,In use\nA1,Gel nails,pink,yes\n");

        result.IsSuccess.ShouldBeTrue();
        var row = result.Rows.ShouldHaveSingleItem();
        (row.Name, row.Category, row.Minutes, row.Price, row.DepositPercent).ShouldBe(("Gel nails", null, null, null, null));
    }

    [Fact]
    public void A_file_without_a_name_column_empty_or_not_text_is_refused()
    {
        var noName = Parse("Price,Minutes\n45,45\n");
        noName.Error.ShouldBe(ClientImportError.UnknownFormat);
        noName.Header.ShouldBe(["Price", "Minutes"]);
        noName.Rows.ShouldBeEmpty();

        Parse("Service,Price\n").Error.ShouldBe(ClientImportError.Empty);
        Parse(string.Empty).Error.ShouldBe(ClientImportError.Empty);
        _provider.Parse(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08, 0x00 }).Error.ShouldBe(ClientImportError.NotText);
        _provider.Parse(new byte[ClientImportLimits.MaxBytes + 1]).Error.ShouldBe(ClientImportError.TooLarge);
        var tooMany = new StringBuilder("Service\n");
        for (var i = 0; i <= ServiceImportLimits.MaxRows; i++)
        {
            tooMany.Append("Service ").Append(i).Append('\n');
        }

        Parse(tooMany.ToString()).Error.ShouldBe(ClientImportError.TooManyRows);
    }

    [Fact]
    public void Numbers_that_cannot_be_read_are_named_on_their_row()
    {
        var result = Parse("Service,Minutes,Price,Deposit\nCut,1h 30,forty,half\nColour,1:30,45.999,20\nShort row,45\n");

        result.IsSuccess.ShouldBeTrue();
        result.Rows[0].Problems.ShouldBe([ServiceImportProblem.InvalidMinutes, ServiceImportProblem.InvalidPrice, ServiceImportProblem.InvalidDeposit], ignoreOrder: true);
        (result.Rows[0].Minutes, result.Rows[0].Price, result.Rows[0].DepositPercent).ShouldBe((null, null, null));
        result.Rows[1].Problems.ShouldBe([ServiceImportProblem.InvalidMinutes]);
        result.Rows[2].Problems.ShouldBe([ServiceImportProblem.WrongColumnCount]);
    }

    [Fact]
    public void A_service_the_salon_does_not_have_is_new_and_needs_its_minutes_and_its_price()
    {
        var rows = Classify("Service,Minutes,Price\nCut,45,45\nNo time,,45\nNo price,45,\nNeither,,\n", Nothing);

        rows.Select(r => r.Status).ShouldBe([ServiceImportStatus.New, ServiceImportStatus.Invalid, ServiceImportStatus.Invalid, ServiceImportStatus.Invalid]);
        rows[0].Change.ShouldBeNull();
        rows[1].Problems.ShouldBe([ServiceImportProblem.MissingMinutes]);
        rows[2].Problems.ShouldBe([ServiceImportProblem.MissingPrice]);
        rows[3].Problems.ShouldBe([ServiceImportProblem.MissingMinutes, ServiceImportProblem.MissingPrice]);
    }

    [Fact]
    public void A_service_the_salon_has_changes_only_where_the_file_says_something_different()
    {
        var cut = new ExistingService(Guid.NewGuid(), "Cut", 45, 45m, null);
        var colour = new ExistingService(Guid.NewGuid(), "Colour", 90, 80m, 20m);
        var existing = Existing(cut, colour);

        // A salon with the cut only: the colour would be new and has no price; the second "Cut" is said twice.
        var rows = Classify("Service,Minutes,Price,Deposit\ncut,45,47.50,\n COLOUR ,120,,25\nCut,45,45.00,\n", Existing(cut));
        rows.Select(r => r.Status).ShouldBe([ServiceImportStatus.Changed, ServiceImportStatus.Invalid, ServiceImportStatus.Repeated]);
        rows[1].Problems.ShouldBe([ServiceImportProblem.MissingPrice]);

        rows = Classify("Service,Minutes,Price,Deposit\ncut,45,47.50,\n COLOUR ,120,,25\n", existing);
        rows.Select(r => r.Status).ShouldBe([ServiceImportStatus.Changed, ServiceImportStatus.Changed]);
        rows[0].Change.ShouldBe(new ServiceChange(cut.Id, 45, 45, 45m, 47.50m, null, null));
        (rows[0].Change!.MinutesChange, rows[0].Change!.PriceChanges, rows[0].Change!.DepositChanges).ShouldBe((false, true, false));
        rows[1].Change.ShouldBe(new ServiceChange(colour.Id, 90, 120, 80m, 80m, 20m, 25m), "an empty price leaves the price");
        (rows[1].Change!.MinutesChange, rows[1].Change!.PriceChanges, rows[1].Change!.DepositChanges).ShouldBe((true, false, true));

        // The same numbers, written differently, and empty cells: nothing changes.
        rows = Classify("Service,Minutes,Price,Deposit\nCut,45 min,£45.00,\nColour,,,20.0\n", existing);
        rows.Select(r => r.Status).ShouldBe([ServiceImportStatus.Unchanged, ServiceImportStatus.Unchanged]);
        rows.ShouldAllBe(r => r.Change != null && !r.Change.Any);
    }

    [Theory]
    [InlineData("0", ServiceImportProblem.InvalidMinutes)]
    [InlineData("3", ServiceImportProblem.InvalidMinutes)]
    [InlineData("47", ServiceImportProblem.InvalidMinutes)]
    [InlineData("605", ServiceImportProblem.InvalidMinutes)]
    [InlineData("45.5", ServiceImportProblem.InvalidMinutes)]
    public void The_time_is_whole_minutes_on_the_five_minute_step_within_what_a_visit_can_last(string minutes, ServiceImportProblem problem)
    {
        var row = Classify($"Service,Minutes,Price\nCut,{minutes},45\n", Nothing).ShouldHaveSingleItem();

        row.Status.ShouldBe(ServiceImportStatus.Invalid);
        row.Problems.ShouldBe([problem]);
    }

    [Fact]
    public void A_price_and_a_deposit_out_of_range_a_long_name_and_a_long_category_are_invalid()
    {
        var rows = Classify(
            "Service,Category,Minutes,Price,Deposit\n"
            + "Dear,,45,100000.01,\n"
            + "Greedy,,45,45,101\n"
            + $"{new string('n', 201)},,45,45,\n"
            + $"Long heading,{new string('c', 101)},45,45,\n"
            + ",,45,45,\n",
            Nothing);

        rows.ShouldAllBe(r => r.Status == ServiceImportStatus.Invalid);
        rows.Select(r => r.Problems.Single()).ShouldBe(
        [
            ServiceImportProblem.InvalidPrice, ServiceImportProblem.InvalidDeposit, ServiceImportProblem.NameTooLong,
            ServiceImportProblem.CategoryTooLong, ServiceImportProblem.MissingName,
        ]);
    }

    [Fact]
    public void A_name_said_twice_is_taken_once_and_an_invalid_row_does_not_reserve_its_name()
    {
        var rows = Classify("Service,Minutes,Price\nCut,47,45\nCut,45,45\ncut,60,50\n", Nothing);

        rows.Select(r => r.Status).ShouldBe([ServiceImportStatus.Invalid, ServiceImportStatus.New, ServiceImportStatus.Repeated]);
        rows[2].Problems.ShouldBe([ServiceImportProblem.RepeatedInFile]);
    }

    [Fact]
    public void A_service_with_waiting_time_inside_it_cannot_be_made_shorter_than_that_time()
    {
        // Colour: 120 minutes, of which the colour develops for 45 minutes starting after 30.
        var colour = new ExistingService(Guid.NewGuid(), "Colour", 120, 80m, null) { ProcessingMinutes = 45, ProcessingStartsAfterMinutes = 30 };

        Classify("Service,Minutes\nColour,75\n", Existing(colour)).ShouldHaveSingleItem().Status.ShouldBe(ServiceImportStatus.Changed, "30 and 45 fit in 75");
        var tooShort = Classify("Service,Minutes\nColour,60\n", Existing(colour)).ShouldHaveSingleItem();
        tooShort.Status.ShouldBe(ServiceImportStatus.Invalid);
        tooShort.Problems.ShouldBe([ServiceImportProblem.InvalidMinutes]);
    }

    private static Dictionary<string, ExistingService> Existing(params ExistingService[] services)
        => services.ToDictionary(s => ServiceImportClassifier.NameKey(s.Name), StringComparer.Ordinal);

    private IReadOnlyList<ClassifiedServiceRow> Classify(string csv, IReadOnlyDictionary<string, ExistingService> existing)
        => ServiceImportClassifier.Classify(Parse(csv).Rows, existing);

    private ServiceImportParseResult Parse(string csv) => _provider.Parse(Encoding.UTF8.GetBytes(csv));
}