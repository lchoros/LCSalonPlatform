using FluentValidation;

using NodaTime.Text;

using Salon.Core.Clients;
using Salon.Core.Payments;
using Salon.Services.Clients;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>
/// Field rules for <see cref="EditClientCommand"/> that need no database or tenant: names, email shape, text caps and
/// the ISO patch-test date. The same rules as the create form; messages come from resource keys, resolved in the request culture.
/// </summary>
public sealed class EditClientValidator : AbstractValidator<EditClientCommand>
{
    public EditClientValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.DepositPercent)
            .Must(percent => percent is null or (>= 0 and <= (int)DepositRules.MaxPercent))
            .WithMessage(_ => localizer["Clients.Edit.Validation.DepositPercentRange"]);

        RuleFor(c => c.FirstName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Clients.Edit.Validation.FirstNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Edit.Validation.FirstNameTooLong", Client.NameMaxLength));

        RuleFor(c => c.LastName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Clients.Edit.Validation.LastNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Edit.Validation.LastNameTooLong", Client.NameMaxLength));

        RuleFor(c => c.Email)
            .Must(email => EmailAddress.TryNormalise(email, out _))
            .WithMessage(_ => localizer["Clients.Edit.Validation.EmailInvalid"]);

        RuleFor(c => c.Notes)
            .Must(notes => notes is null || notes.Trim().Length <= Client.NotesMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Edit.Validation.NotesTooLong", Client.NotesMaxLength));

        RuleFor(c => c.HealthNotes)
            .Must(notes => notes is null || notes.Trim().Length <= Client.HealthNotesMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Edit.Validation.HealthNotesTooLong", Client.HealthNotesMaxLength));

        RuleFor(c => c.LastPatchTestOn)
            .Must(date => string.IsNullOrWhiteSpace(date) || LocalDatePattern.Iso.Parse(date.Trim()).Success)
            .WithMessage(_ => localizer["Clients.Edit.Validation.LastPatchTestOnInvalid"]);
    }
}