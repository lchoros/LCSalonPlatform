using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Services.Localization;
using Salon.Services.Products;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Products;

namespace Salon.Web.Features.Products.RecordStock;

/// <summary>
/// The four forms of a product's page, <c>POST /admin/products/{id}/stock/{received|used|written-off|counted}</c>
/// (ADR 0036). Tenant only. A delivery and a use are for everybody who works the diary
/// (<see cref="Permissions.BookingsManage"/>): the person who opens the box or empties the tube records it. A count and
/// a write-off put the books right or take stock off them without anything to show for it, so they need
/// <see cref="Permissions.CatalogManage"/>, as changing the product does. Every answer is a redirect to the product's
/// page, with a confirmation or with the reason nothing changed.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[Route("admin/products")]
public sealed class RecordStockController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>A product's own page; the ProductCard slice owns the route.</summary>
    public const string ListPath = "/admin/products";

    [HttpPost("{id:guid}/stock/{kind}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(Guid id, string kind, int? number, string? note, CancellationToken cancellationToken)
    {
        if (!StockKindSegments.TryParse(kind, out var chosen))
        {
            return NotFound();
        }

        if (NeedsManager(chosen) && !User.HasClaim(UserClaimTypes.Permission, Permissions.CatalogManage))
        {
            return Forbid();
        }

        // A blank or unreadable number is the same mistake as a wrong one; the service names it.
        var result = await dispatcher.SendAsync(new RecordStockCommand(id, chosen, number ?? -1, note, CurrentUserId()), cancellationToken);
        switch (result.Outcome)
        {
            case StockOutcome.ProductNotFound:
                // Another salon's id looks the same as a missing one (tenant filter).
                return NotFound();
            case StockOutcome.Recorded:
                SetFlash(localizer.Plural($"Products.Stock.Recorded.{chosen}", Math.Abs(result.Change), result.OnHand));
                break;
            case StockOutcome.CountMatches:
                SetFlash(localizer.Format("Products.Stock.CountMatches", result.OnHand));
                break;
            default:
                SetProblem(localizer.Format($"Products.Stock.Refused.{result.Outcome}", result.OnHand));
                break;
        }

        // The path is built from the route's Guid, never from text the request supplied.
        return Redirect($"{ListPath}/{id}");
    }

    private static bool NeedsManager(StockMovementKind kind) => kind is StockMovementKind.Counted or StockMovementKind.WrittenOff;

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}