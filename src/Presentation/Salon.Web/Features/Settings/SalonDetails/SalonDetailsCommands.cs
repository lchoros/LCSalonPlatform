using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Locations;
using Salon.Services.Localization;
using Salon.Services.Locations;

namespace Salon.Web.Features.Settings.SalonDetails;

/// <summary>The salon's address and contact details as the form shows them (ADR 0027); all blank before the first save.</summary>
public sealed record GetSalonDetailsQuery : IQuery<SaveSalonDetailsCommand>;

/// <summary>
/// The salon details form as posted: where clients find the salon and how they reach it.
/// <see cref="SaveSalonDetailsValidator"/> holds the rules.
/// </summary>
public sealed record SaveSalonDetailsCommand(
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? Postcode,
    string? Phone,
    string? Email) : ICommand<SalonLocation>
{
    public static SaveSalonDetailsCommand Empty { get; } = new(null, null, null, null, null, null, null);

    public static SaveSalonDetailsCommand From(SalonLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new SaveSalonDetailsCommand(location.Name, location.AddressLine1, location.AddressLine2, location.City, location.Postcode, location.Phone, location.Email);
    }
}

public sealed class SaveSalonDetailsValidator : AbstractValidator<SaveSalonDetailsCommand>
{
    public SaveSalonDetailsValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        string TooLong(int max) => localizer.Format("Settings.SalonDetails.Validation.TooLong", max);

        RuleFor(c => c.Name)
            .Must(name => Fits(name, Location.NameMaxLength)).WithMessage(_ => TooLong(Location.NameMaxLength));

        RuleFor(c => c.AddressLine1)
            .Cascade(CascadeMode.Stop)
            .Must(line => LocationRules.Normalize(line) is not null).WithMessage(_ => localizer["Settings.SalonDetails.Validation.AddressRequired"])
            .Must(line => Fits(line, Location.AddressLineMaxLength)).WithMessage(_ => TooLong(Location.AddressLineMaxLength));

        RuleFor(c => c.AddressLine2)
            .Must(line => Fits(line, Location.AddressLineMaxLength)).WithMessage(_ => TooLong(Location.AddressLineMaxLength));

        RuleFor(c => c.City)
            .Cascade(CascadeMode.Stop)
            .Must(city => LocationRules.Normalize(city) is not null).WithMessage(_ => localizer["Settings.SalonDetails.Validation.CityRequired"])
            .Must(city => Fits(city, Location.CityMaxLength)).WithMessage(_ => TooLong(Location.CityMaxLength));

        RuleFor(c => c.Postcode)
            .Cascade(CascadeMode.Stop)
            .Must(postcode => LocationRules.Normalize(postcode) is not null).WithMessage(_ => localizer["Settings.SalonDetails.Validation.PostcodeRequired"])
            .Must(postcode => Fits(postcode, Location.PostcodeMaxLength)).WithMessage(_ => TooLong(Location.PostcodeMaxLength));

        RuleFor(c => c.Phone)
            .Must(phone => LocationRules.Normalize(phone) is not { } number || (number.Length <= Location.PhoneMaxLength && LocationRules.IsPhone(number)))
            .WithMessage(_ => localizer["Settings.SalonDetails.Validation.Phone"]);

        RuleFor(c => c.Email)
            .Cascade(CascadeMode.Stop)
            .Must(email => Fits(email, Location.EmailMaxLength)).WithMessage(_ => TooLong(Location.EmailMaxLength))
            .EmailAddress().When(c => LocationRules.Normalize(c.Email) is not null).WithMessage(_ => localizer["Settings.SalonDetails.Validation.Email"]);
    }

    private static bool Fits(string? text, int maxLength) => LocationRules.Normalize(text) is not { } value || value.Length <= maxLength;
}

public sealed class GetSalonDetailsHandler(ILocationService locations) : IRequestHandler<GetSalonDetailsQuery, SaveSalonDetailsCommand>
{
    public async Task<SaveSalonDetailsCommand> HandleAsync(GetSalonDetailsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await locations.GetMainAsync(cancellationToken) is { } location ? SaveSalonDetailsCommand.From(location) : SaveSalonDetailsCommand.Empty;
    }
}

public sealed class SaveSalonDetailsHandler(ILocationService locations) : IRequestHandler<SaveSalonDetailsCommand, SalonLocation>
{
    public Task<SalonLocation> HandleAsync(SaveSalonDetailsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; the service trims and repeats the checks that keep the table sound.
        return locations.SaveMainAsync(
            new LocationDetails(request.Name, request.AddressLine1, request.AddressLine2, request.City, request.Postcode, request.Phone, request.Email),
            cancellationToken);
    }
}