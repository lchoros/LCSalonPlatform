using System.Text;
using Salon.Core.Import;
using Salon.Plugin.Import;
using Salon.Services.Clients.Import;
using Salon.Services.Products.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0045: a product list is read by its headings, in English or Polish and in any order, and every row is called
/// new, already there or invalid by the rules of the product form. Invalid wins over duplicate.
/// </summary>
public class ProductImportTests
{
    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private readonly CsvProductImportProvider _provider = new();

    [Fact]
    public void A_list_with_every_column_is_read_as_typed()
    {
        var result = Parse("Name,Brand,Code,Price,Stock,Low stock at\n Shampoo 250 ml ,Salon Range,SH-250,12.50,8,2\nColour tube 60 ml,,,,3,\n");

        result.IsSuccess.ShouldBeTrue();
        result.Rows.Count.ShouldBe(2);
        result.Rows[0].ShouldBe(new ImportedProductRow(2, "Shampoo 250 ml", "Salon Range", "SH-250", 12.50m, 8, 2, result.Rows[0].Problems));
        result.Rows[0].Problems.ShouldBeEmpty();
        var tube = result.Rows[1];
        (tube.SourceRow, tube.Name, tube.Brand, tube.Code, tube.Price, tube.Stock, tube.LowStockAt).ShouldBe((3, "Colour tube 60 ml", null, null, null, 3, null));
    }

    [Fact]
    public void Polish_headings_a_semicolon_and_a_decimal_comma_are_understood()
    {
        var result = Parse("Stan;Cena sprzedaży;Nazwa produktu;Marka\n4;45,90 zł;Odżywka 250 ml;Seria Salon\n");

        result.IsSuccess.ShouldBeTrue();
        var row = result.Rows.ShouldHaveSingleItem();
        (row.Name, row.Brand, row.Price, row.Stock).ShouldBe(("Odżywka 250 ml", "Seria Salon", 45.90m, 4));
    }

    [Fact]
    public void Only_a_name_column_is_needed_and_other_columns_are_ignored()
    {
        var result = Parse("Supplier ref,Product,Colour\nA1,Hair oil 50 ml,amber\n");

        result.IsSuccess.ShouldBeTrue();
        var row = result.Rows.ShouldHaveSingleItem();
        (row.Name, row.Price, row.Stock).ShouldBe(("Hair oil 50 ml", null, null));
    }

    [Fact]
    public void A_file_without_a_name_column_is_refused_and_says_what_it_found()
    {
        var result = Parse("Price,Stock\n12.50,3\n");

        result.Error.ShouldBe(ClientImportError.UnknownFormat);
        result.Header.ShouldBe(["Price", "Stock"]);
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_a_binary_and_a_too_long_file_are_refused()
    {
        Parse("Name,Price\n").Error.ShouldBe(ClientImportError.Empty);
        _provider.Parse(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00 }).Error.ShouldBe(ClientImportError.NotText);
        var tooLong = new StringBuilder("Name\n");
        for (var i = 0; i <= ProductImportLimits.MaxRows; i++)
        {
            tooLong.Append("Product ").Append(i).Append('\n');
        }

        Parse(tooLong.ToString()).Error.ShouldBe(ClientImportError.TooManyRows);
    }

    [Fact]
    public void Numbers_that_cannot_be_read_and_short_rows_are_named_on_the_row()
    {
        var result = Parse("Name,Price,Stock,Low stock at\nShampoo,twelve,a few,soon\nConditioner,9\n");

        result.IsSuccess.ShouldBeTrue();
        result.Rows[0].Problems.ShouldBe([ProductImportProblem.InvalidPrice, ProductImportProblem.InvalidStock, ProductImportProblem.InvalidLowStockAt]);
        result.Rows[1].Problems.ShouldBe([ProductImportProblem.WrongColumnCount]);
        result.Rows[1].Price.ShouldBe(9m);
    }

    [Fact]
    public void A_good_row_is_new()
    {
        var rows = ProductImportClassifier.Classify([Row(2, "Shampoo 250 ml", price: 12.50m, stock: 8)], Nothing);

        var row = rows.ShouldHaveSingleItem();
        row.Status.ShouldBe(ImportRowStatus.New);
        row.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_name_the_salon_has_is_a_duplicate_whatever_its_case_or_spacing()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { ProductImportClassifier.NameKey("Shampoo 250 ml") };

        var rows = ProductImportClassifier.Classify([Row(2, "  SHAMPOO 250 ML "), Row(3, "Conditioner 250 ml")], existing);

        rows[0].Status.ShouldBe(ImportRowStatus.Duplicate);
        rows[0].Problems.ShouldBe([ProductImportProblem.DuplicateName]);
        rows[1].Status.ShouldBe(ImportRowStatus.New);
    }

    [Fact]
    public void A_name_repeated_in_the_file_is_imported_once()
    {
        var rows = ProductImportClassifier.Classify([Row(2, "Shampoo"), Row(3, "shampoo"), Row(4, "Shampoo")], Nothing);

        rows.Select(r => r.Status).ShouldBe([ImportRowStatus.New, ImportRowStatus.Duplicate, ImportRowStatus.Duplicate]);
        rows[1].Problems.ShouldBe([ProductImportProblem.DuplicateInFile]);
    }

    [Fact]
    public void Every_rule_of_the_product_form_is_checked_and_invalid_wins_over_duplicate()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { ProductImportClassifier.NameKey("Shampoo") };
        ImportedProductRow[] file =
        [
            Row(2, " "),
            Row(3, new string('n', 151)),
            Row(4, "Brand too long") with { Brand = new string('b', 101) },
            Row(5, "Code too long") with { Code = new string('c', 51) },
            Row(6, "Three decimals", price: 1.999m),
            Row(7, "Too dear", price: 100_000.01m),
            Row(8, "Too many", stock: 1_000_001),
            Row(9, "Low mark too high") with { LowStockAt = 10_001 },
            Row(10, "Shampoo", price: 1.999m),
            Row(11, "Read wrong") with { Problems = [ProductImportProblem.InvalidStock] },
        ];

        var rows = ProductImportClassifier.Classify(file, existing);

        rows.ShouldAllBe(r => r.Status == ImportRowStatus.Invalid);
        rows.Select(r => r.Problems.Single()).ShouldBe(
        [
            ProductImportProblem.MissingName,
            ProductImportProblem.NameTooLong,
            ProductImportProblem.BrandTooLong,
            ProductImportProblem.CodeTooLong,
            ProductImportProblem.InvalidPrice,
            ProductImportProblem.InvalidPrice,
            ProductImportProblem.InvalidStock,
            ProductImportProblem.InvalidLowStockAt,
            ProductImportProblem.InvalidPrice,
            ProductImportProblem.InvalidStock,
        ]);
    }

    [Fact]
    public void An_invalid_row_does_not_take_its_name_from_a_good_one_further_down()
    {
        var rows = ProductImportClassifier.Classify([Row(2, "Shampoo", price: 1.999m), Row(3, "Shampoo", price: 12.50m)], Nothing);

        rows.Select(r => r.Status).ShouldBe([ImportRowStatus.Invalid, ImportRowStatus.New]);
    }

    private ProductImportParseResult Parse(string csv) => _provider.Parse(Encoding.UTF8.GetBytes(csv));

    private static ImportedProductRow Row(int sourceRow, string name, decimal? price = null, int? stock = null)
        => new(sourceRow, name, null, null, price, stock, null, []);
}