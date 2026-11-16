using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Clients;

/// <summary>
/// One consultation form a client answered before a treatment (ADR 0058). Tenant-owned. The answers are about the
/// client's health (allergies, skin, medication), which is special-category data under UK GDPR: they exist here only
/// as cipher text (<see cref="AnswersProtected"/>), produced and read through the field encryptor by the
/// consultation service, never by a list, a search, a file, a log or an event. What is kept in the clear is what a
/// list needs and says nothing about health: whose it is, when, who took it and for what. A record is never edited:
/// a new consultation is a new record, so what the client confirmed on a day stays what they confirmed.
/// </summary>
public class ConsultationRecord : BaseEntity, ITenantEntity
{
    public const int ForMaxLength = 100;
    public const int ConsentWordingMaxLength = 500;

    /// <summary>
    /// Plain-text ceiling for the "more about the answers" box. The cipher text column has no length of its own: how
    /// long cipher text is depends on the letters typed, so the ceilings are on what goes in.
    /// </summary>
    public const int DetailsMaxLength = 2000;

    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }

    public Instant TakenAt { get; set; }

    /// <summary>The login that took the consultation with the client.</summary>
    public Guid? TakenByUserId { get; set; }

    /// <summary>What the consultation was for, in the desk's words ("Colour", "Full head bleach"); optional.</summary>
    public string? For { get; set; }

    /// <summary>The answers, the details and the name the client confirmed with: cipher text of a small JSON document.</summary>
    public required string AnswersProtected { get; set; }

    /// <summary>The exact sentence the client agreed to, so the salon can show what was confirmed.</summary>
    public required string ConsentWording { get; set; }

    /// <summary>Which set of questions was asked (<see cref="ConsultationQuestions.Version"/>).</summary>
    public int FormVersion { get; set; }
}

/// <summary>
/// The questions of the consultation form (ADR 0058): one fixed set, asked before a colour or chemical treatment. Each
/// has a key that is stored with the answer; the words live in the resources. Changing or adding a question raises
/// <see cref="Version"/>, and a record keeps the version it was asked with.
/// </summary>
public static class ConsultationQuestions
{
    public const int Version = 1;

    public const string HadReaction = "HadReaction";
    public const string Allergies = "Allergies";
    public const string ScalpCondition = "ScalpCondition";
    public const string Medication = "Medication";
    public const string Pregnant = "Pregnant";
    public const string TreatedRecently = "TreatedRecently";

    /// <summary>In the order they are asked.</summary>
    public static IReadOnlyList<string> All { get; } = [HadReaction, Allergies, ScalpCondition, Medication, Pregnant, TreatedRecently];
}

/// <summary>What the client said, as the service keeps it under encryption and gives it back (ADR 0058).</summary>
/// <param name="Answers">One yes or no per question key of <see cref="ConsultationQuestions.All"/>.</param>
/// <param name="Details">Anything the client or the stylist added; null when nothing.</param>
/// <param name="PatchTestOn">The day of the patch test, when one was done.</param>
/// <param name="ConfirmedBy">The client's name as they typed it to confirm the answers.</param>
public sealed record ConsultationAnswers(IReadOnlyDictionary<string, bool> Answers, string? Details, LocalDate? PatchTestOn, string ConfirmedBy)
{
    /// <summary>The questions answered "yes", in the order asked: what the person doing the treatment must read.</summary>
    public IReadOnlyList<string> Concerns => [.. ConsultationQuestions.All.Where(key => Answers.TryGetValue(key, out var yes) && yes)];

    /// <summary>Never the answers: a record must not end up in a log line because somebody printed it.</summary>
    public override string ToString() => $"{nameof(ConsultationAnswers)} {{ ... }}";
}

/// <summary>What a consultation must have before it is kept (ADR 0058). Pure.</summary>
public static class ConsultationRules
{
    /// <summary>Every question has an answer: a form with a question skipped is not a consultation.</summary>
    public static bool IsComplete(IReadOnlyDictionary<string, bool>? answers)
        => answers is not null && ConsultationQuestions.All.All(answers.ContainsKey);

    public static bool DetailsFit(string? details) => details is null || details.Trim().Length <= ConsultationRecord.DetailsMaxLength;

    public static bool ForFits(string? text) => text is null || text.Trim().Length <= ConsultationRecord.ForMaxLength;

    /// <summary>The client confirms with their name: at least two characters that are not space.</summary>
    public static bool IsConfirmed(string? name, bool agreed) => agreed && !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 2 and <= Client.NameMaxLength * 2;

    /// <summary>A patch test is done on a day that has happened.</summary>
    public static bool PatchTestDayFits(LocalDate? day, LocalDate today) => day is null || day <= today;
}