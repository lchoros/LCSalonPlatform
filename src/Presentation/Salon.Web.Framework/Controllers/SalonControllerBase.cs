using Microsoft.AspNetCore.Mvc;

namespace Salon.Web.Framework.Controllers;

/// <summary>Base for every slice controller. Tenant guard, work context and permission filters attach here.</summary>
public abstract class SalonControllerBase : Controller;