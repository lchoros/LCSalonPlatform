using Salon.Core.Clients;
using Salon.Core.Import;
using Salon.Services.Clients.Import;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>What the import preview says about a row (ADR 0019, ADR 0043): the words, kept out of the view.</summary>
public static class ImportText
{
    public static string Status(ILocalizationService localizer, ImportRowStatus status)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return status switch
        {
            ImportRowStatus.New => localizer["Clients.Import.Status.New"],
            ImportRowStatus.Duplicate => localizer["Clients.Import.Status.Duplicate"],
            _ => localizer["Clients.Import.Status.Invalid"],
        };
    }

    public static string Problem(ILocalizationService localizer, ImportProblem problem)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return problem switch
        {
            ImportProblem.WrongColumnCount => localizer["Clients.Import.Problem.WrongColumnCount"],
            ImportProblem.MissingFirstName => localizer["Clients.Import.Problem.MissingFirstName"],
            ImportProblem.MissingLastName => localizer["Clients.Import.Problem.MissingLastName"],
            ImportProblem.NameTooLong => localizer.Format("Clients.Import.Problem.NameTooLong", Client.NameMaxLength),
            ImportProblem.InvalidPhone => localizer["Clients.Import.Problem.InvalidPhone"],
            ImportProblem.InvalidEmail => localizer["Clients.Import.Problem.InvalidEmail"],
            ImportProblem.DuplicatePhone => localizer["Clients.Import.Problem.DuplicatePhone"],
            ImportProblem.DuplicateEmail => localizer["Clients.Import.Problem.DuplicateEmail"],
            ImportProblem.DuplicateName => localizer["Clients.Import.Problem.DuplicateName"],
            ImportProblem.DuplicateInFile => localizer["Clients.Import.Problem.DuplicateInFile"],
            _ => problem.ToString(),
        };
    }

    /// <summary>The phone and the email as they will be stored, or a dash when the row has neither.</summary>
    public static string Contact(ClassifiedImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var parts = new[] { row.NormalisedPhone ?? row.Row.Phone, row.NormalisedEmail ?? row.Row.Email }.Where(p => !string.IsNullOrEmpty(p));
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? "—" : text;
    }
}