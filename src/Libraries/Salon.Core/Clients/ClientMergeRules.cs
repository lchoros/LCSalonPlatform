using NodaTime;

namespace Salon.Core.Clients;

/// <summary>
/// The pure rules of merging a duplicate card (the source) into the one the salon keeps (the target), ADR 0017. No
/// database: the client service loads both rows, applies these and moves the appointments. The target always wins where
/// both have a value; the source only fills gaps. Consent is never gained by a merge.
/// </summary>
public static class ClientMergeRules
{
    /// <summary>Put between the target's and the source's notes when both exist.</summary>
    public const string NotesSeparator = "\n---\n";

    /// <summary>
    /// Both notes kept, target first, separated by <see cref="NotesSeparator"/>; either alone when the other is blank; the
    /// result is cut to <paramref name="maxLength"/> (the column) so a merge never fails on two long notes.
    /// </summary>
    public static string? UnionNotes(string? target, string? source, int maxLength = Client.NotesMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        var targetText = string.IsNullOrWhiteSpace(target) ? null : target.Trim();
        var sourceText = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        var union = (targetText, sourceText) switch
        {
            (null, null) => null,
            (not null, null) => targetText,
            (null, not null) => sourceText,
            _ => targetText + NotesSeparator + sourceText,
        };

        return union is { Length: var length } && length > maxLength ? union[..maxLength].TrimEnd() : union;
    }

    /// <summary>The target's cipher text unless it has none; then the source's, moved as it is (same key ring, no decryption needed).</summary>
    public static string? ChooseHealthNotes(string? targetProtected, string? sourceProtected)
        => string.IsNullOrEmpty(targetProtected) ? NullIfEmpty(sourceProtected) : targetProtected;

    /// <summary>The patch-test date travels with the health notes: the target's when it has one, otherwise the source's.</summary>
    public static LocalDate? ChoosePatchTest(LocalDate? target, LocalDate? source) => target ?? source;

    /// <summary>
    /// The stricter state: what the target already has. A target that never consented stays unconsented even if the
    /// duplicate did, because the salon cannot prove which wording that person agreed to for this card; a target that did
    /// consent keeps its own wording and instant as proof.
    /// </summary>
    public static ConsentState ChooseConsent(ConsentState target, ConsentState source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        return target.Granted ? target : ConsentState.NotGranted;
    }

    /// <summary>A phone or email: the target's when present, else the source's. The source's is not kept anywhere else.</summary>
    public static string? ChooseContact(string? target, string? source)
        => string.IsNullOrEmpty(target) ? NullIfEmpty(source) : target;

    /// <summary>
    /// Whether <paramref name="source"/> may be merged into <paramref name="target"/>: two different cards, neither of them
    /// already merged away. The service also requires both to belong to the current tenant (the filter does that).
    /// </summary>
    public static MergeRefusal? Refusal(Client target, Client source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (target.Id == source.Id)
        {
            return MergeRefusal.SameClient;
        }

        if (source.MergedIntoClientId is not null)
        {
            return MergeRefusal.SourceAlreadyMerged;
        }

        if (target.MergedIntoClientId is not null)
        {
            return MergeRefusal.TargetAlreadyMerged;
        }

        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Marketing consent as stored on a card: the flag with its proof (when, which wording).</summary>
public sealed record ConsentState(bool Granted, Instant? At, string? Wording)
{
    public static ConsentState NotGranted { get; } = new(false, null, null);
}

public enum MergeRefusal
{
    SameClient = 1,
    SourceAlreadyMerged = 2,
    TargetAlreadyMerged = 3,
}
