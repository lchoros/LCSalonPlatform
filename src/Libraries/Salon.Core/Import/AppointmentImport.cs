using NodaTime;

namespace Salon.Core.Import;

/// <summary>Why a row of a diary file cannot be read in as it is (ADR 0067).</summary>
public enum AppointmentImportProblem
{
    /// <summary>The record has a different number of fields than the header.</summary>
    WrongColumnCount = 1,

    /// <summary>No date, or not a date read day-first or ISO.</summary>
    InvalidDate = 2,

    /// <summary>No start time, or not a time of day to the minute.</summary>
    InvalidTime = 3,

    /// <summary>The end or the length cannot be read, is not after the start, or is longer than a visit can be.</summary>
    InvalidLength = 4,

    /// <summary>The end time and the length both given, and more than a minute apart.</summary>
    LengthsDisagree = 5,

    /// <summary>Not an amount of money, below zero, more than two decimals or over the cap.</summary>
    InvalidPrice = 6,

    /// <summary>The clocks skip this time in the spring.</summary>
    TimeSkipped = 7,

    /// <summary>The clocks pass this time twice in the autumn.</summary>
    TimeTwice = 8,

    /// <summary>A status word that says neither "booked" nor "cancelled": pending, a request, unconfirmed.</summary>
    UnknownStatus = 9,

    /// <summary>Neither a client's name nor a phone number nor an email.</summary>
    MissingClient = 10,

    /// <summary>No client of the salon has this phone number, email or name.</summary>
    UnknownClient = 11,

    /// <summary>More than one client has this name and the row gives nothing else to tell them apart.</summary>
    AmbiguousClient = 12,

    /// <summary>The phone number and the email belong to two different clients.</summary>
    ContactsDisagree = 13,

    /// <summary>The phone number or email belongs to a client of another name: a family number, or a mistake.</summary>
    ContactBelongsToOther = 14,

    MissingService = 15,

    /// <summary>The salon has no service of this name.</summary>
    UnknownService = 16,

    /// <summary>The salon has the service but no longer offers it.</summary>
    ServiceNotInUse = 17,

    /// <summary>No person named, and the salon has more than one who takes bookings.</summary>
    MissingStaff = 18,

    /// <summary>Nobody at the salon has this name.</summary>
    UnknownStaff = 19,

    /// <summary>More than one person at the salon has this name.</summary>
    AmbiguousStaff = 20,

    /// <summary>The person has left or does not take bookings.</summary>
    StaffNotBookable = 21,

    /// <summary>The person does not do this service.</summary>
    StaffDoesNotOffer = 22,

    /// <summary>The person already has a visit in the diary at this time.</summary>
    ClashesWithDiary = 23,

    /// <summary>An earlier row of the file gives the same person a visit at this time (often a second client during processing time).</summary>
    ClashesInFile = 24,

    /// <summary>The time is kept for a waiting client (ADR 0065).</summary>
    ClashesWithHeldTime = 25,
}

/// <summary>How the old system described a visit, as far as a diary import is concerned (ADR 0067).</summary>
public enum OldStatus
{
    /// <summary>No status, or a word for a visit that will happen: booked, confirmed, new.</summary>
    Booked = 1,

    /// <summary>A word for a visit that will not happen: cancelled, no-show, deleted.</summary>
    Gone = 2,

    /// <summary>Any other word: a request, pending, unconfirmed. Not guessed.</summary>
    Unknown = 3,
}

/// <summary>
/// One visit as it appears in a diary file (ADR 0067): text trimmed, the date and times read, nothing matched yet.
/// What could not be read is null and named in <see cref="Problems"/>; the classifier matches the client, the
/// service and the person against the salon. Notes and columns the reader does not know are never kept.
/// </summary>
/// <param name="SourceRow">1-based line number of the record in the file, header included.</param>
/// <param name="Start">The local date and time the visit starts, on the salon's clock.</param>
/// <param name="End">The local end, when the file gives one.</param>
/// <param name="Minutes">The length in minutes, when the file gives one.</param>
/// <param name="ClientName">The client as written: one full name, or first and last name joined.</param>
/// <param name="Status">The old system's word for the visit, as written; null when the file has none.</param>
public sealed record ImportedAppointmentRow(
    int SourceRow,
    LocalDateTime? Start,
    LocalDateTime? End,
    int? Minutes,
    string? ClientName,
    string? ClientPhone,
    string? ClientEmail,
    string? Service,
    string? Staff,
    decimal? Price,
    string? Status,
    IReadOnlyList<AppointmentImportProblem> Problems)
{
    /// <summary>Never a name, a number or an email in a log line.</summary>
    public override string ToString() => $"{nameof(ImportedAppointmentRow)} {{ SourceRow = {SourceRow} }}";
}

/// <summary>Why a whole diary file is refused, beyond the reasons a client file is (<see cref="ClientImportError"/>).</summary>
public enum AppointmentImportFileError
{
    /// <summary>The headings name no date, no start time, no service or no client.</summary>
    MissingColumns = 1,

