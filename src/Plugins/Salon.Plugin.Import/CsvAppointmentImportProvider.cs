using NodaTime;
using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Plugin.Import;

/// <summary>
/// Reads the coming weeks of a salon's diary from a CSV file (ADR 0067): refuses an oversized or binary file,
/// decodes, splits with the row cap and finds the columns by their headings, in English or Polish and in any order,
/// the names Fresha gives its bookings among them. A file needs a date and a start time (or both in one column), a
/// service and something that names the client. The dates are read day-first or ISO; a file with a date that can
/// only be month-first is refused whole. A notes column is never read: it can hold health details. Pure.
/// </summary>
public sealed class CsvAppointmentImportProvider : IAppointmentImportProvider
{
    private static readonly HashSet<string> DateAliases = HeaderKey.Set(
        "Date", "Day", "Appointment date", "Booking date", "Visit date", "Scheduled date", "Data", "Data wizyty", "Dzień", "Dzien");

    private static readonly HashSet<string> TimeAliases = HeaderKey.Set(
        "Time", "Start", "Start time", "Starts", "From", "Appointment time", "Scheduled time", "Godzina", "Godzina rozpoczęcia", "Początek", "Od", "Rozpoczęcie");

    private static readonly HashSet<string> DateTimeAliases = HeaderKey.Set(
        "Date and time", "Start date", "Start date and time", "Starts at", "Appointment", "Data i godzina", "Data i godzina rozpoczęcia", "Termin", "Termin wizyty");

    private static readonly HashSet<string> EndAliases = HeaderKey.Set(
        "End", "End time", "Ends", "Finish", "Finish time", "Until", "To", "Koniec", "Godzina zakończenia", "Data i godzina zakończenia", "Do", "Zakończenie");

    private static readonly HashSet<string> MinutesAliases = HeaderKey.Set(
        "Duration", "Length", "Minutes", "Mins", "Duration minutes", "Duration min", "Czas", "Czas trwania", "Minuty", "Długość");

    private static readonly HashSet<string> ClientAliases = HeaderKey.Set(
        "Client", "Client name", "Customer", "Customer name", "Name", "Full name", "Klient", "Klientka", "Imię i nazwisko", "Nazwa klienta", "Klient imię i nazwisko");

    private static readonly HashSet<string> FirstNameAliases = HeaderKey.Set(
        "First name", "Forename", "Given name", "Client first name", "Imię", "Imie");

    private static readonly HashSet<string> LastNameAliases = HeaderKey.Set(
        "Last name", "Surname", "Family name", "Client last name", "Nazwisko");

    private static readonly HashSet<string> PhoneAliases = HeaderKey.Set(
        "Phone", "Phone number", "Mobile", "Mobile number", "Mobile phone", "Telephone", "Tel", "Client phone", "Telefon", "Numer telefonu", "Telefon komórkowy", "Komórka");

    private static readonly HashSet<string> EmailAliases = HeaderKey.Set(
        "Email", "E-mail", "Email address", "Client email", "Adres email", "Adres e-mail");

    private static readonly HashSet<string> ServiceAliases = HeaderKey.Set(
        "Service", "Service name", "Services", "Treatment", "Treatment name", "Usługa", "Usługi", "Nazwa usługi", "Zabieg", "Zaplanowane usługi");

    private static readonly HashSet<string> StaffAliases = HeaderKey.Set(
        "Staff", "Staff member", "Team member", "Employee", "Stylist", "Therapist", "Practitioner", "With", "Pracownik", "Pracownica", "Specjalista", "Specjalistka");

    private static readonly HashSet<string> PriceAliases = HeaderKey.Set(
        "Price", "Fee", "Amount", "Value", "Gross sale", "Service price", "Cena", "Kwota", "Wartość", "Cena usługi");

    private static readonly HashSet<string> StatusAliases = HeaderKey.Set(
        "Status", "State", "Appointment status", "Booking status", "Status wizyty");

