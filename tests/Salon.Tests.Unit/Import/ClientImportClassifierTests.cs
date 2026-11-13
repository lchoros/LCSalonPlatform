using Salon.Core.Import;
using Salon.Services.Clients.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0019: the pure part of the preview. New, duplicate (against the salon or earlier rows of the file) or invalid, with
/// the same phone and email normalisation as the client form; invalid wins over duplicate.
/// </summary>
public class ClientImportClassifierTests
{
    private static readonly ExistingClients Nobody = new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    [Fact]
    public void A_valid_unknown_client_is_new_with_normalised_contact_details()
    {
        var rows = ClientImportClassifier.Classify([Row(2, "Olivia", "Brown", "07700 900123", "Olivia@Example.com")], "GBP", Nobody);

        var row = rows.ShouldHaveSingleItem();
        row.Status.ShouldBe(ImportRowStatus.New);
        row.Problems.ShouldBeEmpty();
        row.NormalisedPhone.ShouldBe("+447700900123");
        row.NormalisedEmail.ShouldBe("olivia@example.com");
    }

    [Fact]
    public void The_salon_country_resolves_local_numbers()
    {
        var rows = ClientImportClassifier.Classify([Row(2, "Anna", "Nowak", "601 234 567")], "PLN", Nobody);

        rows[0].Status.ShouldBe(ImportRowStatus.New);
        rows[0].NormalisedPhone.ShouldBe("+48601234567");
    }

    [Fact]
    public void A_local_number_without_a_country_is_invalid()
    {
        var rows = ClientImportClassifier.Classify([Row(2, "Anna", "Nowak", "601 234 567")], null, Nobody);

        rows[0].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[0].Problems.ShouldBe([ImportProblem.InvalidPhone]);
    }

    [Fact]
    public void Bad_phone_or_email_is_invalid()
    {
        var rows = ClientImportClassifier.Classify(
        [
            Row(2, "Jack", "Jones", "not a phone", "jack@example.com"),
            Row(3, "Jill", "Jones", "+44 7700 900999", "jack@@example"),
        ], "GBP", Nobody);

        rows[0].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[0].Problems.ShouldBe([ImportProblem.InvalidPhone]);
        rows[1].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[1].Problems.ShouldBe([ImportProblem.InvalidEmail]);
    }

    [Fact]
    public void Parser_problems_make_the_row_invalid_and_are_kept()
    {
        var rows = ClientImportClassifier.Classify([Row(5, "Madonna", "", "07700 900000", problems: [ImportProblem.MissingLastName])], "GBP", Nobody);

        rows[0].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[0].Problems.ShouldBe([ImportProblem.MissingLastName]);
    }

    [Fact]
    public void A_known_phone_or_email_is_a_duplicate()
    {
        var existing = new ExistingClients(
            new HashSet<string>(["+447700900123"], StringComparer.Ordinal),
            new HashSet<string>(["anna@example.com"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var rows = ClientImportClassifier.Classify(
        [
            Row(2, "Olivia", "Brown", "07700 900123", "new@example.com"),
            Row(3, "Anna", "Nowak", "07700 900999", "Anna@Example.com"),
            Row(4, "Fresh", "Face", "07700 900555"),
        ], "GBP", existing);

        rows[0].Status.ShouldBe(ImportRowStatus.Duplicate);
        rows[0].Problems.ShouldBe([ImportProblem.DuplicatePhone]);
        rows[1].Status.ShouldBe(ImportRowStatus.Duplicate);
        rows[1].Problems.ShouldBe([ImportProblem.DuplicateEmail]);
        rows[2].Status.ShouldBe(ImportRowStatus.New);
    }

    [Fact]
    public void Without_contact_details_the_name_decides()
    {
        var existing = new ExistingClients(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>([ExistingClients.NameKey("Olivia", "Brown")], StringComparer.Ordinal));

        var rows = ClientImportClassifier.Classify(
        [
            Row(2, "olivia", "BROWN"),
            Row(3, "Oliver", "Twist"),
            Row(4, "Olivia", "Brown", "07700 900123"),
        ], "GBP", existing);

        rows[0].Status.ShouldBe(ImportRowStatus.Duplicate);
        rows[0].Problems.ShouldBe([ImportProblem.DuplicateName]);
        rows[1].Status.ShouldBe(ImportRowStatus.New);
        rows[2].Status.ShouldBe(ImportRowStatus.New, "a phone is a better key than a name; the salon may have two Olivia Browns");
    }

    [Fact]
    public void Repeats_inside_the_file_are_duplicates_of_the_first_occurrence()
    {
        var rows = ClientImportClassifier.Classify(
        [
            Row(2, "Olivia", "Brown", "07700 900123"),
            Row(3, "Olivia", "Brown", "+44 7700 900123"),
            Row(4, "Anna", "Nowak", email: "Anna@Example.com"),
            Row(5, "Anna", "Nowak", email: "anna@example.com"),
            Row(6, "Oliver", "Twist"),
            Row(7, "oliver", "twist"),
        ], "GBP", Nobody);

        rows.Select(r => r.Status).ShouldBe([ImportRowStatus.New, ImportRowStatus.Duplicate, ImportRowStatus.New, ImportRowStatus.Duplicate, ImportRowStatus.New, ImportRowStatus.Duplicate]);
        rows[1].Problems.ShouldBe([ImportProblem.DuplicateInFile]);
        rows[3].Problems.ShouldBe([ImportProblem.DuplicateInFile]);
        rows[5].Problems.ShouldBe([ImportProblem.DuplicateInFile]);
    }

    [Fact]
    public void Invalid_wins_over_duplicate()
    {
        var existing = new ExistingClients(
            new HashSet<string>(["+447700900123"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var rows = ClientImportClassifier.Classify([Row(2, "Olivia", "Brown", "07700 900123", "bad@@email")], "GBP", existing);

        rows[0].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[0].Problems.ShouldBe([ImportProblem.InvalidEmail]);
    }

    [Fact]
    public void An_invalid_row_does_not_reserve_its_phone_for_later_rows()
    {
        var rows = ClientImportClassifier.Classify(
        [
            Row(2, "", "Brown", "07700 900123", problems: [ImportProblem.MissingFirstName]),
            Row(3, "Olivia", "Brown", "07700 900123"),
        ], "GBP", Nobody);

        rows[0].Status.ShouldBe(ImportRowStatus.Invalid);
        rows[1].Status.ShouldBe(ImportRowStatus.New);
    }

    [Fact]
    public void Order_and_source_rows_are_preserved()
    {
        var rows = ClientImportClassifier.Classify([Row(9, "A", "B", "+447700900001"), Row(2, "C", "D", "+447700900002")], "GBP", Nobody);

        rows.Select(r => r.Row.SourceRow).ShouldBe([9, 2]);
    }

    private static ImportedClientRow Row(int sourceRow, string first, string last, string? phone = null, string? email = null, IReadOnlyList<ImportProblem>? problems = null)
        => new(sourceRow, first, last, phone, email, null, false, problems ?? []);
}