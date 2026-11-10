using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;

using Salon.Web.Framework.Controllers;
using Salon.Web.Models;

namespace Salon.Web.Controllers;

public sealed class HomeController : SalonControllerBase
{
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}