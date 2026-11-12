using NodaTime;

using Salon.Core.Clients;

namespace Salon.Tests.Unit.Clients;

/// <summary>The pure merge rules of ADR 0017: the kept card wins, the duplicate only fills gaps, consent is never gained.</summary>
public class ClientMergeRulesTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 12, 10, 0);

    [Fact]
    public void Notes_of_both_cards_are_kept_target_first_with_a_separator()
    {
        ClientMergeRules.UnionNotes("Prefers afternoons.", "Allergic to dogs.").ShouldBe("Prefers afternoons.\n---\nAllergic to dogs.");
    }

    [Theory]
    [InlineData("Target only", null, "Target only")]
    [InlineData("Target only", "   ", "Target only")]
    [InlineData(null, "Source only", "Source only")]
    [InlineData("", " Source only ", "Source only")]
    [InlineData(null, null, null)]
    [InlineData("  ", "", null)]
    public void One_sided_or_blank_notes_need_no_separator(string? target, string? source, string? expected)
    {
        ClientMergeRules.UnionNotes(target, source).ShouldBe(expected);
    }

    [Fact]
    public void A_union_longer_than_the_column_is_cut_so_the_merge_never_fails()
    {
        var target = new string('a', Client.NotesMaxLength - 10);
        var source = new string('b', 100);

        var union = ClientMergeRules.UnionNotes(target, source);

        union.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(Client.NotesMaxLength);
        union.ShouldStartWith(target);
    }

    [Theory]
    [InlineData("cipher-target", "cipher-source", "cipher-target")]
    [InlineData("cipher-target", null, "cipher-target")]
    [InlineData(null, "cipher-source", "cipher-source")]
    [InlineData("", "cipher-source", "cipher-source")]
    [InlineData(null, null, null)]
    [InlineData(null, "", null)]
    public void Health_notes_are_the_targets_unless_it_has_none(string? target, string? source, string? expected)
    {
        ClientMergeRules.ChooseHealthNotes(target, source).ShouldBe(expected);
    }

    [Fact]
    public void The_patch_test_date_follows_the_same_rule()
    {
        var targetDate = new LocalDate(2026, 10, 1);
        var sourceDate = new LocalDate(2026, 5, 15);

        ClientMergeRules.ChoosePatchTest(targetDate, sourceDate).ShouldBe(targetDate);
        ClientMergeRules.ChoosePatchTest(null, sourceDate).ShouldBe(sourceDate);
        ClientMergeRules.ChoosePatchTest(null, null).ShouldBeNull();
    }

    [Fact]
    public void A_target_that_consented_keeps_its_own_proof()
    {
        var target = new ConsentState(true, Now, "I agree to Studio 20 contacting me.");
        var source = new ConsentState(true, Now.Plus(Duration.FromDays(1)), "Other wording");

        ClientMergeRules.ChooseConsent(target, source).ShouldBe(target);
        ClientMergeRules.ChooseConsent(target, ConsentState.NotGranted).ShouldBe(target);
    }

    [Fact]
    public void A_target_without_consent_never_gains_it_from_the_duplicate()
    {
        var source = new ConsentState(true, Now, "I agree to Studio 20 contacting me.");

        var merged = ClientMergeRules.ChooseConsent(ConsentState.NotGranted, source);

        merged.Granted.ShouldBeFalse();
        merged.At.ShouldBeNull();
        merged.Wording.ShouldBeNull();
    }

    [Theory]
    [InlineData("+447700900123", "+447700900999", "+447700900123")]
    [InlineData("+447700900123", null, "+447700900123")]
    [InlineData(null, "+447700900999", "+447700900999")]
    [InlineData("", "+447700900999", "+447700900999")]
    [InlineData(null, null, null)]
    [InlineData(null, "", null)]
    public void Phone_and_email_are_the_targets_unless_it_has_none(string? target, string? source, string? expected)
    {
        ClientMergeRules.ChooseContact(target, source).ShouldBe(expected);
    }

    [Fact]
    public void Two_different_unmerged_cards_may_merge()
    {
        ClientMergeRules.Refusal(Card(), Card()).ShouldBeNull();
    }

    [Fact]
    public void A_card_cannot_be_merged_into_itself()
    {
        var card = Card();

        ClientMergeRules.Refusal(card, card).ShouldBe(MergeRefusal.SameClient);
    }

    [Fact]
    public void A_card_already_merged_away_takes_no_part_on_either_side()
    {
        var merged = Card();
        merged.MergedIntoClientId = Guid.NewGuid();

        ClientMergeRules.Refusal(Card(), merged).ShouldBe(MergeRefusal.SourceAlreadyMerged);
        ClientMergeRules.Refusal(merged, Card()).ShouldBe(MergeRefusal.TargetAlreadyMerged);
    }

    private static Client Card() => new() { FirstName = "Olivia", LastName = "Brown" };
}