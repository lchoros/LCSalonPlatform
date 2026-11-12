using System.Net.Mail;

using FluentValidation;

using Salon.Core.Staff;
using Salon.Services.Localization;

namespace Salon.Web.Features.Staff.EditStaff;

/// <summary>Input rules for <see cref="EditStaffCommand"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class EditStaffValidator : AbstractValidator<EditStaffCommand>
{
    public EditStaffValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Staff.Create.Validation.NameRequired"])
            .Must(name => name is null || name.Trim().Length <= StaffMember.DisplayNameMaxLength)
            .WithMessage(_ => localizer.Format("Staff.Create.Validation.NameTooLong", StaffMember.DisplayNameMaxLength));

        RuleFor(c => c.Email)
            .Must(email => string.IsNullOrWhiteSpace(email) || email.Trim().Length <= StaffMember.EmailMaxLength)
            .WithMessage(_ => localizer.Format("Staff.Create.Validation.EmailTooLong", StaffMember.EmailMaxLength))
            .Must(email => string.IsNullOrWhiteSpace(email) || IsEmail(email.Trim()))
            .WithMessage(_ => localizer["Staff.Create.Validation.EmailInvalid"]);

        RuleFor(c => c.Phone)
            .Must(phone => string.IsNullOrWhiteSpace(phone) || phone.Trim().Length <= StaffMember.PhoneMaxLength)
            .WithMessage(_ => localizer.Format("Staff.Create.Validation.PhoneTooLong", StaffMember.PhoneMaxLength));

        RuleFor(c => c.Colour)
            .Must(StaffMember.IsValidColour)
            .WithMessage(_ => localizer["Staff.Create.Validation.ColourInvalid"]);
    }

    /// <summary>A plain address only: "Anna &lt;anna@example.com&gt;" parses but is not what the field asks for.</summary>
    private static bool IsEmail(string candidate)
        => MailAddress.TryCreate(candidate, out var address) && string.Equals(address.Address, candidate, StringComparison.Ordinal);
}