    public AppointmentImportParseResult Parse(ReadOnlyMemory<byte> content, LocalDate firstDay)
    {
        // An export from the old system's support holds years of history: the byte and row caps are for the whole
        // file, the cap on visits is counted only on the days that are kept.
        if (content.Length > AppointmentImportLimits.MaxBytes)
        {
            return AppointmentImportParseResult.Failed(ClientImportError.TooLarge);
        }

        if (!CsvDecoder.TryDecode(content.Span, out var text))
        {
            return AppointmentImportParseResult.Failed(ClientImportError.NotText);
        }

        var document = CsvReader.Read(text, AppointmentImportLimits.MaxRowsRead);
        if (document.Header.Count == 0 || document.Records.Count == 0)
        {
            return AppointmentImportParseResult.Failed(ClientImportError.Empty, document.Header);
        }

        if (document.Truncated)
        {
            return AppointmentImportParseResult.Failed(ClientImportError.TooManyRows, document.Header);
        }

        var columns = Columns.Of(document.Header);
        if (columns.Missing() is { Count: > 0 } missing)
        {
            return AppointmentImportParseResult.Refused(AppointmentImportFileError.MissingColumns, document.Header, missing);
        }

        // One date that can only be month-first means every date of the file may be: refuse it before any is read.
        var dateCells = document.Records.SelectMany(r => new[] { Field(r, columns.Date), ImportDates.DatePart(Field(r, columns.DateTime)), ImportDates.DatePart(Field(r, columns.Time)) });
        if (dateCells.Any(ImportDates.LooksMonthFirst))
        {
            return AppointmentImportParseResult.Refused(AppointmentImportFileError.MonthFirstDates, document.Header);
        }

        // Rows of earlier days are history: counted, a few of their dates kept so a misread file shows itself, and dropped.
        var kept = new List<ImportedAppointmentRow>();
        var pastCount = 0;
        var pastDays = new List<LocalDate>();
        foreach (var record in document.Records)
        {
            var row = Row(record, columns, document.Header.Count);
            if (row.Start is { } start && start.Date < firstDay)
            {
                pastCount++;
                if (pastDays.Count < AppointmentImportLimits.PastDaysShown && !pastDays.Contains(start.Date))
                {
                    pastDays.Add(start.Date);
                }

                continue;
            }

            kept.Add(row);
        }

        // Rows the old system calls cancelled are kept only to be reported, so they do not count toward the cap.
        var future = kept.Count(r => AppointmentImportStatuses.Read(r.Status) != OldStatus.Gone);
        if (future > AppointmentImportLimits.MaxRows)
        {
            return AppointmentImportParseResult.Refused(AppointmentImportFileError.TooManyFutureRows, document.Header, futureCount: future);
        }

        return new AppointmentImportParseResult(document.Header, kept, null, PastCount: pastCount, PastDays: pastDays);
    }

    private static ImportedAppointmentRow Row(ImportRecord record, Columns columns, int headerCount)
    {
        var problems = new List<AppointmentImportProblem>();
        if (record.Fields.Count != headerCount)
        {
            problems.Add(AppointmentImportProblem.WrongColumnCount);
        }

        var start = Start(record, columns, problems);

        LocalDateTime? end = null;
        if (Field(record, columns.End) is { } endText && start is { } from)
        {
            // An end is a time on the day of the visit, or a date and a time.
            if (ImportDates.TryTime(endText, out var endTime) && endTime is { } clock)
            {
                end = from.Date + clock;
            }
            else if (ImportDates.TryDateTime(endText, out var endMoment) && endMoment is { } moment)
            {
                end = moment;
            }
            else
            {
                problems.Add(AppointmentImportProblem.InvalidLength);
            }
        }

        if (!ImportDates.TryMinutes(Field(record, columns.Minutes), out var minutes))
        {
            AddOnce(problems, AppointmentImportProblem.InvalidLength);
        }

        if (!ImportNumbers.TryMoney(Field(record, columns.Price), out var price))
        {
            problems.Add(AppointmentImportProblem.InvalidPrice);
        }

        var name = Field(record, columns.Client)
            ?? string.Join(' ', new[] { Field(record, columns.FirstName), Field(record, columns.LastName) }.Where(p => p is not null)) switch
            {
                "" => null,
                var joined => joined,
            };

        return new ImportedAppointmentRow(
            record.SourceRow,
            start,
            end,
            minutes,
            name,
            Field(record, columns.Phone),
            Field(record, columns.Email),
            Field(record, columns.Service),
            Field(record, columns.Staff),
            price,
            Field(record, columns.Status),
            problems);
    }

