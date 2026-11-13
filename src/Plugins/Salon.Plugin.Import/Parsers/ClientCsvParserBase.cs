using Salon.Core.Clients;
using Salon.Core.Import;

namespace Salon.Plugin.Import.Parsers;

/// <summary>
/// What the Booksy and Versum parsers share (ADR 0019): header aliases per field, a score for detection, and the mapping
/// of a record to an <see cref="ImportedClientRow"/>. Values stay as typed apart from trimming; only what can be judged
/// without the salon's context is flagged here (missing or over-long names, a wrong field count).
/// </summary>
public abstract class ClientCsvParserBase : IClientImportParser
{
    /// <summary>A column only this product's export has counts this much more than a shared one when choosing the parser.</summary>
    private const int SignatureWeight = 10;

    private static readonly HashSet<string> YesValues = new(["yes", "y", "true", "1", "tak", "t", "x", "✓", "prawda"], StringComparer.Ordinal);

    public abstract string Format { get; }

    protected abstract HashSet<string> FirstNameAliases { get; }

    protected abstract HashSet<string> LastNameAliases { get; }

    protected abstract HashSet<string> FullNameAliases { get; }

    protected abstract HashSet<string> PhoneAliases { get; }

    protected abstract HashSet<string> EmailAliases { get; }

    protected abstract HashSet<string> NotesAliases { get; }

    protected abstract HashSet<string> ConsentAliases { get; }

    /// <summary>Columns that are not imported but identify the product (birthday, gender, source, last visit…).</summary>
    protected abstract HashSet<string> SignatureAliases { get; }

    public int Match(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var map = MapColumns(header);
        var hasName = (map.FirstName >= 0 && map.LastName >= 0) || map.FullName >= 0;
        var hasContact = map.Phone >= 0 || map.Email >= 0;
        if (!hasName || !hasContact)
        {
            return 0;
        }

        var recognised = new[] { map.FirstName, map.LastName, map.FullName, map.Phone, map.Email, map.Notes, map.Consent }.Count(i => i >= 0);
        return recognised + (map.Signatures * SignatureWeight);
    }

    public IReadOnlyList<ImportedClientRow> Parse(IReadOnlyList<string> header, IReadOnlyList<ImportRecord> records)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);

        var map = MapColumns(header);
        return records.Select(record => MapRow(record, map, header.Count)).ToList();
    }

    private static ImportedClientRow MapRow(ImportRecord record, ColumnMap map, int headerCount)
    {
        var fields = record.Fields;
        var problems = new List<ImportProblem>();
        if (fields.Count != headerCount)
        {
            problems.Add(ImportProblem.WrongColumnCount);
        }

        string firstName;
        string lastName;
        if (map.FirstName >= 0 || map.LastName >= 0)
        {
            firstName = Field(fields, map.FirstName);
            lastName = Field(fields, map.LastName);
        }
        else
        {
            (firstName, lastName) = SplitFullName(Field(fields, map.FullName));
        }

        if (firstName.Length == 0)
        {
            problems.Add(ImportProblem.MissingFirstName);
        }

        if (lastName.Length == 0)
        {
            problems.Add(ImportProblem.MissingLastName);
        }

        if (firstName.Length > Client.NameMaxLength || lastName.Length > Client.NameMaxLength)
        {
            problems.Add(ImportProblem.NameTooLong);
        }

        return new ImportedClientRow(
            record.SourceRow,
            firstName,
            lastName,
            NullIfEmpty(Field(fields, map.Phone)),
            NullIfEmpty(Field(fields, map.Email)),
            NullIfEmpty(Field(fields, map.Notes)),
            IsYes(Field(fields, map.Consent)),
            problems);
    }

    private static string Field(IReadOnlyList<string> fields, int index)
        => index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool IsYes(string value) => YesValues.Contains(value.ToLowerInvariant());

    /// <summary>"Olivia Rose Brown" → ("Olivia", "Rose Brown"); a single word is a first name with the last name missing.</summary>
    private static (string FirstName, string LastName) SplitFullName(string fullName)
    {
        var parts = fullName.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[0], parts[1]),
        };
    }

    private ColumnMap MapColumns(IReadOnlyList<string> header)
    {
        var map = new ColumnMap();
        for (var i = 0; i < header.Count; i++)
        {
            var key = HeaderKey.Of(header[i]);
            if (key.Length == 0)
            {
                continue;
            }

            // First column wins when an export repeats a heading.
            if (map.FirstName < 0 && FirstNameAliases.Contains(key))
            {
                map.FirstName = i;
            }
            else if (map.LastName < 0 && LastNameAliases.Contains(key))
            {
                map.LastName = i;
            }
            else if (map.FullName < 0 && FullNameAliases.Contains(key))
            {
                map.FullName = i;
            }
            else if (map.Phone < 0 && PhoneAliases.Contains(key))
            {
                map.Phone = i;
            }
            else if (map.Email < 0 && EmailAliases.Contains(key))
            {
                map.Email = i;
            }
            else if (map.Notes < 0 && NotesAliases.Contains(key))
            {
                map.Notes = i;
            }
            else if (map.Consent < 0 && ConsentAliases.Contains(key))
            {
                map.Consent = i;
            }
            else if (SignatureAliases.Contains(key))
            {
                map.Signatures++;
            }
        }

        return map;
    }

    private sealed class ColumnMap
    {
        public int FirstName { get; set; } = -1;

        public int LastName { get; set; } = -1;

        public int FullName { get; set; } = -1;

        public int Phone { get; set; } = -1;

        public int Email { get; set; } = -1;

        public int Notes { get; set; } = -1;

        public int Consent { get; set; } = -1;

        public int Signatures { get; set; }
    }
}