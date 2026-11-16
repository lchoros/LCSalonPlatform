using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.Consultations;

/// <summary>Whose consultation form it would be (ADR 0058); null when the salon has no such client.</summary>
public sealed record ConsultationClientQuery(Guid ClientId) : IQuery<ConsultationClient?>;

/// <summary>One consultation with its answers decrypted (ADR 0058); null when this client has none with this id.</summary>
public sealed record ConsultationQuery(Guid ClientId, Guid ConsultationId) : IQuery<ConsultationDetails?>;

/// <summary>
/// The consultation form as posted (ADR 0058). <see cref="RecordConsultationValidator"/> holds the field rules. A
/// command: the record and the card's patch test date commit together.
/// </summary>
/// <param name="Answers">Yes or no per question key; a question left out is a form not finished.</param>
/// <param name="PatchTestOn">ISO date; empty when no test was done.</param>
/// <param name="ConfirmedBy">The client's name, typed by the client.</param>
/// <param name="Agreed">The tick beside the consent sentence.</param>
/// <param name="RecordId">The id the form was given; the same form sent twice is one record.</param>
/// <param name="ConsentWording">The sentence as shown; set by the controller, never taken from the post.</param>
public sealed record RecordConsultationCommand(
    Guid ClientId,
    string? For,
    Dictionary<string, bool>? Answers,
    string? Details,
    string? PatchTestOn,
    string? ConfirmedBy,
    bool Agreed,
    Guid? RecordId = null,
    Guid? ByUserId = null,
    string? ConsentWording = null) : ICommand<ConsultationResult>
{
    /// <summary>An ISO date as typed; null for an empty field. An unreadable one is the validator's to refuse.</summary>
    public static LocalDate? Day(string? text)
        => !string.IsNullOrWhiteSpace(text) && LocalDatePattern.Iso.Parse(text.Trim()) is { Success: true } parsed ? parsed.Value : null;

    /// <summary>Never the answers: a command must not end up in a log line because somebody printed it.</summary>
    public override string ToString() => $"{nameof(RecordConsultationCommand)} {{ ClientId = {ClientId} }}";
}

public sealed class RecordConsultationValidator : AbstractValidator<RecordConsultationCommand>
{
    public RecordConsultationValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Answers)
            .Must(answers => ConsultationRules.IsComplete(answers))
            .WithMessage(_ => localizer["Clients.Consultation.Validation.Answers"]);

        RuleFor(c => c.For)
            .Must(ConsultationRules.ForFits)
            .WithMessage(_ => localizer.Format("Clients.Consultation.Validation.For", ConsultationRecord.ForMaxLength));

        RuleFor(c => c.Details)
            .Must(ConsultationRules.DetailsFit)
            .WithMessage(_ => localizer.Format("Clients.Consultation.Validation.Details", ConsultationRecord.DetailsMaxLength));

        RuleFor(c => c.PatchTestOn)
            .Must(text => string.IsNullOrWhiteSpace(text) || RecordConsultationCommand.Day(text) is not null)
            .WithMessage(_ => localizer["Clients.Consultation.Validation.PatchTestOn"]);

        RuleFor(c => c.ConfirmedBy)
            .Must(name => ConsultationRules.IsConfirmed(name, agreed: true))
            .WithMessage(_ => localizer["Clients.Consultation.Validation.ConfirmedBy"]);

        RuleFor(c => c.Agreed)
            .Equal(true)
            .WithMessage(_ => localizer["Clients.Consultation.Validation.Agreed"]);
    }
}

public sealed class ConsultationClientHandler(IConsultationService consultations) : IRequestHandler<ConsultationClientQuery, ConsultationClient?>
{
    public Task<ConsultationClient?> HandleAsync(ConsultationClientQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return consultations.FindClientAsync(request.ClientId, cancellationToken);
    }
}

public sealed class ConsultationHandler(IConsultationService consultations) : IRequestHandler<ConsultationQuery, ConsultationDetails?>
{
    public Task<ConsultationDetails?> HandleAsync(ConsultationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return consultations.GetAsync(request.ClientId, request.ConsultationId, cancellationToken);
    }
}

/// <summary>Hands the form to <see cref="IConsultationService"/>, the only place the answers are encrypted (ADR 0058).</summary>
public sealed class RecordConsultationHandler(IConsultationService consultations) : IRequestHandler<RecordConsultationCommand, ConsultationResult>
{
    public Task<ConsultationResult> HandleAsync(RecordConsultationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return consultations.RecordAsync(
            new ConsultationRequest(
                request.ClientId,
                request.For,
                request.Answers,
                request.Details,
                RecordConsultationCommand.Day(request.PatchTestOn),
                request.ConfirmedBy,
                request.Agreed,
                request.ConsentWording ?? string.Empty,
                request.RecordId),
            request.ByUserId,
            cancellationToken);
    }
}

