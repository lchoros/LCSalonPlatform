using FluentValidation;

using NodaTime.Text;

using Salon.Core.Clients;
using Salon.Services.Clients;
using Salon.Services.Localization;

namespace Salon.Web.Features.Clients.CreateClient;

/// <summary>
/// Field rules for <see cref="CreateClientCommand"/> that need no database or tenant: names, email shape, text caps and
/// the ISO patch-test date. Messages come from resource keys, resolved in the request culture.
/// </summary>
public sealed class CreateClientValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.FirstName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Clients.Create.Validation.FirstNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Create.Validation.FirstNameTooLong", Client.NameMaxLength));

        RuleFor(c => c.LastName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Clients.Create.Validation.LastNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Create.Validation.LastNameTooLong", Client.NameMaxLength));

        RuleFor(c => c.Email)
            .Must(email => EmailAddress.TryNormalise(email, out _))
            .WithMessage(_ => localizer["Clients.Create.Validation.EmailInvalid"]);

        RuleFor(c => c.Notes)
            .Must(notes => notes is null || notes.Trim().Length <= Client.NotesMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Create.Validation.NotesTooLong", Client.NotesMaxLength));

        RuleFor(c => c.HealthNotes)
            .Must(notes => notes is null || notes.Trim().Length <= Client.HealthNotesMaxLength)
            .WithMessage(_ => localizer.Format("Clients.Create.Validation.HealthNotesTooLong", Client.HealthNotesMaxLength));

        RuleFor(c => c.LastPatchTestOn)
            .Must(date => string.IsNullOrWhiteSpace(date) || LocalDatePattern.Iso.Parse(date.Trim()).Success)
            .WithMessage(_ => localizer["Clients.Create.Validation.LastPatchTestOnInvalid"]);
    }
}