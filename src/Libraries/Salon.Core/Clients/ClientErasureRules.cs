namespace Salon.Core.Clients;

/// <summary>Why a client cannot be erased yet (ADR 0062). Each is something the desk can settle first.</summary>
public enum ErasureBlocker
{
    /// <summary>The client has a visit still to come: cancel it first, so nobody waits for a person with no name.</summary>
    UpcomingVisit = 1,

    /// <summary>The salon holds the client's deposit: give it back first.</summary>
    DepositHeld = 2,

    /// <summary>The client owes a fee: take the payment or waive it first.</summary>
    FeeOwed = 3,

    /// <summary>The client was erased before.</summary>
    AlreadyErased = 4,

    /// <summary>The card was merged into another: that card is the client now, erase it instead.</summary>
    MergedAway = 5,
}

/// <summary>
/// When a client can be erased, and what erasing leaves (ADR 0062). Pure. Erasing takes away everything that says
/// who the person is or anything about them; it keeps what the salon must keep to show its books add up: that a
/// visit took place, what it cost and how it was paid, pointing at a row with no name.
/// </summary>
public static class ClientErasureRules
{
    /// <summary>Everything that stands in the way, in the order the desk would deal with it; empty when the client can be erased.</summary>
    public static IReadOnlyList<ErasureBlocker> Blockers(bool alreadyErased, bool mergedAway, int upcomingVisits, bool depositHeld, bool feeOwed)
    {
        if (alreadyErased)
        {
            return [ErasureBlocker.AlreadyErased];
        }

        if (mergedAway)
        {
            return [ErasureBlocker.MergedAway];
        }

        var blockers = new List<ErasureBlocker>(3);
        if (upcomingVisits > 0)
        {
            blockers.Add(ErasureBlocker.UpcomingVisit);
        }

        if (depositHeld)
        {
            blockers.Add(ErasureBlocker.DepositHeld);
        }

        if (feeOwed)
        {
            blockers.Add(ErasureBlocker.FeeOwed);
        }

        return blockers;
    }

    /// <summary>
    /// Takes everything personal off the row and marks it erased. The names become <paramref name="firstName"/> and
    /// <paramref name="lastName"/>, words that say "erased client" in the salon's language: lists that still show
    /// the row (a till line, a past visit) say what happened instead of showing a blank.
    /// </summary>
    public static void Scrub(Client client, string firstName, string lastName, NodaTime.Instant now, Guid? byUserId)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.FirstName = firstName;
        client.LastName = lastName;
        client.Phone = null;
        client.Email = null;
        client.Notes = null;
        client.HealthNotesProtected = null;
        client.LastPatchTestOn = null;
        client.MarketingConsent = false;
        client.MarketingConsentAt = null;
        client.MarketingConsentWording = null;
        client.DepositPercent = null;
        client.IsActive = false;
        client.ErasedAt = now;
        client.ErasedByUserId = byUserId;
        client.UpdatedAt = now;
    }
}