/// <summary>
/// Consultation forms on a client's card (ADR 0058): the form at <c>/admin/clients/{clientId}/consultations/new</c>
/// and one kept record at <c>/admin/clients/{clientId}/consultations/{id}</c>. Tenant only, for the logins that may
/// open the card (<see cref="Permissions.ClientsView"/>), which is where the health notes already are (ADR 0011).
/// Another salon's client or record is a 404: the tenant filter makes it not exist here. The pages are never cached.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/clients/{clientId:guid}/consultations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConsultationsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The sentence the client agrees to; kept with the record exactly as it was shown.</summary>
    public const string ConsentKey = "Clients.Consultation.Consent";

    /// <summary>The client's card; the ClientCard slice owns the route.</summary>
    public static string CardPath(Guid clientId) => $"/admin/clients/{clientId}";

    [HttpGet("new")]
    public async Task<IActionResult> Form(Guid clientId, CancellationToken cancellationToken)
    {
        var client = await dispatcher.SendAsync(new ConsultationClientQuery(clientId), cancellationToken);
        if (client is null || !client.CanRecord)
        {
            return NotFound();
        }

        return View(new ConsultationFormViewModel(client, new RecordConsultationCommand(clientId, null, null, null, null, null, false)));
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(Guid clientId, RecordConsultationCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var client = await dispatcher.SendAsync(new ConsultationClientQuery(clientId), cancellationToken);
        if (client is null || !client.CanRecord)
        {
            return NotFound();
        }

        // An answer that is neither yes nor no never reaches the pipeline; say it our way.
        if (!ModelState.IsValid)
        {
            ModelState.Clear();
            ModelState.AddModelError(nameof(RecordConsultationCommand.Answers), localizer["Clients.Consultation.Validation.Answers"]);
            return View(new ConsultationFormViewModel(client, form with { ClientId = clientId }));
        }

        var command = form with { ClientId = clientId, ByUserId = CurrentUserId(), ConsentWording = localizer[ConsentKey] };
        try
        {
            var result = await dispatcher.SendAsync(command, cancellationToken);
            switch (result.Outcome)
            {
                case ConsultationOutcome.Recorded:
                    SetFlash(localizer["Clients.Consultation.Saved"]);
                    return Redirect($"{CardPath(clientId)}/consultations/{result.Id}");
                case ConsultationOutcome.ClientUnknown:
                    return NotFound();
                case ConsultationOutcome.PatchTestDayInvalid:
                    ModelState.AddModelError(nameof(RecordConsultationCommand.PatchTestOn), localizer["Clients.Consultation.Validation.PatchTestFuture"]);
                    break;
                default:
                    ModelState.AddModelError(string.Empty, localizer[$"Clients.Consultation.Refused.{result.Outcome}"]);
                    break;
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
        }

        return View(new ConsultationFormViewModel(client, command));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid clientId, Guid id, CancellationToken cancellationToken)
    {
        var details = await dispatcher.SendAsync(new ConsultationQuery(clientId, id), cancellationToken);
        return details is null ? NotFound() : View(new ConsultationViewModel(details, TakeFlash()));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

public sealed record ConsultationFormViewModel(ConsultationClient Client, RecordConsultationCommand Form)
{
    /// <summary>
    /// The id this form's record will have (ADR 0058). Kept across a refused post, so the same form sent twice, by a
    /// second click or a reload, is one record.
    /// </summary>
    public Guid RecordId { get; } = Form.RecordId ?? Guid.CreateVersion7();

    public string CardPath => ConsultationsController.CardPath(Client.Id);

    /// <summary>Each question with what was already answered on a form shown again; null for not answered yet.</summary>
    public IReadOnlyList<ConsultationQuestionField> Questions { get; } =
        [.. ConsultationQuestions.All.Select(key => new ConsultationQuestionField(key, Form.Answers is { } answers && answers.TryGetValue(key, out var yes) ? yes : null))];
}

/// <param name="Key">The question's key, which is also the name of its answer in the post.</param>
/// <param name="Answer">True for yes, false for no, null for not answered.</param>
public sealed record ConsultationQuestionField(string Key, bool? Answer)
{
    public string TextKey => ConsultationText.QuestionKey(Key);

    public string FieldName => $"Answers[{Key}]";
}

public sealed record ConsultationViewModel(ConsultationDetails Details, string? Flash)
{
    public string CardPath => ConsultationsController.CardPath(Details.ClientId);
}