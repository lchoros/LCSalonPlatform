using System.Text;
using NodaTime;
using Salon.Core.Import;
using Salon.Plugin.Import;
using Salon.Services.Appointments.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0067: a diary file is read by its headings, from today on, and every row is matched to a client, a service and
/// a person the salon has, every rule failing closed. Anna and Tom take bookings, Kasia has left; Sarah Smith's card
/// has her phone, a second Jane Brown exists, and the salon offers a cut (45 min, £45) that Tom does not do.
/// </summary>
public class AppointmentImportTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = new(2027, 1, 11);
    private static readonly Instant Now = London.AtStrictly(Today + new LocalTime(9, 0)).ToInstant();

    private static readonly ImportClient Sarah = new(Guid.NewGuid(), "Sarah", "Smith", "+447700900111", "sarah@example.test");
    private static readonly ImportClient Olivia = new(Guid.NewGuid(), "Olivia", "Brown", "+447700900222", null);
    private static readonly ImportClient JaneOne = new(Guid.NewGuid(), "Jane", "Brown", null, null);
    private static readonly ImportClient JaneTwo = new(Guid.NewGuid(), "Jane", "Brown", null, null);
    private static readonly ImportClient Lucja = new(Guid.NewGuid(), "Łucja", "Kowalska", null, null);

    private static readonly ImportService Cut = new(Guid.NewGuid(), "Cut & blow-dry", true);
    private static readonly ImportService Colour = new(Guid.NewGuid(), "Colour", true);
    private static readonly ImportService Perm = new(Guid.NewGuid(), "Perm", false);

    private static readonly ImportStaff Anna = new(Guid.NewGuid(), "Anna Kowalska", true);
    private static readonly ImportStaff Tom = new(Guid.NewGuid(), "Tom", true);
    private static readonly ImportStaff Kasia = new(Guid.NewGuid(), "Kasia Nowak", false);

    private readonly CsvAppointmentImportProvider _provider = new();

    [Fact]
    public void A_file_with_every_column_is_read_as_typed_and_its_notes_are_never_kept()
    {
        var result = Parse("Date,Start time,End time,Client,Phone,Email,Service,Staff,Price,Status,Notes\n20/01/2027,14:30,15:15,Sarah Smith,07700 900111,sarah@example.test,Cut & blow-dry,Anna,47.50,Confirmed,allergic to PPD\n");

        result.IsSuccess.ShouldBeTrue();
        var row = result.Rows.ShouldHaveSingleItem();
        (row.SourceRow, row.Start, row.End, row.Minutes, row.ClientName, row.ClientPhone, row.ClientEmail, row.Service, row.Staff, row.Price, row.Status)
            .ShouldBe((2, new LocalDateTime(2027, 1, 20, 14, 30), new LocalDateTime(2027, 1, 20, 15, 15), (int?)null, "Sarah Smith", "07700 900111", "sarah@example.test", "Cut & blow-dry", "Anna", 47.50m, "Confirmed"));
        row.Problems.ShouldBeEmpty();
        row.ToString().ShouldNotContain("Sarah", Case.Insensitive, "a row never puts a name in a log");
        typeof(ImportedAppointmentRow).GetProperties().Select(p => p.Name).ShouldNotContain("Notes");
    }

    [Fact]
    public void Polish_headings_one_date_and_time_column_and_first_and_last_name_are_understood()
    {
        var result = Parse("Termin;Imię;Nazwisko;Usługa;Pracownik;Czas trwania;Cena\n20.01.2027 10:00;Łucja;Kowalska;Koloryzacja;Anna;1:30;120,50 zł\n");

        var row = result.Rows.ShouldHaveSingleItem();
        (row.Start, row.ClientName, row.Service, row.Minutes, row.Price).ShouldBe((new LocalDateTime(2027, 1, 20, 10, 0), "Łucja Kowalska", "Koloryzacja", (int?)90, (decimal?)120.50m));
    }

    [Fact]
    public void Freshas_booking_columns_are_understood()
    {
        var result = Parse("Scheduled date,Start time,End time,Duration,Client,Team member,Service,Status\n2027-01-20,09:15,10:00,45,Olivia Brown,Tom,Cut & blow-dry,Booked\n");

        var row = result.Rows.ShouldHaveSingleItem();
        (row.Start, row.End, row.Minutes, row.Staff, row.Status).ShouldBe((new LocalDateTime(2027, 1, 20, 9, 15), new LocalDateTime(2027, 1, 20, 10, 0), (int?)45, "Tom", "Booked"));
    }

    [Fact]
    public void History_is_counted_not_kept_so_years_of_it_do_not_refuse_the_file()
    {
        var csv = new StringBuilder("Date,Time,Client,Service\n");
        for (var day = 0; day < 3_000; day++)
        {
            csv.Append(new LocalDate(2018, 1, 1).PlusDays(day).ToString("dd/MM/yyyy", null)).Append(",10:00,Sarah Smith,Colour\n");
        }

        csv.Append("12/01/2027,10:00,Sarah Smith,Colour\n");

        var result = Parse(csv.ToString());

        result.IsSuccess.ShouldBeTrue();
        result.Rows.ShouldHaveSingleItem().Start.ShouldBe(new LocalDateTime(2027, 1, 12, 10, 0));
        result.PastCount.ShouldBe(3_000);
        result.PastDays.ShouldNotBeNull().ShouldBe([new LocalDate(2018, 1, 1), new LocalDate(2018, 1, 2), new LocalDate(2018, 1, 3), new LocalDate(2018, 1, 4), new LocalDate(2018, 1, 5)]);
    }

    [Fact]
    public void More_visits_from_today_on_than_one_file_may_hold_are_refused_but_cancelled_ones_do_not_count()
    {
        var csv = new StringBuilder("Date,Time,Client,Service,Status\n");
        for (var i = 0; i < AppointmentImportLimits.MaxRows; i++)
        {
            csv.Append("20/01/2027,10:00,Sarah Smith,Colour,Booked\n");
        }

        csv.Append("20/01/2027,11:00,Sarah Smith,Colour,Cancelled\n");
        Parse(csv.ToString()).IsSuccess.ShouldBeTrue();

        csv.Append("20/01/2027,12:00,Sarah Smith,Colour,Booked\n");
        var refused = Parse(csv.ToString());
        (refused.FileError, refused.FutureCount).ShouldBe((AppointmentImportFileError.TooManyFutureRows, AppointmentImportLimits.MaxRows + 1));
    }

    [Fact]
    public void A_file_that_names_no_date_time_service_or_client_is_refused_and_says_which()
    {
        var result = Parse("Booking ref,Who,Notes\nA1,Sarah,hello\n");

        result.FileError.ShouldBe(AppointmentImportFileError.MissingColumns);
        result.Missing.ShouldBe(["Date", "Time", "Service", "Client"], "\"Who\" is not a heading the reader knows");
        result.Header.ShouldBe(["Booking ref", "Who", "Notes"]);

        Parse("Date,Time,Service,Client\n").Error.ShouldBe(ClientImportError.Empty);
        _provider.Parse(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08, 0x00 }, Today).Error.ShouldBe(ClientImportError.NotText);
        _provider.Parse(new byte[AppointmentImportLimits.MaxBytes + 1], Today).Error.ShouldBe(ClientImportError.TooLarge);
    }

    [Fact]
    public void One_date_that_can_only_be_month_first_refuses_the_whole_file()
    {
        // The first row reads as 2 December either way; only the second shows the file is month-first.
        var result = Parse("Date,Time,Client,Service\n12/02/2027,10:00,Sarah Smith,Colour\n01/25/2027,10:00,Sarah Smith,Colour\n");

        result.FileError.ShouldBe(AppointmentImportFileError.MonthFirstDates);
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Dates_times_lengths_and_prices_that_cannot_be_read_are_named_on_their_row()
    {
        var rows = Parse("Date,Time,End,Duration,Client,Service,Price\n31/02/2027,10:00,,,Sarah Smith,Colour,\n20/01/2027,25:00,,,Sarah Smith,Colour,\n20/01/2027,10:00,soon,,Sarah Smith,Colour,\n20/01/2027,10:00,,an hour,Sarah Smith,Colour,forty\n20/01/2027,10:00:30,,,Sarah Smith,Colour,\n").Rows;

        rows[0].Problems.ShouldBe([AppointmentImportProblem.InvalidDate]);
        rows[1].Problems.ShouldBe([AppointmentImportProblem.InvalidTime]);
        rows[2].Problems.ShouldBe([AppointmentImportProblem.InvalidLength]);
        rows[3].Problems.ShouldBe([AppointmentImportProblem.InvalidLength, AppointmentImportProblem.InvalidPrice]);
        rows[4].Problems.ShouldBe([AppointmentImportProblem.InvalidTime], "a diary is kept to the minute");
    }

    [Theory]
    [InlineData(null, OldStatus.Booked)]
    [InlineData("", OldStatus.Booked)]
    [InlineData("Booked", OldStatus.Booked)]
    [InlineData("Confirmed", OldStatus.Booked)]
    [InlineData("Umówiona", OldStatus.Booked)]
    [InlineData("potwierdzona", OldStatus.Booked)]
    [InlineData("Cancelled", OldStatus.Gone)]
    [InlineData("Canceled by client", OldStatus.Gone)]
    [InlineData("No-show", OldStatus.Gone)]
    [InlineData("DNA", OldStatus.Gone)]
    [InlineData("Odwołana", OldStatus.Gone)]
    [InlineData("Nieobecność", OldStatus.Gone)]
    [InlineData("Unconfirmed", OldStatus.Unknown)]
    [InlineData("Pending", OldStatus.Unknown)]
    [InlineData("Awaiting confirmation", OldStatus.Unknown)]
    [InlineData("Oczekująca", OldStatus.Unknown)]
    [InlineData("Inactive", OldStatus.Unknown)]
    [InlineData("Completed", OldStatus.Unknown)]
    [InlineData("Adnan", OldStatus.Unknown)]
    public void A_status_word_is_booked_cancelled_or_not_guessed(string? word, OldStatus reading)
    {
        AppointmentImportStatuses.Read(word).ShouldBe(reading);
    }

    [Fact]
    public void A_row_that_matches_a_client_a_service_and_a_person_is_new_with_the_files_length_and_price()
    {
        var row = Classify("Date,Time,End,Phone,Client,Service,Staff,Price\n20/01/2027,14:30,15:15,07700 900111,Sarah Smith,cut & blow dry,anna kowalska,47.50\n").ShouldHaveSingleItem();

        row.Status.ShouldBe(AppointmentImportStatus.New);
        var m = row.Match;
        (m.ClientId, m.ClientMatchedBy, m.ServiceId, m.StaffId, m.StaffByFirstName, m.Minutes, m.Price).ShouldBe((Sarah.Id, ClientMatch.Phone, Cut.Id, Anna.Id, false, 45, 47.50m));
        m.Start.ShouldBe(London.AtStrictly(new LocalDateTime(2027, 1, 20, 14, 30)).ToInstant());
        m.End.ShouldBe(m.Start!.Value + Duration.FromMinutes(45));
    }

    [Fact]
    public void Without_a_length_or_a_price_the_persons_own_are_used_and_a_salon_of_one_needs_no_staff_column()
    {
        var row = Classify("Date,Time,Client,Service,Staff\n20/01/2027,10:00,Olivia Brown,Cut & blow-dry,Anna\n").ShouldHaveSingleItem();
        (row.Match.Minutes, row.Match.Price).ShouldBe((45, 45m));

        var alone = Classify("Date,Time,Client,Service\n20/01/2027,10:00,Olivia Brown,Colour\n", Context(staff: [Anna, Kasia])).ShouldHaveSingleItem();
        (alone.Status, alone.Match.StaffId).ShouldBe((AppointmentImportStatus.New, Anna.Id));

        Classify("Date,Time,Client,Service\n20/01/2027,10:00,Olivia Brown,Colour\n").ShouldHaveSingleItem().Problems.ShouldBe([AppointmentImportProblem.MissingStaff]);
    }

    [Fact]
    public void A_client_is_found_by_phone_then_email_then_a_unique_full_name_in_either_order()
    {
        var rows = Classify(
            "Date,Time,Client,Phone,Email,Service,Staff\n" +
            "20/01/2027,10:00,Sarah,+44 7700 900111,,Colour,Anna\n" +
            "20/01/2027,12:00,,,SARAH@example.test,Colour,Anna\n" +
            "20/01/2027,14:00,Brown Olivia,,,Colour,Anna\n" +
            "20/01/2027,16:00,lucja kowalska,,,Colour,Anna\n");

        rows.Select(r => (r.Status, r.Match.ClientId, r.Match.ClientMatchedBy)).ShouldBe(
        [
            (AppointmentImportStatus.New, Sarah.Id, ClientMatch.Phone),
            (AppointmentImportStatus.New, Sarah.Id, ClientMatch.Email),
            (AppointmentImportStatus.New, Olivia.Id, ClientMatch.Name),
            (AppointmentImportStatus.New, Lucja.Id, ClientMatch.Name),
        ]);
    }

    [Fact]
    public void Client_matching_fails_closed()
    {
        var rows = Classify(
            "Date,Time,Client,Phone,Email,Service,Staff\n" +
            // A number on no card is not tried as a name, though Sarah Smith exists.
            "20/01/2027,10:00,Sarah Smith,07700 900999,,Colour,Anna\n" +
            // Sarah's phone with Olivia's email.
            "20/01/2027,11:00,Sarah Smith,07700 900111,olivia@example.test,Colour,Anna\n" +
            // Sarah's phone, her daughter's visit.
            "20/01/2027,12:00,Lily Smith,07700 900111,,Colour,Anna\n" +
            // Two Jane Browns and nothing to tell them apart.
            "20/01/2027,13:00,Jane Brown,,,Colour,Anna\n" +
            // Nobody of that name.
            "20/01/2027,14:00,Mia Clark,,,Colour,Anna\n" +
            // Nothing at all.
            "20/01/2027,15:00,,,,Colour,Anna\n",
            Context(clients: [Sarah, Olivia with { Email = "olivia@example.test" }, JaneOne, JaneTwo, Lucja]));

        rows.ShouldAllBe(r => r.Status == AppointmentImportStatus.Invalid);
        rows.Select(r => r.Problems.Single()).ShouldBe(
        [
            AppointmentImportProblem.UnknownClient, AppointmentImportProblem.ContactsDisagree, AppointmentImportProblem.ContactBelongsToOther,
            AppointmentImportProblem.AmbiguousClient, AppointmentImportProblem.UnknownClient, AppointmentImportProblem.MissingClient,
        ]);
        rows[2].Match.ClientName.ShouldBe("Sarah Smith", "the preview says whose number it is");
        rows[2].Match.ClientId.ShouldBeNull();
    }

    [Fact]
    public void A_number_that_cannot_be_read_is_no_number_and_the_name_decides()
    {
        var row = Classify("Date,Time,Client,Phone,Service,Staff\n20/01/2027,10:00,Olivia Brown,call her,Colour,Anna\n").ShouldHaveSingleItem();

        (row.Status, row.Match.ClientId, row.Match.ClientMatchedBy).ShouldBe((AppointmentImportStatus.New, Olivia.Id, ClientMatch.Name));
    }

    [Fact]
    public void Services_and_people_are_found_by_name_and_a_leaver_is_never_found_in_a_colleague()
    {
        var rows = Classify(
            "Date,Time,Client,Service,Staff\n" +
            "20/01/2027,10:00,Olivia Brown,Highlights,Anna\n" +
            "20/01/2027,10:30,Olivia Brown,Perm,Anna\n" +
            "20/01/2027,11:00,Olivia Brown,,Anna\n" +
            "20/01/2027,11:30,Olivia Brown,Colour,Anna K.\n" +
            "20/01/2027,12:00,Olivia Brown,Colour,Kasia Nowak\n" +
            "20/01/2027,12:30,Olivia Brown,Colour,Kasia\n" +
            "20/01/2027,13:00,Olivia Brown,Colour,Becky\n" +
            "20/01/2027,13:30,Olivia Brown,Cut & blow-dry,Tom\n",
            Context(staff: [Anna, Tom, Kasia, new ImportStaff(Guid.NewGuid(), "Kasia Wiśniewska", true)]));

        rows[0].Problems.ShouldBe([AppointmentImportProblem.UnknownService]);
        rows[1].Problems.ShouldBe([AppointmentImportProblem.ServiceNotInUse]);
        rows[2].Problems.ShouldBe([AppointmentImportProblem.MissingService]);
        (rows[3].Status, rows[3].Match.StaffId, rows[3].Match.StaffByFirstName).ShouldBe((AppointmentImportStatus.New, Anna.Id, true));
        rows[4].Problems.ShouldBe([AppointmentImportProblem.StaffNotBookable], "the leaver by her full name");
        rows[5].Problems.ShouldBe([AppointmentImportProblem.AmbiguousStaff], "two Kasias, one of whom left: never guessed");
        rows[6].Problems.ShouldBe([AppointmentImportProblem.UnknownStaff]);
        rows[7].Problems.ShouldBe([AppointmentImportProblem.StaffDoesNotOffer]);
    }

    [Fact]
    public void Lengths_that_are_wrong_or_disagree_and_prices_out_of_range_are_invalid()
    {
        var rows = Classify(
            "Date,Time,End,Duration,Client,Service,Staff,Price\n" +
            "20/01/2027,10:00,09:00,,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,,0,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,,601,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,11:00,30,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,11:00,61,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,,,Olivia Brown,Colour,Anna,100000.01\n");

        rows.Select(r => r.Problems.SingleOrDefault()).ShouldBe(
        [
            AppointmentImportProblem.InvalidLength, AppointmentImportProblem.InvalidLength, AppointmentImportProblem.InvalidLength,
            AppointmentImportProblem.LengthsDisagree, default, AppointmentImportProblem.InvalidPrice,
        ]);
        rows[4].Match.Minutes.ShouldBe(60, "an end and a length a minute apart agree; the end decides");
    }

    [Fact]
    public void A_time_the_clocks_skip_or_pass_twice_is_not_guessed()
    {
        var rows = Classify("Date,Time,Client,Service,Staff\n28/03/2027,01:30,Olivia Brown,Colour,Anna\n31/10/2027,01:30,Olivia Brown,Colour,Anna\n");

        rows.Select(r => r.Problems.Single()).ShouldBe([AppointmentImportProblem.TimeSkipped, AppointmentImportProblem.TimeTwice]);
    }

    [Fact]
    public void Earlier_today_is_past_and_cancelled_rows_are_left_out_or_named_when_brought_over_before()
    {
        var before = new ImportedVisit(Guid.NewGuid(), Olivia.Id, Colour.Id, new LocalDate(2027, 1, 20), Active: true);
        var rows = Classify(
            "Date,Time,Client,Service,Staff,Status\n" +
            "11/01/2027,08:00,Olivia Brown,Colour,Anna,\n" +
            "20/01/2027,10:00,Olivia Brown,Colour,Anna,Cancelled\n" +
            "21/01/2027,10:00,Olivia Brown,Colour,Anna,Cancelled\n" +
            "21/01/2027,11:00,Somebody Else,Unknown thing,Nobody,No-show\n" +
            "22/01/2027,10:00,Olivia Brown,Colour,Anna,Pending\n",
            Context(imported: [before]));

        rows.Select(r => r.Status).ShouldBe(
        [
            AppointmentImportStatus.Past, AppointmentImportStatus.CancelledSince, AppointmentImportStatus.Cancelled,
            AppointmentImportStatus.Cancelled, AppointmentImportStatus.Invalid,
        ]);
        rows[1].Match.ExistingAppointmentId.ShouldBe(before.Id);
        rows[3].Problems.ShouldBeEmpty("a cancelled row is not written, so what it names does not matter");
        rows[4].Problems.ShouldBe([AppointmentImportProblem.UnknownStatus]);
    }

    [Fact]
    public void A_visit_brought_over_before_is_never_brought_over_again_whatever_became_of_it()
    {
        // Brought over by the last file and since cancelled here by the client; another moved to 15:00 with Tom.
        var cancelled = new ImportedVisit(Guid.NewGuid(), Olivia.Id, Colour.Id, new LocalDate(2027, 1, 20), Active: false);
        var moved = new ImportedVisit(Guid.NewGuid(), Sarah.Id, Colour.Id, new LocalDate(2027, 1, 21), Active: true);
        var inDiary = new DiaryVisit(Guid.NewGuid(), Anna.Id, Lucja.Id, At(2027, 1, 22, 10, 0), At(2027, 1, 22, 11, 0));

        var rows = Classify(
            "Date,Time,Client,Service,Staff\n" +
            "20/01/2027,10:00,Olivia Brown,Colour,Anna\n" +
            "21/01/2027,10:00,Sarah Smith,Colour,Anna\n" +
            "22/01/2027,10:00,Łucja Kowalska,Colour,Anna\n",
            Context(imported: [cancelled, moved], diary: [inDiary]));

        rows.ShouldAllBe(r => r.Status == AppointmentImportStatus.AlreadyThere);
        rows.Select(r => r.Match.ExistingAppointmentId).ShouldBe([cancelled.Id, moved.Id, inDiary.Id]);
    }

    [Fact]
    public void A_visit_that_overlaps_the_diary_an_earlier_row_or_a_held_time_is_invalid_and_back_to_back_is_not()
    {
        var diary = new DiaryVisit(Guid.NewGuid(), Anna.Id, Sarah.Id, At(2027, 1, 20, 10, 0), At(2027, 1, 20, 11, 0));
        var held = new HeldSlot(Anna.Id, At(2027, 1, 20, 15, 0), At(2027, 1, 20, 15, 45));

        var rows = Classify(
            "Date,Time,Duration,Client,Service,Staff\n" +
            "20/01/2027,10:30,30,Olivia Brown,Colour,Anna\n" +
            "20/01/2027,11:00,60,Olivia Brown,Colour,Anna\n" +
            "20/01/2027,11:30,30,Łucja Kowalska,Colour,Anna\n" +
            "20/01/2027,11:30,30,Łucja Kowalska,Colour,Tom\n" +
            "20/01/2027,15:30,30,Olivia Brown,Cut & blow-dry,Anna\n",
            Context(diary: [diary], held: [held]));

        rows.Select(r => (r.Status, r.Problems.SingleOrDefault())).ShouldBe(
        [
            (AppointmentImportStatus.Invalid, AppointmentImportProblem.ClashesWithDiary),
            (AppointmentImportStatus.New, default),
            (AppointmentImportStatus.Invalid, AppointmentImportProblem.ClashesInFile),
            (AppointmentImportStatus.New, default),
            (AppointmentImportStatus.Invalid, AppointmentImportProblem.ClashesWithHeldTime),
        ]);
    }

    [Theory]
    [InlineData("Sarah", true)]
    [InlineData("sarah smith", true)]
    [InlineData("Smith Sarah", true)]
    [InlineData("Sarah Brown", true)]
    [InlineData("", true)]
    [InlineData("Lily Smith", false)]
    [InlineData("Smith", false)]
    public void A_contact_is_the_cards_person_when_the_file_names_the_cards_first_name(string written, bool same)
    {
        ImportNames.SamePerson(written, "Sarah").ShouldBe(same);
    }

    [Fact]
    public void Names_compare_without_case_accents_or_punctuation()
    {
        ImportNames.Key("  Łucja   KOWALSKA ").ShouldBe("lucja kowalska");
        ImportNames.Key("Cut & Blow-Dry").ShouldBe(ImportNames.Key("cut & blow dry"));
        ImportNames.Loose("Blow-dry").ShouldBe("blowdry");
        ImportNames.FullName("Kowalska Łucja", "Łucja", "Kowalska").ShouldBeTrue();
        ImportNames.FullName("Łucja", "Łucja", "Kowalska").ShouldBeFalse();
    }

    private static Instant At(int year, int month, int day, int hour, int minute) => London.AtStrictly(new LocalDateTime(year, month, day, hour, minute)).ToInstant();

    private AppointmentImportParseResult Parse(string csv) => _provider.Parse(Encoding.UTF8.GetBytes(csv), Today);

    private IReadOnlyList<ClassifiedAppointmentRow> Classify(string csv, AppointmentImportContext? context = null)
    {
        var parsed = Parse(csv);
        parsed.IsSuccess.ShouldBeTrue(parsed.FileError?.ToString() ?? parsed.Error?.ToString());
        return AppointmentImportClassifier.Classify(parsed.Rows, context ?? Context());
    }

    private static AppointmentImportContext Context(
        IReadOnlyList<ImportClient>? clients = null,
        IReadOnlyList<ImportStaff>? staff = null,
        IReadOnlyList<DiaryVisit>? diary = null,
        IReadOnlyList<ImportedVisit>? imported = null,
        IReadOnlyList<HeldSlot>? held = null)
    {
        staff ??= [Anna, Tom, Kasia];
        var offerings = new Dictionary<(Guid, Guid), ImportOffering>();
        foreach (var person in staff.Where(s => s.Bookable))
        {
            offerings[(Cut.Id, person.Id)] = new ImportOffering(person.Id != Tom.Id, 45, 45m);
            offerings[(Colour.Id, person.Id)] = new ImportOffering(true, 90, 80m);
        }

        return new AppointmentImportContext(
            London,
            Now,
            "GBP",
            clients ?? [Sarah, Olivia, JaneOne, Lucja],
            [Cut, Colour, Perm],
            staff,
            offerings,
            diary ?? [],
            imported ?? [],
            held ?? []);
    }
}