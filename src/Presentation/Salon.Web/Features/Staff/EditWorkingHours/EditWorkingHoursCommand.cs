using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>
/// Replaces the whole weekly rota of one staff member (ADR 0010). Returns false when the salon has no such person.
/// <see cref="EditWorkingHoursValidator"/> holds the rules; the handler deletes every existing row and inserts the new
/// ones inside the pipeline's transaction, so a reader never sees half a rota.
/// </summary>
/// <param name="Days">Each day of the week exactly once, in any order.</param>
public sealed record EditWorkingHoursCommand(Guid StaffMemberId, IReadOnlyList<DayHoursInput> Days) : ICommand<bool>;

/// <param name="IsDayOff">When true every range of the day is ignored and the day gets no rows.</param>
/// <param name="Ranges">Up to <see cref="WorkingHoursRules.MaxRangesPerDay"/>; blank slots are skipped.</param>
public sealed record DayHoursInput(IsoDayOfWeek Day, bool IsDayOff, IReadOnlyList<TimeRangeInput> Ranges);

/// <summary>Times as the form posted them ("09:00"); parsed by <see cref="WorkingHoursRules.TryParseTime"/>.</summary>
public sealed record TimeRangeInput(string? Start, string? End)
{
    /// <summary>An unused slot: both fields blank.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(End);

    /// <summary>The parsed range when both ends parse; null otherwise (the validator reports which one did not).</summary>
    public TimeRange? Parsed
        => WorkingHoursRules.TryParseTime(Start) is { } start && WorkingHoursRules.TryParseTime(End) is { } end
            ? new TimeRange(start, end)
            : null;
}