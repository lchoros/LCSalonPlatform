using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Import;
using Salon.Services.Appointments.Import;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.ImportAppointments;

/// <summary>
/// Step 1 to 2 of reading the old system's diary in (ADR 0067): read the uploaded bytes from today on and classify
/// the rows against the salon. Reads only; the controller keeps the preview server-side until somebody confirms it.
/// </summary>
/// <param name="FileName">Shown on the preview and the result; never logged.</param>
public sealed record PreviewAppointmentImportQuery(ReadOnlyMemory<byte> Content, string FileName) : IQuery<PreviewAppointmentImportOutcome>
{
    public override string ToString() => $"{nameof(PreviewAppointmentImportQuery)} {{ Bytes = {Content.Length} }}";
}

/// <summary>Either a reason the whole file is refused, or the classified preview.</summary>
public sealed record PreviewAppointmentImportOutcome(AppointmentImportParseResult Parsed, AppointmentImportPreview? Preview);

/// <summary>
/// Step 2 to 3: write the visits the rows still make new. A command, so the pipeline wraps the visits, their history
/// and their events in one transaction, rolled back whole when the database refuses a clash.
/// </summary>
public sealed record ConfirmAppointmentImportCommand(AppointmentImportPreview Preview, Guid? ByUserId) : ICommand<AppointmentImportResult>
{
    public override string ToString() => $"{nameof(ConfirmAppointmentImportCommand)} {{ New = {Preview.NewCount} }}";
}

/// <summary>A file must be there and within the byte cap before the reader sees it.</summary>
public sealed class PreviewAppointmentImportValidator : AbstractValidator<PreviewAppointmentImportQuery>
{
    public PreviewAppointmentImportValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        RuleFor(q => q.FileName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Appointments.Import.Validation.FileRequired"]);
        RuleFor(q => q.Content)
            .Must(content => content.Length > 0)
            .WithMessage(_ => localizer["Appointments.Import.Validation.FileRequired"])
            .Must(content => content.Length <= AppointmentImportLimits.MaxBytes)
            .WithMessage(_ => localizer.Format("Appointments.Import.Error.TooLarge", ImportAppointmentsController.MaxMegabytes));
    }
}

/// <summary>The salon's today first (rows before it are history), then the reader (pure), then the service.</summary>
public sealed class PreviewAppointmentImportHandler(IAppointmentImportProvider provider, IAppointmentImportService imports)
    : IRequestHandler<PreviewAppointmentImportQuery, PreviewAppointmentImportOutcome>
{
    public async Task<PreviewAppointmentImportOutcome> HandleAsync(PreviewAppointmentImportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = provider.Parse(request.Content, await imports.FirstDayAsync(cancellationToken));
        if (!parsed.IsSuccess)
        {
            return new PreviewAppointmentImportOutcome(parsed, null);
        }

        return new PreviewAppointmentImportOutcome(parsed, await imports.PreviewAsync(parsed, request.FileName, cancellationToken));
    }
}

/// <summary>Hands the preview to <see cref="IAppointmentImportService"/>, which decides again against the diary as it is now.</summary>
public sealed class ConfirmAppointmentImportHandler(IAppointmentImportService imports) : IRequestHandler<ConfirmAppointmentImportCommand, AppointmentImportResult>
{
    public Task<AppointmentImportResult> HandleAsync(ConfirmAppointmentImportCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return imports.CommitAsync(request.Preview, request.ByUserId, cancellationToken);
    }
}