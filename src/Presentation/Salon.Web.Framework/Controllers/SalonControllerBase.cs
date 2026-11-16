using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Reports;

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

    /// <summary>
    /// A table as a CSV file the reader's spreadsheet opens (ADR 0042, ADR 0047): the list separator of their language
    /// (never their decimal sign), UTF-8 with a byte order mark so Excel reads the letters, and never cached, because
    /// the rows are the salon's own data.
    /// </summary>
    protected FileContentResult CsvDownload(string fileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<CsvValue>> rows)
    {
        var culture = CultureInfo.CurrentCulture;
        var listSeparator = culture.TextInfo.ListSeparator;
        var separator = listSeparator.Length == 1 && listSeparator != culture.NumberFormat.NumberDecimalSeparator ? listSeparator[0] : ';';
        var csv = Csv.Write(headers, rows, separator);
        Response.Headers.CacheControl = "no-store";
        return File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)], Csv.ContentType, fileName);
    }
}