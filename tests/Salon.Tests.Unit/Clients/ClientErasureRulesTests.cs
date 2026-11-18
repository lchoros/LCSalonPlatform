using NodaTime;
using Salon.Core.Clients;

namespace Salon.Tests.Unit.Clients;

/// <summary>
/// Erasing a client (ADR 0062): what stands in the way, in the order the desk deals with it, and that scrubbing a
/// row leaves nothing that says who the person was.
/// </summary>
public class ClientErasureRulesTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 18, 10, 0);

    [Fact]
    public void A_client_with_nothing_open_can_be_erased()
    {
        ClientErasureRules.Blockers(false, false, 0, false, false).ShouldBeEmpty();
    }

    [Fact]
    public void Each_open_matter_is_named_visits_first_then_money()
    {
        ClientErasureRules.Blockers(false, false, 2, false, false).ShouldBe([ErasureBlocker.UpcomingVisit]);
        ClientErasureRules.Blockers(false, false, 0, true, false).ShouldBe([ErasureBlocker.DepositHeld]);
        ClientErasureRules.Blockers(false, false, 0, false, true).ShouldBe([ErasureBlocker.FeeOwed]);
        ClientErasureRules.Blockers(false, false, 1, true, true).ShouldBe([ErasureBlocker.UpcomingVisit, ErasureBlocker.DepositHeld, ErasureBlocker.FeeOwed]);
    }

    [Fact]
    public void A_card_already_erased_or_merged_away_says_only_that()
    {
        ClientErasureRules.Blockers(true, false, 3, true, true).ShouldBe([ErasureBlocker.AlreadyErased]);
        ClientErasureRules.Blockers(false, true, 3, true, true).ShouldBe([ErasureBlocker.MergedAway]);
        ClientErasureRules.Blockers(true, true, 0, false, false).ShouldBe([ErasureBlocker.AlreadyErased]);
    }

    [Fact]
    public void Scrubbing_leaves_nothing_that_says_who_the_person_was()
    {
        var userId = Guid.NewGuid();
        var client = new Client
        {
            FirstName = "Olivia",
            LastName = "Brown",
            Phone = "+447700900123",
            Email = "olivia@example.test",
            Notes = "Prefers afternoons.",
            HealthNotesProtected = "cipher-text",
            LastPatchTestOn = new LocalDate(2026, 10, 1),
            MarketingConsent = true,
            MarketingConsentAt = Now - Duration.FromDays(30),
            MarketingConsentWording = "I agree to offers by text.",
            DepositPercent = 25m,
            IsActive = true,
            CreatedAt = Now - Duration.FromDays(400),
            UpdatedAt = Now - Duration.FromDays(30),
        };
        var id = client.Id;

        ClientErasureRules.Scrub(client, "Erased", "client", Now, userId);

        (client.FirstName, client.LastName, client.FullName).ShouldBe(("Erased", "client", "Erased client"));
        (client.Phone, client.Email, client.Notes, client.HealthNotesProtected, client.LastPatchTestOn).ShouldBe((null, null, null, null, null));
        (client.MarketingConsent, client.MarketingConsentAt, client.MarketingConsentWording, client.DepositPercent).ShouldBe((false, null, null, null));
        (client.IsActive, client.ErasedAt, client.ErasedByUserId, client.UpdatedAt).ShouldBe((false, Now, userId, Now));
        (client.Id, client.CreatedAt).ShouldBe((id, Now - Duration.FromDays(400)), "the row and its age stay, so what points at it still resolves");
    }
}