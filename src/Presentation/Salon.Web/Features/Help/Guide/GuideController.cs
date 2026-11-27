using Microsoft.AspNetCore.Mvc;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Help.Guide;

/// <summary>
/// The guide for the salon's owner at <c>/admin/guide</c> (ADR 0099): one short page, a section for each of the first things they do —
/// the day's appointments, a booking for a client, services, the team, and the page
/// clients book on — each with a picture of the screen, the steps in plain words and a link to
/// the page itself. Anybody signed in may read it; it shows nothing of the salon's data (the pictures are of a
/// made-up salon). The words are resource keys in both languages; the list of sections and their addresses is here.
/// </summary>
[RequireTenant]
[RequireSignedIn]
[Route("admin/guide")]
public sealed class GuideController : SalonControllerBase
{
    public const string Path = "/admin/guide";

    /// <summary>The sections in the order a new member of the team meets them; the addresses are owned by their slices.</summary>
    private static readonly GuideSection[] Sections =
    [
        new("Day", "/admin/appointments", 5),
        new("Book", "/admin/appointments/new", 4),
        new("Services", "/admin/services", 5),
        new("Staff", "/admin/staff", 4),
        new("Online", "/book", 5),
    ];

    [HttpGet("")]
    public IActionResult Index() => View(new GuideViewModel(Sections));
}

/// <summary>One section of the guide.</summary>
/// <param name="Key">Names its words (<c>Guide.{Key}.Title</c>, <c>.Lead</c>, <c>.Step1</c>…) and its picture.</param>
/// <param name="Path">The page the section is about.</param>
/// <param name="Steps">How many numbered steps it has.</param>
public sealed record GuideSection(string Key, string Path, int Steps)
{
    /// <summary>The section's place on the page, for the list at the top.</summary>
    public string Anchor => Key.ToLowerInvariant();

    /// <summary>The picture of the screen: made by the guide's own walk, of a made-up salon.</summary>
    public string Image => $"/img/guide/{Anchor}.jpg";

    public string TitleKey => $"Guide.{Key}.Title";

    public string LeadKey => $"Guide.{Key}.Lead";

    /// <summary>The resource keys of the steps, in order.</summary>
    public IEnumerable<string> StepKeys => Enumerable.Range(1, Steps).Select(n => $"Guide.{Key}.Step{n}");
}

public sealed record GuideViewModel(IReadOnlyList<GuideSection> Sections);