    /// <summary>The start from a date-and-time column, or from a date column and a time column; the reason when it cannot be read.</summary>
    private static LocalDateTime? Start(ImportRecord record, Columns columns, List<AppointmentImportProblem> problems)
    {
        if (Field(record, columns.DateTime) is { } both)
        {
            if (ImportDates.TryDateTime(both, out var moment) && moment is { } start)
            {
                return start;
            }

            problems.Add(ImportDates.TryDate(ImportDates.DatePart(both), out var day) && day is not null ? AppointmentImportProblem.InvalidTime : AppointmentImportProblem.InvalidDate);
            return null;
        }

        var dateText = Field(record, columns.Date);
        var timeText = Field(record, columns.Time);

        // Some systems put the whole moment in the "Start" column next to a date column, or with no date column at all.
        if (timeText is not null && ImportDates.TryDateTime(timeText, out var whole) && whole is { } atOnce)
        {
            if (dateText is null || (ImportDates.TryDate(dateText, out var sameDay) && sameDay == atOnce.Date))
            {
                return atOnce;
            }
        }

        LocalDate? date = null;
        if (dateText is null || !ImportDates.TryDate(dateText, out date) || date is null)
        {
            problems.Add(AppointmentImportProblem.InvalidDate);
        }

        LocalTime? time = null;
        if (timeText is null || !ImportDates.TryTime(timeText, out time) || time is null)
        {
            problems.Add(AppointmentImportProblem.InvalidTime);
        }

        return date is { } d && time is { } t ? d + t : null;
    }

    private static void AddOnce(List<AppointmentImportProblem> problems, AppointmentImportProblem problem)
    {
        if (!problems.Contains(problem))
        {
            problems.Add(problem);
        }
    }

    /// <summary>The trimmed text of a column; null when the file has no such column, the row is short, or the cell is blank.</summary>
    private static string? Field(ImportRecord record, int index)
        => index >= 0 && index < record.Fields.Count && !string.IsNullOrWhiteSpace(record.Fields[index]) ? record.Fields[index].Trim() : null;

    /// <summary>Where each field is in the file; -1 for a column the file does not have. The first heading that fits wins.</summary>
    private sealed record Columns(
        int Date, int Time, int DateTime, int End, int Minutes,
        int Client, int FirstName, int LastName, int Phone, int Email,
        int Service, int Staff, int Price, int Status)
    {
        public static Columns Of(IReadOnlyList<string> header)
        {
            var keys = header.Select(HeaderKey.Of).ToList();
            int Find(HashSet<string> aliases) => keys.FindIndex(aliases.Contains);
            return new Columns(
                Find(DateAliases), Find(TimeAliases), Find(DateTimeAliases), Find(EndAliases), Find(MinutesAliases),
                Find(ClientAliases), Find(FirstNameAliases), Find(LastNameAliases), Find(PhoneAliases), Find(EmailAliases),
                Find(ServiceAliases), Find(StaffAliases), Find(PriceAliases), Find(StatusAliases));
        }

        /// <summary>What a file must name and this one does not, as the words the refusal shows.</summary>
        public List<string> Missing()
        {
            var missing = new List<string>();
            var hasMoment = DateTime >= 0 || Time >= 0;
            if (Date < 0 && DateTime < 0 && Time < 0)
            {
                missing.Add("Date");
            }

            if (!hasMoment)
            {
                missing.Add("Time");
            }

            if (Service < 0)
            {
                missing.Add("Service");
            }

            if (Client < 0 && FirstName < 0 && LastName < 0 && Phone < 0 && Email < 0)
            {
                missing.Add("Client");
            }

            return missing;
        }
    }
}