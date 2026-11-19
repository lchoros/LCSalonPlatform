using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.TeamLogins;

/// <summary>The salon's logins, owners first (ADR 0076). A query: it reads, never writes.</summary>
public sealed record TeamLoginsQuery : IQuery<TeamLoginList>;

/// <param name="ActiveOwners">How many active owner logins the salon has; at one, that owner cannot be turned off or demoted.</param>
public sealed record TeamLoginList(IReadOnlyList<TeamLogin> Logins, int ActiveOwners);

/// <param name="LastSignIn">In the salon's zone; null for a login never used.</param>
public sealed record TeamLogin(Guid Id, string DisplayName, string Email, Role Role, bool IsActive, LocalDateTime? LastSignIn);

public sealed class TeamLoginsHandler(
    IUserService users,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<TeamLoginsQuery, TeamLoginList>
{
    public async Task<TeamLoginList> HandleAsync(TeamLoginsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(TeamLoginsQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var logins = await users.ListAsync(cancellationToken);
        return new TeamLoginList(
            [.. logins.Select(u => new TeamLogin(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.LastLoginAt?.InZone(zone).LocalDateTime))],
            logins.Count(u => u.IsActive && u.Role == Role.Owner));
    }
}

/// <param name="Flash">Confirmation left by a change (post-redirect-get), if any.</param>
public sealed record TeamLoginsViewModel(TeamLoginList List, Guid? CurrentUserId, string? Flash);

/// <summary>
/// <c>/admin/settings/team</c>, owner only (ADR 0076): every login of the salon — who, their email, their role,
/// whether it is on and when it was last used — with the way to add one or change one. The salon's people without
/// a login live on the Staff pages; this page is only about who can sign in.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/team")]
public sealed class TeamLoginsController(IDispatcher dispatcher) : SalonControllerBase
{
    public const string ListPath = "/admin/settings/team";

    public static string EditPath(Guid userId) => $"{ListPath}/{userId}";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new TeamLoginsViewModel(await dispatcher.SendAsync(new TeamLoginsQuery(), cancellationToken), CurrentUserId(), TakeFlash()));

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}