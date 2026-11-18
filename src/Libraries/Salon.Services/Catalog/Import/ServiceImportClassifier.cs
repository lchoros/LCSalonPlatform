using Salon.Core.Catalog;
using Salon.Core.Import;
using Salon.Core.Payments;

namespace Salon.Services.Catalog.Import;

/// <summary>A service the salon has, as far as a price list file can change it (ADR 0066).</summary>
/// <param name="DepositPercent">Null follows the salon's own rule.</param>
internal sealed record ExistingService(Guid Id, string Name, int Minutes, decimal Price, decimal? DepositPercent)
{
    /// <summary>Time inside the service when the client waits; the service cannot become shorter than it needs.</summary>
    public int ProcessingMinutes { get; init; }

    public int ProcessingStartsAfterMinutes { get; init; }
}

/// <summary>
/// Pure classification of the rows of a price list file against the salon's services (ADR 0066). Invalid wins over
/// everything; a name repeated in the file is taken once, the first row. A service the salon has is matched by its
/// name without case and is "changed" when the file gives another time, price or deposit, "unchanged" otherwise: an
/// empty cell means "leave it as it is". A service the salon does not have needs a time and a price. The rules are
/// <see cref="ServiceRules"/>, the ones the service form uses, so what the preview calls new or changed will save.
/// </summary>
internal static class ServiceImportClassifier
{
    /// <summary>A name as it is compared: trimmed and without case, because "Cut" and "cut" are one service to the desk.</summary>
    public static string NameKey(string? name) => ServiceRules.NormalizeName(name).ToUpperInvariant();

    /// <param name="existing">The salon's services by <see cref="NameKey"/>, in use or not: a service not in use still owns its name.</param>
    public static IReadOnlyList<ClassifiedServiceRow> Classify(IReadOnlyList<ImportedServiceRow> rows, IReadOnlyDictionary<string, ExistingService> existing)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(existing);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ClassifiedServiceRow>(rows.Count);
        foreach (var row in rows)
        {
            var problems = new List<ServiceImportProblem>(row.Problems);
            var key = NameKey(row.Name);
            var has = existing.TryGetValue(key, out var service);
            Check(!ServiceRules.HasName(row.Name), ServiceImportProblem.MissingName);
            Check(!ServiceRules.NameFits(row.Name), ServiceImportProblem.NameTooLong);
            Check(!ServiceRules.CategoryNameFits(row.Category), ServiceImportProblem.CategoryTooLong);
            Check(row.Minutes is { } minutes && !(ServiceRules.IsDurationInRange(minutes) && ServiceRules.IsOnStep(minutes)), ServiceImportProblem.InvalidMinutes);

            // A service with time for the colour to develop cannot be made shorter than that time needs.
            Check(service is not null && row.Minutes is { } shorter && !ServiceRules.ProcessingFitsInside(shorter, service.ProcessingMinutes, service.ProcessingStartsAfterMinutes), ServiceImportProblem.InvalidMinutes);
            Check(row.Price is { } price && !(ServiceRules.IsPriceInRange(price) && ServiceRules.HasAtMostTwoDecimals(price)), ServiceImportProblem.InvalidPrice);
            Check(!DepositRules.IsPercentInRange(row.DepositPercent), ServiceImportProblem.InvalidDeposit);

            // Only a service that is new needs both numbers; whether a cell could not be read is said already.
            Check(!has && row.Minutes is null && !problems.Contains(ServiceImportProblem.InvalidMinutes), ServiceImportProblem.MissingMinutes);
            Check(!has && row.Price is null && !problems.Contains(ServiceImportProblem.InvalidPrice), ServiceImportProblem.MissingPrice);
            if (problems.Count > 0)
            {
                result.Add(new ClassifiedServiceRow(row, ServiceImportStatus.Invalid, problems));
                continue;
            }

            if (!seen.Add(key))
            {
                result.Add(new ClassifiedServiceRow(row, ServiceImportStatus.Repeated, [ServiceImportProblem.RepeatedInFile]));
            }
            else if (service is null)
            {
                result.Add(new ClassifiedServiceRow(row, ServiceImportStatus.New, []));
            }
            else
            {
                var change = Change(service, row);
                result.Add(new ClassifiedServiceRow(row, change.Any ? ServiceImportStatus.Changed : ServiceImportStatus.Unchanged, [], change));
            }

            void Check(bool wrong, ServiceImportProblem problem)
            {
                if (wrong && !problems.Contains(problem))
                {
                    problems.Add(problem);
                }
            }
        }

        return result;
    }

    /// <summary>What a row says against the service as it is; an empty cell keeps what is there.</summary>
    public static ServiceChange Change(ExistingService service, ImportedServiceRow row)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(row);
        return new ServiceChange(
            service.Id,
            service.Minutes,
            row.Minutes ?? service.Minutes,
            service.Price,
            row.Price ?? service.Price,
            service.DepositPercent,
            row.DepositPercent ?? service.DepositPercent);
    }
}