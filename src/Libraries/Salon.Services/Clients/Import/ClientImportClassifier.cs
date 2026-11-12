using Salon.Core.Import;

namespace Salon.Services.Clients.Import;

/// <summary>The salon's current clients, reduced to the three keys the classifier matches on (ADR 0019).</summary>
internal sealed record ExistingClients(IReadOnlySet<string> Phones, IReadOnlySet<string> Emails, IReadOnlySet<string> Names)
{
    /// <summary>Case-insensitive "first last" key for rows that have no phone or email to match on.</summary>
    public static string NameKey(string firstName, string lastName) => throw new NotImplementedException();
}

/// <summary>Pure classification of parsed rows against the salon's clients (ADR 0019).</summary>
internal static class ClientImportClassifier
{
    public static IReadOnlyList<ClassifiedImportRow> Classify(IReadOnlyList<ImportedClientRow> rows, string? currencyHint, ExistingClients existing)
        => throw new NotImplementedException();
}
