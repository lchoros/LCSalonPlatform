using NodaTime;
using Salon.Core.Import;

namespace Salon.Services.Appointments.Import;

/// <summary>
/// Preview-then-commit of the coming weeks of a salon's diary from its old system (ADR 0067), in the shape of the
/// other imports. <see cref="PreviewAsync"/> only reads and says, row by row, what would be added and why anything
/// would not. <see cref="CommitAsync"/> classifies again against the diary as it is then and writes the visits it
/// can, in one write that commits or rolls back whole. Clients, services and people are never created here.
/// </summary>
public interface IAppointmentImportService
{
    /// <summary>The first day a file is read from: today in the salon's zone. Earlier rows are history.</summary>
    Task<LocalDate> FirstDayAsync(CancellationToken cancellationToken = default);

    /// <summary>Classifies every kept row for the current tenant. Requires a tenant in scope.</summary>
    Task<AppointmentImportPreview> PreviewAsync(AppointmentImportParseResult parsed, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the visits the preview's rows still make new; must run inside a command (one transaction). Throws
    /// <see cref="AppointmentImportClashException"/> when the database refuses a clash that appeared after the check,
    /// so the command rolls back and nothing of the file is written.
    /// </summary>
    Task<AppointmentImportResult> CommitAsync(AppointmentImportPreview preview, Guid? byUserId, CancellationToken cancellationToken = default);
}

/// <summary>What becomes of a row of the file.</summary>
public enum AppointmentImportStatus
{
    /// <summary>Added to the diary on confirm.</summary>
    New = 1,

    /// <summary>The visit was brought over before (since moved or cancelled here, or as it was), or is in the diary already.</summary>
    AlreadyThere = 2,

    /// <summary>The old system says cancelled, and the visit brought over before is still booked here: the owner decides.</summary>
    CancelledSince = 3,

    /// <summary>The old system says cancelled or not attended; nothing here to change.</summary>
    Cancelled = 4,

    /// <summary>Earlier today: over, or about to be, by the time the file is read.</summary>
    Past = 5,

    /// <summary>Cannot be read in; the reasons are listed.</summary>
    Invalid = 6,
}

/// <summary>How a row's client was found.</summary>
public enum ClientMatch
{
    Phone = 1,
    Email = 2,

    /// <summary>By the full name alone: shown apart, because a name is the weakest match.</summary>
    Name = 3,
}

/// <summary>What a row was matched to, as far as it was; on a row that is not new, some of it can be missing.</summary>
public sealed record AppointmentImportMatch
{
    public Guid? ClientId { get; init; }

    /// <summary>The name on the matched card, shown beside the name in the file.</summary>
    public string? ClientName { get; init; }

    public ClientMatch? ClientMatchedBy { get; init; }

    public Guid? ServiceId { get; init; }

    public string? ServiceName { get; init; }

    public Guid? StaffId { get; init; }

    public string? StaffName { get; init; }

    /// <summary>True when the person was found by the first word of the name only.</summary>
    public bool StaffByFirstName { get; init; }

    public Instant? Start { get; init; }

    public Instant? End { get; init; }

    public int? Minutes { get; init; }

    public decimal? Price { get; init; }

