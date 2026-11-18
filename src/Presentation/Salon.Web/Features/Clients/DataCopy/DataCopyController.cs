using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.DataCopy;

/// <summary>Everything the salon holds about one client (ADR 0062); null when the salon has no such client.</summary>
/// <param name="ByUserId">Who asked for the copy; making one leaves a trace.</param>
public sealed record ClientDataCopyQuery(Guid ClientId, Guid? ByUserId) : IQuery<ClientDataCopy?>;

public sealed class ClientDataCopyHandler(IClientDataService data) : IRequestHandler<ClientDataCopyQuery, ClientDataCopy?>
{
    public Task<ClientDataCopy?> HandleAsync(ClientDataCopyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return data.GetCopyAsync(request.ClientId, request.ByUserId, cancellationToken);
    }
}

/// <summary>
/// <c>/admin/clients/{clientId}/data</c> (ADR 0062): one page with everything the salon holds about a client, to
/// print or save and hand to the client who asks for a copy of their data. It shows health notes and consultation
/// answers, so it is for the logins that manage the client list (<see cref="Permissions.ClientsManage"/>: owner and
/// manager), it is never cached, and making it is logged by id. Another salon's client is a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[RequirePermission(Permissions.ClientsManage)]
[Route("admin/clients/{clientId:guid}/data")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DataCopyController(IDispatcher dispatcher) : SalonControllerBase
{
    /// <summary>The client's card; the ClientCard slice owns the route.</summary>
    public static string CardPath(Guid clientId) => $"/admin/clients/{clientId}";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid clientId, CancellationToken cancellationToken)
    {
        var copy = await dispatcher.SendAsync(new ClientDataCopyQuery(clientId, CurrentUserId()), cancellationToken);
        return copy is null ? NotFound() : View(new DataCopyViewModel(copy));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

public sealed record DataCopyViewModel(ClientDataCopy Copy)
{
    public string CardPath => DataCopyController.CardPath(Copy.Card.Id);
}

/// <summary>What the copy page says (ADR 0062, ADR 0043): the words, kept out of the view.</summary>
public static class DataCopyText
{
    /// <summary>Every question of a consultation form with the client's answer in words, in the order asked.</summary>
    public static IReadOnlyList<(string Question, string Answer)> Answers(ILocalizationService localizer, ConsultationAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(answers);
        return
        [
            .. ConsultationQuestions.All.Select(key => (
                localizer[$"Clients.Consultation.Question.{key}"],
                answers.Answers.TryGetValue(key, out var yes)
                    ? localizer[yes ? "Clients.Consultation.Yes" : "Clients.Consultation.No"]
                    : localizer["Clients.Consultation.NotAsked"])),
        ];
    }

    /// <summary>"From 01/12/2026 to 20/12/2026", "From ...", "Until ..." or "Any day".</summary>
    public static string Days(ILocalizationService localizer, CopyWaitlistEntry entry, System.Globalization.CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(culture);
        return (entry.NotBefore, entry.NotAfter) switch
        {
            ({ } from, { } to) => localizer.Format("Clients.Data.Waitlist.Between", from.ToString("d", culture), to.ToString("d", culture)),
            ({ } from, null) => localizer.Format("Clients.Data.Waitlist.From", from.ToString("d", culture)),
            (null, { } to) => localizer.Format("Clients.Data.Waitlist.Until", to.ToString("d", culture)),
            _ => localizer["Clients.Data.Waitlist.AnyDay"],
        };
    }
}