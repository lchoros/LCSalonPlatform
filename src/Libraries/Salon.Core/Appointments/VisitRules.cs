namespace Salon.Core.Appointments;

/// <summary>
/// A visit booked as several services (ADR 0095): they follow one another without a gap, with one person, and each is
/// an appointment of its own that shares <see cref="Appointment.VisitGroupId"/> with the others.
/// </summary>
public static class VisitRules
{
    /// <summary>The most services one booking may hold: enough for a cut, a colour and a treatment, short enough to fit a day.</summary>
    public const int MaxServices = 4;

    /// <summary>How the services of one visit are named together in a message or on a page: "Cut + Colour".</summary>
    public static string JoinNames(IEnumerable<string> names) => string.Join(" + ", names);
}