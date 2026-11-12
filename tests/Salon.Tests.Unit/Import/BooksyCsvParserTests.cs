using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0019: the assumed Booksy Biz customer export. Values stay as typed; only trimming and the two name problems happen here.</summary>
public class BooksyCsvParserTests
{
    private static readonly string[] FixtureHeader =
    [
        "First Name", "Last Name", "Phone Number", "Email", "Birthday", "Gender", "Address", "City", "Zip Code", "Customer notes", "Accepts Marketing",
    ];

    private readonly BooksyCsvParser _parser = new();

    [Fact]
    public void Parses_the_sample_export()
    {
        CsvDecoder.TryDecode(ImportFixtures.Read(ImportFixtures.Booksy), out var text).ShouldBeTrue();
        var document = CsvReader.Read(text);
        document.Header.ShouldBe(FixtureHeader);

        var rows = _parser.Parse(document.Header, document.Records);

        rows.Count.ShouldBe(5);

        var olivia = rows[0];
        olivia.SourceRow.ShouldBe(2);
        olivia.FirstName.ShouldBe("Olivia");
        olivia.LastName.ShouldBe("Brown");
        olivia.Phone.ShouldBe("07700 900123");
        olivia.Email.ShouldBe("Olivia.Brown@Example.com");
        olivia.Notes.ShouldBe("Prefers afternoons; says \"no fringe\"");
        olivia.MarketingConsent.ShouldBeTrue();
        olivia.Problems.ShouldBeEmpty();

        var zoe = rows[1];
        zoe.FirstName.ShouldBe("Zoë");
        zoe.LastName.ShouldBe("O'Neill");
        zoe.Phone.ShouldBe("+44 7700 900456");
        zoe.Email.ShouldBeNull();
        zoe.Notes.ShouldBeNull();
        zoe.MarketingConsent.ShouldBeFalse();
        zoe.Problems.ShouldBeEmpty();

        rows[2].MarketingConsent.ShouldBeTrue("'yes' in lower case is still consent");
        rows[2].Notes.ShouldBe("Regular colour client");

        var madonna = rows[3];
        madonna.SourceRow.ShouldBe(5);
        madonna.FirstName.ShouldBe("Madonna");
        madonna.LastName.ShouldBe(string.Empty);
        madonna.Problems.ShouldBe([ImportProblem.MissingLastName]);

        var jack = rows[4];
        jack.Phone.ShouldBe("not a phone");
        jack.Email.ShouldBe("jack@@example");
        jack.Problems.ShouldBeEmpty("phone and email validity need the salon's country; the classifier decides");
    }

    [Fact]
    public void Matches_the_export_header_and_not_an_unrelated_file()
    {
        _parser.Match(FixtureHeader).ShouldBeGreaterThan(0);
        _parser.Match(["Foo", "Bar"]).ShouldBe(0);
        _parser.Match(["First Name", "Last Name", "Birthday"]).ShouldBe(0, "a name without any contact column is not a client export");
        _parser.Match(["Phone", "Email"]).ShouldBe(0, "a contact without a name is not a client export");
        _parser.Match([]).ShouldBe(0);
    }

    [Theory]
    [InlineData("first_name", "last_name", "phone_number", "email_address")]
    [InlineData("FIRST NAME", "LAST NAME", "PHONE", "EMAIL")]
    [InlineData(" First-Name ", "Last-Name", "Mobile", "E-Mail")]
    [InlineData("Imię", "Nazwisko", "Numer telefonu", "Adres e-mail")]
    [InlineData("Imie", "Nazwisko", "Telefon", "E-mail")]
    public void Header_matching_ignores_case_accents_punctuation_and_language(string first, string last, string phone, string email)
    {
        var header = new[] { first, last, phone, email };

        _parser.Match(header).ShouldBe(4);
        var row = _parser.Parse(header, [new ImportRecord(2, ["Anna", "Nowak", "601 234 567", "anna@example.pl"])]).ShouldHaveSingleItem();
        row.FirstName.ShouldBe("Anna");
        row.LastName.ShouldBe("Nowak");
        row.Phone.ShouldBe("601 234 567");
        row.Email.ShouldBe("anna@example.pl");
    }