    /// <summary>The visit already in the diary this row is (<see cref="AppointmentImportStatus.AlreadyThere"/>, <see cref="AppointmentImportStatus.CancelledSince"/>).</summary>
    public Guid? ExistingAppointmentId { get; init; }
}

/// <summary>A row after classification.</summary>
public sealed record ClassifiedAppointmentRow(ImportedAppointmentRow Row, AppointmentImportStatus Status, IReadOnlyList<AppointmentImportProblem> Problems, AppointmentImportMatch Match)
{
    public override string ToString() => $"{nameof(ClassifiedAppointmentRow)} {{ SourceRow = {Row.SourceRow}, Status = {Status} }}";
}

/// <summary>One value of the file the salon does not have, with how many rows carry it and the nearest name it has.</summary>
public sealed record UnrecognisedValue(string Value, int Rows, string? Suggestion);

/// <summary>One status word of the file and how it was read.</summary>
public sealed record StatusWord(string Word, OldStatus Reading, int Rows);

/// <summary>What the owner confirms: the classified rows of one file for one salon. Kept server-side, never posted back.</summary>
/// <param name="Currency">The salon's currency, which every price of the file is read in.</param>
/// <param name="ReadAt">When the file was read: visits from then on, reminders from then on.</param>
/// <param name="RemindersFrom">
/// Reminders go out for visits whose reminder time is after the import; the old system should keep reminding until
/// this moment. Null when the salon sends no reminders.
/// </param>
/// <param name="PastCount">Rows of earlier days: history, not brought over.</param>
/// <param name="PastDays">A few of their dates as read, so a misread file shows itself.</param>
public sealed record AppointmentImportPreview(
    Guid TenantId,
    string FileName,
    string Currency,
    ZonedDateTime ReadAt,
    ZonedDateTime? RemindersFrom,
    IReadOnlyList<ClassifiedAppointmentRow> Rows,
    int PastCount,
    IReadOnlyList<LocalDate> PastDays,
    IReadOnlyList<UnrecognisedValue> UnknownServices,
    IReadOnlyList<UnrecognisedValue> UnknownStaff,
    IReadOnlyList<StatusWord> Statuses)
{
    public int NewCount => Count(AppointmentImportStatus.New);

    public int AlreadyThereCount => Count(AppointmentImportStatus.AlreadyThere);

    public int CancelledCount => Count(AppointmentImportStatus.Cancelled) + Count(AppointmentImportStatus.CancelledSince);

    public int InvalidCount => Count(AppointmentImportStatus.Invalid);

    /// <summary>Future visits the owner must deal with by hand: not readable, or cancelled in the old system since.</summary>
    public int NotBroughtCount => Count(AppointmentImportStatus.Invalid) + Count(AppointmentImportStatus.CancelledSince);

    /// <summary>New visits that start before <see cref="RemindersFrom"/>: they may miss a reminder from us.</summary>
    public int NotFullyReminded => RemindersFrom is { } from ? Rows.Count(r => r.Status == AppointmentImportStatus.New && r.Match.Start < from.ToInstant()) : 0;

    /// <summary>The first and the last day of the visits to be added; null when none.</summary>
    public (LocalDate First, LocalDate Last)? Span(DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var days = Rows.Where(r => r.Status == AppointmentImportStatus.New && r.Match.Start is not null).Select(r => r.Match.Start!.Value.InZone(zone).Date).ToList();
        return days.Count == 0 ? null : (days.Min(), days.Max());
    }

    private int Count(AppointmentImportStatus status) => Rows.Count(r => r.Status == status);

    public override string ToString() => $"{nameof(AppointmentImportPreview)} {{ Rows = {Rows.Count}, New = {NewCount} }}";
}

/// <summary>Counts after the commit, and the rows the owner still has to deal with by hand.</summary>
/// <param name="NotBrought">Future rows not written: not readable, or cancelled in the old system since the last file.</param>
public sealed record AppointmentImportResult(
    string FileName,
    int Added,
    int AlreadyThere,
    int Cancelled,
    int Past,
    ZonedDateTime? RemindersFrom,
    int NotFullyReminded,
    IReadOnlyList<ClassifiedAppointmentRow> NotBrought)
{
    public override string ToString() => $"{nameof(AppointmentImportResult)} {{ Added = {Added} }}";
}

/// <summary>The database refused a visit that clashes with one written since the file was checked: nothing was written.</summary>
public sealed class AppointmentImportClashException : Exception
{
    public AppointmentImportClashException()
        : base("A visit of the file clashes with one written since the file was checked; nothing was written.")
    {
    }

    public AppointmentImportClashException(string message)
        : base(message)
    {
    }

    public AppointmentImportClashException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}