    /// <summary>At least one date can only be read month-first (11/20/2026): every date of the file could be wrong.</summary>
    MonthFirstDates = 2,

    /// <summary>More visits from today on than one file may hold (<see cref="AppointmentImportLimits.MaxRows"/>).</summary>
    TooManyFutureRows = 3,
}

/// <summary>
/// What the provider made of a file: the rows from today on, how many rows were of earlier days (counted, not kept),
/// or an error and no rows.
/// </summary>
/// <param name="PastCount">Rows whose date is before the first day kept: history, which is not brought over.</param>
/// <param name="PastDays">A few of those dates as read, so a misread file shows itself.</param>
/// <param name="Missing">With <see cref="AppointmentImportFileError.MissingColumns"/>: what the headings did not name: Date, Time, Service, Client.</param>
/// <param name="FutureCount">With <see cref="AppointmentImportFileError.TooManyFutureRows"/>: how many there were.</param>
public sealed record AppointmentImportParseResult(
    IReadOnlyList<string> Header,
    IReadOnlyList<ImportedAppointmentRow> Rows,
    ClientImportError? Error,
    AppointmentImportFileError? FileError = null,
    IReadOnlyList<string>? Missing = null,
    int PastCount = 0,
    IReadOnlyList<LocalDate>? PastDays = null,
    int FutureCount = 0)
{
    public bool IsSuccess => Error is null && FileError is null;

    public static AppointmentImportParseResult Failed(ClientImportError error, IReadOnlyList<string>? header = null) => new(header ?? [], [], error);

    public static AppointmentImportParseResult Refused(AppointmentImportFileError error, IReadOnlyList<string> header, IReadOnlyList<string>? missing = null, int futureCount = 0)
        => new(header, [], null, error, missing, FutureCount: futureCount);
}

/// <summary>
/// Turns the bytes of an uploaded diary file into <see cref="ImportedAppointmentRow"/>s (ADR 0067): decodes, splits,
/// finds the columns by their headings and reads the dates and times. Rows of days before
/// <paramref name="firstDay"/> are counted and dropped, so an export of years of history is not refused for its size.
/// Pure: nothing here touches the database.
/// </summary>
public interface IAppointmentImportProvider
{
    AppointmentImportParseResult Parse(ReadOnlyMemory<byte> content, LocalDate firstDay);
}

/// <summary>Caps on one diary file (ADR 0067).</summary>
public static class AppointmentImportLimits
{
    /// <summary>Visits from today on in one file: more than a busy salon books in a season, and one short transaction.</summary>
    public const int MaxRows = 2_000;

    /// <summary>Rows read in all, history included: an export of several years of a busy salon.</summary>
    public const int MaxRowsRead = 250_000;

    /// <summary>Twenty megabytes: an export of several years of history.</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>The longest visit the service form allows (<c>ServiceRules.MaxDurationMinutes</c>).</summary>
    public const int MaxMinutes = 600;

    /// <summary>How many dates of past rows the preview shows.</summary>
    public const int PastDaysShown = 5;
}

/// <summary>The old system's words for a visit (ADR 0067). Pure. A word the import does not know is not guessed.</summary>
public static class AppointmentImportStatuses
{
    // Read before the others: "unconfirmed" contains "confirmed".
    private static readonly string[] Undecided =
    [
        "unconfirm", "not confirmed", "pending", "await", "request", "waiting", "tentative", "provisional", "reschedul",
        "niepotwierdz", "oczekuj", "wstępn", "wstepn", "prośb", "prosb", "zapytan",
    ];

    private static readonly string[] Gone =
    [
        "cancel", "no show", "noshow", "no-show", "did not show", "didn't show", "not attended", "did not attend", "deleted", "rejected", "declined",
        "anulow", "odwoł", "odwol", "nieobecn", "nie przysz", "nie pojawi", "usunię", "usunie", "odrzuc",
    ];

    private static readonly string[] Booked =
    [
        "booked", "confirmed", "new", "accepted", "arrived", "scheduled", "upcoming", "reserved", "planned",
        "umówion", "umowion", "potwierdzon", "zarezerwowan", "zaplanowan", "nowa", "nowy", "przyjęt", "przyjet",
    ];

    public static OldStatus Read(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return OldStatus.Booked;
        }

        var value = status.Trim().ToLowerInvariant();
        if (Undecided.Any(word => value.Contains(word, StringComparison.Ordinal)))
        {
            return OldStatus.Unknown;
        }

        // "DNA" (did not attend) only as the whole word: as part of a word it is not a status.
        if (value == "dna" || Gone.Any(word => value.Contains(word, StringComparison.Ordinal)))
        {
            return OldStatus.Gone;
        }

        return Booked.Any(word => value.Contains(word, StringComparison.Ordinal)) ? OldStatus.Booked : OldStatus.Unknown;
    }
}