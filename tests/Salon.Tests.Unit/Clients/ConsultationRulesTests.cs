using NodaTime;
using Salon.Core.Clients;

namespace Salon.Tests.Unit.Clients;

/// <summary>
/// The pure rules of a consultation form (ADR 0058): every question answered, the client's name and tick to confirm,
/// text that fits, a patch test on a day that has happened; and the answers never print themselves.
/// </summary>
public class ConsultationRulesTests
{
    private static readonly LocalDate Today = new(2026, 11, 16);

    [Fact]
    public void The_form_asks_six_questions_each_once()
    {
        ConsultationQuestions.All.Count.ShouldBe(6);
        ConsultationQuestions.All.Distinct().Count().ShouldBe(6);
        ConsultationQuestions.Version.ShouldBe(1);
    }

    [Fact]
    public void A_form_is_complete_when_every_question_has_an_answer()
    {
        ConsultationRules.IsComplete(All(false)).ShouldBeTrue();
        ConsultationRules.IsComplete(All(true)).ShouldBeTrue();
    }

    [Fact]
    public void A_form_with_a_question_skipped_is_not_complete()
    {
        foreach (var skipped in ConsultationQuestions.All)
        {
            var answers = All(false);
            answers.Remove(skipped);
            ConsultationRules.IsComplete(answers).ShouldBeFalse(skipped);
        }

        ConsultationRules.IsComplete(null).ShouldBeFalse();
        ConsultationRules.IsComplete(new Dictionary<string, bool>()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Olivia Brown", true, true)]
    [InlineData("  Li  ", true, true)]
    [InlineData("Olivia Brown", false, false)]
    [InlineData("O", true, false)]
    [InlineData("   ", true, false)]
    [InlineData(null, true, false)]
    public void The_client_confirms_with_their_name_and_the_tick(string? name, bool agreed, bool expected)
    {
        ConsultationRules.IsConfirmed(name, agreed).ShouldBe(expected);
    }

    [Fact]
    public void A_name_longer_than_two_names_can_be_is_not_a_confirmation()
    {
        ConsultationRules.IsConfirmed(new string('a', Client.NameMaxLength * 2), true).ShouldBeTrue();
        ConsultationRules.IsConfirmed(new string('a', (Client.NameMaxLength * 2) + 1), true).ShouldBeFalse();
    }

    [Fact]
    public void Details_and_what_it_is_for_have_a_ceiling_counted_without_the_spaces_around()
    {
        ConsultationRules.DetailsFit(null).ShouldBeTrue();
        ConsultationRules.DetailsFit(" " + new string('a', ConsultationRecord.DetailsMaxLength) + " ").ShouldBeTrue();
        ConsultationRules.DetailsFit(new string('a', ConsultationRecord.DetailsMaxLength + 1)).ShouldBeFalse();
        ConsultationRules.ForFits(null).ShouldBeTrue();
        ConsultationRules.ForFits(new string('a', ConsultationRecord.ForMaxLength)).ShouldBeTrue();
        ConsultationRules.ForFits(new string('a', ConsultationRecord.ForMaxLength + 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_patch_test_is_dated_today_or_before()
    {
        ConsultationRules.PatchTestDayFits(null, Today).ShouldBeTrue();
        ConsultationRules.PatchTestDayFits(Today, Today).ShouldBeTrue();
        ConsultationRules.PatchTestDayFits(Today.PlusDays(-400), Today).ShouldBeTrue();
        ConsultationRules.PatchTestDayFits(Today.PlusDays(1), Today).ShouldBeFalse();
    }

    [Fact]
    public void The_concerns_are_the_questions_answered_yes_in_the_order_asked()
    {
        var answers = All(false);
        answers[ConsultationQuestions.Pregnant] = true;
        answers[ConsultationQuestions.HadReaction] = true;

        new ConsultationAnswers(answers, null, null, "Olivia Brown").Concerns
            .ShouldBe([ConsultationQuestions.HadReaction, ConsultationQuestions.Pregnant]);
        new ConsultationAnswers(All(false), null, null, "Olivia Brown").Concerns.ShouldBeEmpty();
    }

    [Fact]
    public void The_answers_do_not_print_themselves()
    {
        var printed = new ConsultationAnswers(All(true), "Reacted to PPD in 2024", Today, "Olivia Brown").ToString();

        printed.ShouldNotContain("PPD");
        printed.ShouldNotContain("Olivia");
        printed.ShouldNotContain("True");
    }

    private static Dictionary<string, bool> All(bool answer) => ConsultationQuestions.All.ToDictionary(key => key, _ => answer);
}