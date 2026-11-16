using System.Globalization;
using Salon.Core.Clients;
using Salon.Services.Clients;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.Consultations;

/// <summary>What the consultation pages say (ADR 0058, ADR 0043): the words, kept out of the views.</summary>
public static class ConsultationText
{
    /// <summary>The resource key of a question's wording.</summary>
    public static string QuestionKey(string questionKey) => $"Clients.Consultation.Question.{questionKey}";

    /// <summary>"Taken on 16/11/2026 by Anna" or, when the login is gone, "Taken on 16/11/2026".</summary>
    public static string Taken(ILocalizationService localizer, ConsultationSummary summary, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(culture);
        var day = summary.TakenOn.ToString("d", culture);
        return summary.TakenByName is { } name
            ? localizer.Format("Clients.Consultation.TakenBy", day, name)
            : localizer.Format("Clients.Consultation.Taken", day);
    }

    /// <summary>Every question with the client's answer in words, in the order asked.</summary>
    public static IReadOnlyList<ConsultationAnswerLine> Lines(ILocalizationService localizer, ConsultationAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(answers);
        return
        [
            .. ConsultationQuestions.All.Select(key =>
            {
                bool? yes = answers.Answers.TryGetValue(key, out var value) ? value : null;
                var word = yes switch
                {
                    true => localizer["Clients.Consultation.Yes"],
                    false => localizer["Clients.Consultation.No"],
                    _ => localizer["Clients.Consultation.NotAsked"],
                };
                return new ConsultationAnswerLine(key, localizer[QuestionKey(key)], word, yes == true);
            }),
        ];
    }

    /// <summary>The day of the patch test on the form, or that none was recorded.</summary>
    public static string PatchTest(ILocalizationService localizer, ConsultationAnswers answers, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(culture);
        return answers.PatchTestOn is { } day ? day.ToString("d", culture) : localizer["Clients.Consultation.NoPatchTest"];
    }
}

/// <param name="Question">The question as asked.</param>
/// <param name="Answer">"Yes", "No", or that this form did not ask it.</param>
/// <param name="IsConcern">True for a yes: the line the person doing the treatment must read.</param>
public sealed record ConsultationAnswerLine(string Key, string Question, string Answer, bool IsConcern);