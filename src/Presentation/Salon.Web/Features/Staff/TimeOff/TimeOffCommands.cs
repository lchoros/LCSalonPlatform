using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>
/// Adds one time-off entry for a staff member (ADR 0015); false when the salon has no such person. Fields arrive as the
/// form posted them (<c>&lt;input type="date"&gt;</c> gives "2026-03-29", <c>&lt;input type="time"&gt;</c> "09:00");
/// <see cref="AddTimeOffValidator"/> holds the rules, the handler stores the parsed values and publishes
/// <see cref="Core.Staff.Events.StaffTimeOffChanged"/>.
/// </summary>
/// <param name="Start">Blank together with <paramref name="End"/> for whole days.</param>
public sealed record AddTimeOffCommand(Guid StaffMemberId, string? FromDate, string? ToDate, string? Start, string? End, string? Reason) : ICommand<bool>
{
    /// <summary>An empty form for a GET render.</summary>
    public static AddTimeOffCommand Empty(Guid staffMemberId) => new(staffMemberId, null, null, null, null, null);

    public LocalDate? ParsedFrom => StaffTimeOffRules.TryParseDate(FromDate);

    public LocalDate? ParsedTo => StaffTimeOffRules.TryParseDate(ToDate);

    public LocalTime? ParsedStart => WorkingHoursRules.TryParseTime(Start);

    public LocalTime? ParsedEnd => WorkingHoursRules.TryParseTime(End);

    public bool HasStart => !string.IsNullOrWhiteSpace(Start);

    public bool HasEnd => !string.IsNullOrWhiteSpace(End);

    /// <summary>The entry as it would be stored, once every field parses; null while anything is still invalid.</summary>
    public StaffTimeOff? ToEntry()
    {
        if (ParsedFrom is not { } from || ParsedTo is not { } to || HasStart != HasEnd || (HasStart && (ParsedStart is null || ParsedEnd is null)))
        {
            return null;
        }

        return new StaffTimeOff
        {
            StaffMemberId = StaffMemberId,
            FromDate = from,
            ToDate = to,
            Start = HasStart ? ParsedStart : null,
            End = HasEnd ? ParsedEnd : null,
            Reason = StaffTimeOffRules.NormalizeReason(Reason),
        };
    }
}

/// <summary>Removes one time-off entry of a staff member; false when the salon has no such person or entry.</summary>
public sealed record DeleteTimeOffCommand(Guid StaffMemberId, Guid EntryId) : ICommand<bool>;