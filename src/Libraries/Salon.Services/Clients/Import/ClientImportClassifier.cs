using Salon.Core.Import;

namespace Salon.Services.Clients.Import;

/// <summary>The salon's current clients, reduced to the three keys the classifier matches on (ADR 0019).</summary>
internal sealed record ExistingClients(IReadOnlySet<string> Phones, IReadOnlySet<string> Emails, IReadOnlySet<string> Names)
{
    /// <summary>Case-insensitive "first last" key for rows that have no phone or email to match on.</summary>
    public static string NameKey(string firstName, string lastName)
    {
        ArgumentNullException.ThrowIfNull(firstName);
        ArgumentNullException.ThrowIfNull(lastName);
        return (firstName.Trim() + " " + lastName.Trim()).ToUpperInvariant();
    }
}

/// <summary>
/// Pure classification of parsed rows against the salon's clients (ADR 0019). Invalid (parser problems, a phone or email
/// the client service would refuse) wins over duplicate; duplicate against the salon (normalised phone, email, or the name
/// when the row has neither) wins over duplicate inside the file; everything else is new and reserves its keys for the
/// rows that follow. Uses the same normalisation as <see cref="ClientService"/>, so what the preview calls new will create.
/// </summary>
internal static class ClientImportClassifier
{
    public static IReadOnlyList<ClassifiedImportRow> Classify(IReadOnlyList<ImportedClientRow> rows, string? currencyHint, ExistingClients existing)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(existing);

        var seenPhones = new HashSet<string>(StringComparer.Ordinal);
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ClassifiedImportRow>(rows.Count);

        foreach (var row in rows)
        {
            var problems = new List<ImportProblem>(row.Problems);
            if (!PhoneNumber.TryNormalise(row.Phone, currencyHint, out var phone))
            {
                problems.Add(ImportProblem.InvalidPhone);
            }

            if (!EmailAddress.TryNormalise(row.Email, out var email))
            {
                problems.Add(ImportProblem.InvalidEmail);
            }

            if (problems.Count > 0)
            {
                result.Add(new ClassifiedImportRow(row, ImportRowStatus.Invalid, problems, phone, email));
                continue;
            }

            var contactless = phone is null && email is null;
            var nameKey = ExistingClients.NameKey(row.FirstName, row.LastName);

            if (phone is not null && existing.Phones.Contains(phone))
            {
                problems.Add(ImportProblem.DuplicatePhone);
            }

            if (email is not null && existing.Emails.Contains(email))
            {
                problems.Add(ImportProblem.DuplicateEmail);
            }

            if (contactless && existing.Names.Contains(nameKey))
            {
                problems.Add(ImportProblem.DuplicateName);
            }

            if (problems.Count == 0
                && ((phone is not null && seenPhones.Contains(phone))
                    || (email is not null && seenEmails.Contains(email))
                    || (contactless && seenNames.Contains(nameKey))))
            {
                problems.Add(ImportProblem.DuplicateInFile);
            }

            if (problems.Count > 0)
            {
                result.Add(new ClassifiedImportRow(row, ImportRowStatus.Duplicate, problems, phone, email));
                continue;
            }

            if (phone is not null)
            {
                seenPhones.Add(phone);
            }

            if (email is not null)
            {
                seenEmails.Add(email);
            }

            seenNames.Add(nameKey);
            result.Add(new ClassifiedImportRow(row, ImportRowStatus.New, problems, phone, email));
        }

        return result;
    }
}