using Microsoft.AspNetCore.Mvc;

namespace Salon.Web.Framework.Controllers;

/// <summary>Base for every slice controller. Tenant guard, work context and permission filters attach here.</summary>
public abstract class SalonControllerBase : Controller
{
    /// <summary>TempData key of the one-line confirmation shown on the next page after a redirect (post-redirect-get).</summary>
    public const string FlashTempDataKey = "Flash";

    /// <summary>Stores an already localized confirmation for the page the user is redirected to.</summary>
    protected void SetFlash(string message) => TempData[FlashTempDataKey] = message;

    /// <summary>Reads and clears the confirmation left by the previous request, if any.</summary>
    protected string? TakeFlash() => TempData[FlashTempDataKey] as string;

    /// <summary>
    /// TempData key of the one-line reason a request was refused, shown on the next page after a redirect. For a slice
    /// whose form sits on another slice's page: it cannot re-render that page, so it redirects back with the reason.
    /// </summary>
    public const string ProblemTempDataKey = "Problem";

    /// <summary>Stores an already localized reason for the page the user is redirected to.</summary>
    protected void SetProblem(string message) => TempData[ProblemTempDataKey] = message;

    /// <summary>Reads and clears the reason left by the previous request, if any.</summary>
    protected string? TakeProblem() => TempData[ProblemTempDataKey] as string;
}