    [Fact]
    public void A_single_name_column_is_split_at_the_first_space()
    {
        string[] header = ["Customer", "Phone"];

        var rows = _parser.Parse(header,
        [
            new ImportRecord(2, ["Olivia Rose Brown", "07700 900123"]),
            new ImportRecord(3, ["  Madonna ", "07700 900000"]),
        ]);

        rows[0].FirstName.ShouldBe("Olivia");
        rows[0].LastName.ShouldBe("Rose Brown");
        rows[0].Problems.ShouldBeEmpty();
        rows[1].FirstName.ShouldBe("Madonna");
        rows[1].LastName.ShouldBe(string.Empty);
        rows[1].Problems.ShouldBe([ImportProblem.MissingLastName]);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("yes", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("tak", true)]
    [InlineData("Y", true)]
    [InlineData("x", true)]
    [InlineData("No", false)]
    [InlineData("nie", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData("maybe", false)]
    public void Consent_is_true_only_for_a_clear_yes(string value, bool expected)
    {
        string[] header = ["First Name", "Last Name", "Phone", "Marketing"];

        var row = _parser.Parse(header, [new ImportRecord(2, ["A", "B", "1", value])]).ShouldHaveSingleItem();

        row.MarketingConsent.ShouldBe(expected);
    }

    [Fact]
    public void Missing_names_are_reported_per_row_and_blank_values_become_null()
    {
        string[] header = ["First Name", "Last Name", "Phone", "Email", "Notes"];

        var rows = _parser.Parse(header,
        [
            new ImportRecord(2, ["", "", "", "  ", ""]),
            new ImportRecord(3, [" Anna ", " Nowak ", " +48 601 234 567 ", "", "  likes tea  "]),
        ]);

        rows[0].Problems.ShouldBe([ImportProblem.MissingFirstName, ImportProblem.MissingLastName], ignoreOrder: true);
        rows[0].Phone.ShouldBeNull();
        rows[0].Email.ShouldBeNull();
        rows[0].Notes.ShouldBeNull();
        rows[1].FirstName.ShouldBe("Anna");
        rows[1].LastName.ShouldBe("Nowak");
        rows[1].Phone.ShouldBe("+48 601 234 567");
        rows[1].Email.ShouldBeNull();
        rows[1].Notes.ShouldBe("likes tea");
        rows[1].Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_name_longer_than_the_column_is_a_problem()
    {
        string[] header = ["First Name", "Last Name", "Phone"];

        var row = _parser.Parse(header, [new ImportRecord(2, [new string('a', 101), "Nowak", "1"])]).ShouldHaveSingleItem();

        row.Problems.ShouldBe([ImportProblem.NameTooLong]);
    }

    [Fact]
    public void Ragged_records_are_flagged_and_still_mapped_as_far_as_possible()
    {
        string[] header = ["First Name", "Last Name", "Phone", "Email"];

        var rows = _parser.Parse(header,
        [
            new ImportRecord(2, ["Olivia", "Brown", "07700 900123"]),
            new ImportRecord(3, ["Amelia", "Taylor", "07700 900789", "amelia@example.com", "extra"]),
        ]);

        rows[0].Problems.ShouldBe([ImportProblem.WrongColumnCount]);
        rows[0].FirstName.ShouldBe("Olivia");
        rows[0].Phone.ShouldBe("07700 900123");
        rows[0].Email.ShouldBeNull();
        rows[1].Problems.ShouldBe([ImportProblem.WrongColumnCount]);
        rows[1].Email.ShouldBe("amelia@example.com");
    }

    [Fact]
    public void Unknown_columns_are_ignored()
    {
        string[] header = ["Loyalty points", "First Name", "Total spent", "Last Name", "Phone", "Last visit"];

        var row = _parser.Parse(header, [new ImportRecord(2, ["120", "Anna", "£340", "Nowak", "07700 900123", "2026-10-01"])]).ShouldHaveSingleItem();

        row.FirstName.ShouldBe("Anna");
        row.LastName.ShouldBe("Nowak");
        row.Phone.ShouldBe("07700 900123");
        row.Notes.ShouldBeNull();
    }

    [Fact]
    public void The_format_name_is_stable()
    {
        _parser.Format.ShouldBe("Booksy");
    }
}
