using System.Globalization;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Import;
using Salon.Services.Appointments.Import;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.ImportAppointments;

/// <summary>What the diary import says about a row (ADR 0067, ADR 0043): the words, kept out of the views.</summary>
public static class AppointmentImportText
{
    private const string Separator = " · ";

    public static string Status(ILocalizationService localizer, AppointmentImportStatus status)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer[$"Appointments.Import.Status.{status}"];
    }

    public static string Problem(ILocalizationService localizer, AppointmentImportProblem problem, ClassifiedAppointmentRow row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return problem switch
        {
            AppointmentImportProblem.InvalidLength => localizer.Format("Appointments.Import.Problem.InvalidLength", AppointmentImportLimits.MaxMinutes),
            AppointmentImportProblem.LengthsDisagree => localizer.Format("Appointments.Import.Problem.LengthsDisagree", row.Row.Minutes ?? 0),
            AppointmentImportProblem.UnknownStatus => localizer.Format("Appointments.Import.Problem.UnknownStatus", row.Row.Status ?? string.Empty),
            AppointmentImportProblem.ContactBelongsToOther => localizer.Format("Appointments.Import.Problem.ContactBelongsToOther", row.Match.ClientName ?? string.Empty),
            AppointmentImportProblem.StaffNotBookable => localizer.Format("Appointments.Import.Problem.StaffNotBookable", row.Match.StaffName ?? row.Row.Staff ?? string.Empty),
            _ => localizer[$"Appointments.Import.Problem.{problem}"],
        };
    }

    /// <summary>Why a row is not brought over, all reasons in one line, for the file of rows not brought over.</summary>
    public static string Reasons(ILocalizationService localizer, ClassifiedAppointmentRow row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return row.Status == AppointmentImportStatus.CancelledSince
            ? localizer["Appointments.Import.Status.CancelledSince"]
            : string.Join(" ", row.Problems.Select(p => Problem(localizer, p, row)));
    }

    /// <summary>When, as the file says it: the weekday and the date, so a day and month read the wrong way round show themselves.</summary>
    public static string When(ClassifiedAppointmentRow row, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(culture);
        return row.Row.Start is { } start
            ? $"{start.Date.ToString("ddd d MMM yyyy", culture)}, {start.TimeOfDay.ToShortTime(culture)}"
            : "—";
    }

    /// <summary>The service, the person, the length and the price as they will be saved, the parts not known left out.</summary>
    public static string Details(ILocalizationService localizer, ClassifiedAppointmentRow row, string currency, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(culture);
        var match = row.Match;
        var parts = new[]
        {
            match.ServiceName ?? row.Row.Service,
            match.StaffName is { } staff
                ? match.StaffByFirstName ? localizer.Format("Appointments.Import.ByFirstName", staff) : staff
                : row.Row.Staff,
            match.Minutes is { } minutes ? localizer.Format("Appointments.Import.Minutes", minutes) : null,
            match.Price is { } price ? new Money(price, currency).ToDisplayString(culture) : null,
        };
        return string.Join(Separator, parts.Where(p => !string.IsNullOrEmpty(p))).OrDash();
    }

    /// <summary>The client as the file names them, and the card they were matched to when it reads differently or was found by name alone.</summary>
    public static string Client(ILocalizationService localizer, ClassifiedAppointmentRow row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        var written = row.Row.ClientName ?? row.Row.ClientPhone ?? row.Row.ClientEmail ?? "—";
        return row.Match switch
        {
            { ClientId: not null, ClientMatchedBy: ClientMatch.Name, ClientName: { } card } => localizer.Format("Appointments.Import.Client.ByName", card),
            { ClientId: not null, ClientMatchedBy: { } by, ClientName: { } card } when !string.Equals(card, written, StringComparison.OrdinalIgnoreCase)
                => localizer.Format($"Appointments.Import.Client.By{by}", written, card),
            { ClientId: not null, ClientName: { } card } => card,
            _ => written,
        };
    }

    /// <summary>The dates of a few past rows as read, for the owner to see whether the file was read the right way round.</summary>
    public static string Days(IEnumerable<LocalDate> days, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(culture);
        return string.Join(", ", days.Select(d => d.ToString("d MMM yyyy", culture)));
    }

    /// <summary>A moment on the salon's clock as a person reads it: "Thu 20 Nov, 14:30".</summary>
    public static string Moment(ZonedDateTime moment, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return $"{moment.Date.ToString("ddd d MMM", culture)}, {moment.TimeOfDay.ToShortTime(culture)}";
    }
}