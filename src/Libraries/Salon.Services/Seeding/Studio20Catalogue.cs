using Salon.Core.Catalog;

namespace Salon.Services.Seeding;

/// <summary>
/// The demo salon's catalogue (ADR 0032): the services and prices of the pilot salon as its public price list shows
/// them, so the booking page is tried with a real-sized list and real names. Two things the price list does not say
/// are filled in and are for the salon to correct: every duration is an estimate, and where the list gives a price
/// range without naming the variants (all but Keratin) there are two options at the lowest and the highest price.
/// </summary>
internal static class Studio20Catalogue
{
    /// <summary>In display order; the sort order of each category is its position here.</summary>
    public static readonly string[] Categories = ["Hair", "Brows", "Eyelashes", "Waxing", "Beauty"];

    public static readonly IReadOnlyList<DemoService> Services =
    [
        .. Range("HAIR KIDS 21: Boy's Haircut", "Hair", 20, 9m, 11m),
        One("HAIR KIDS 22: Girl's Haircut", "Hair", 30, 17m),
        One("HAIR KIDS 23: Girl's Wash, Cut & Finish", "Hair", 45, 25m),
        One("HAIR MEN 17: Haircut Scissors", "Hair", 30, 16m),
        One("HAIR MEN 18: Haircut Scissors + Clipper", "Hair", 30, 14m),
        One("HAIR MEN 19: Haircut Clipper", "Hair", 15, 8m),
        .. Range("HAIR MEN 20: Colour", "Hair", 60, 35m, 50m),
        .. Range("HAIR WOMEN 01: Full Colour", "Hair", 120, 65m, 85m),
        .. Range("HAIR WOMEN 02: Roots Only (one colour, not longer then 1 inch)", "Hair", 90, 50m, 55m),
        .. Range("HAIR WOMEN 03: Foils Highlights", "Hair", 150, 65m, 115m),
        .. Range("HAIR WOMEN 04: Ombre", "Hair", 180, 80m, 130m),
        One("HAIR WOMEN 05: Remove Colour", "Hair", 60, 35m, buffer: 10, processing: 20, processingStartsAfter: 15),
        .. Range("HAIR WOMEN 06: Dry Cut", "Hair", 30, 19m, 25m),
        .. Range("HAIR WOMEN 07: Cut, Wash & Blow Dry", "Hair", 60, 28m, 38m),
        .. Range("HAIR WOMEN 08: Blow Dry", "Hair", 30, 15m, 20m),
        .. Range("HAIR WOMEN 09: Toner", "Hair", 60, 38m, 55m),
        .. Range("HAIR WOMEN 10: Hair Up", "Hair", 60, 35m, 45m),
        One("HAIR WOMEN 11: Fringe", "Hair", 10, 5m),
        One("HAIR WOMEN 12: Treatment + Souna", "Hair", 30, 12m),
        One("HAIR WOMEN 13: Olaplex", "Hair", 30, 20m),
        Variant("HAIR WOMEN 14: Keratin", "Amazon - Grape, Apple, Botox - Medium hair", "Hair", 150, 75m),
        Variant("HAIR WOMEN 14: Keratin", "Amazon - Grape, Apple, Botox - Long hair", "Hair", 180, 85m),
        Variant("HAIR WOMEN 14: Keratin", "Amazon - AntiFrizz - Medium hair", "Hair", 150, 75m),
        Variant("HAIR WOMEN 14: Keratin", "Amazon - AntiFrizz - Long hair", "Hair", 180, 85m),
        Variant("HAIR WOMEN 14: Keratin", "CocoChoco - Long hair", "Hair", 180, 75m),
        Variant("HAIR WOMEN 14: Keratin", "CocoChoco - Medium hair", "Hair", 150, 70m),
        One("HAIR WOMEN 15: Extensions", "Hair", 180, 80m),
        One("HAIR WOMEN 16: Extensions Remove", "Hair", 60, 30m),
        .. Range("HAIR WOMEN 25: Roots + Toner", "Hair", 120, 70m, 75m),
        .. Range("HAIR WOMEN 26: Botox", "Hair", 90, 45m, 80m),
        .. Range("HAIR WOMEN 27: Lamination", "Hair", 45, 10m, 35m),
        One("BROWS01: Eyebrows Powder Henna & Shape", "Brows", 30, 22m),
        One("BROWS02: Eyebrows Regulation (Wax or Twizzers)", "Brows", 15, 10m),
        One("BROWS03: Eyebrows Regulation & Reshape (Wax or Twizzers)", "Brows", 20, 13m),
        One("BROWS04: Brows Lamination", "Brows", 45, 30m),
        One("BROWS05: Brows Tint & Regulation", "Brows", 20, 18m),
        One("BROWS06: Brows Henna, Regulation & Lash Lift, Tint, Botox", "Brows", 75, 40m),
        One("EYELASHES01: Lash Tint", "Eyelashes", 15, 10m),
        One("EYELASHES02: Lash Lift & Botox", "Eyelashes", 45, 25m),
        One("EYELASHES03: Lash Lift & Botox & Tint", "Eyelashes", 60, 30m),
        One("EYELASHES04: Brows Henna, Regulation & Lash Lift, Tint, Botox", "Eyelashes", 75, 40m),
        One("EYELASHES05: 1:1 Classic", "Eyelashes", 90, 40m),
        One("EYELASHES06: 3:1 Extension", "Eyelashes", 120, 40m),
        One("EYELASHES07: Infill Classic (two weeks)", "Eyelashes", 45, 20m),
        One("EYELASHES08: Removing", "Eyelashes", 20, 10m),
        One("WAXING01: Upper Lip Wax", "Waxing", 10, 5m),
        One("WAXING02: Face Wax", "Waxing", 15, 10m),
        One("WAXING03: Ear Wax", "Waxing", 10, 8m),
        One("BEAUTY01: Paraffin Hand Treatment & Massage", "Beauty", 20, 10m),
        One("BEAUTY02: Manicure", "Beauty", 30, 15m),
        One("BEAUTY03: Facial Treatment", "Beauty", 60, 40m),
        One("BEAUTY04: Microdermabrasion", "Beauty", 45, 45m),
    ];

    /// <summary>Label of the cheaper of two options where the price list gives a range without naming the variants.</summary>
    public const string LowerOption = "Option 1";

    /// <summary>Label of the dearer of the two.</summary>
    public const string HigherOption = "Option 2";

    private static DemoService One(string name, string category, int minutes, decimal price, int buffer = 0, int processing = 0, int processingStartsAfter = 0)
        => new(name, null, minutes, price, category, buffer, processing, processingStartsAfter);

    private static DemoService Variant(string title, string label, string category, int minutes, decimal price)
        => new(title + ServiceRules.VariantSeparator + label, null, minutes, price, category, VariantGroup: title, VariantLabel: label);

    private static DemoService[] Range(string title, string category, int minutes, decimal lowest, decimal highest)
        => [Variant(title, LowerOption, category, minutes, lowest), Variant(title, HigherOption, category, minutes, highest)];
}

/// <param name="Description">Shown under the name on the public list; the price list has none.</param>
/// <param name="VariantGroup">The title shared with sibling variants, or null for a service on its own.</param>
internal sealed record DemoService(
    string Name,
    string? Description,
    int Minutes,
    decimal Price,
    string Category,
    int Buffer = 0,
    int Processing = 0,
    int ProcessingStartsAfter = 0,
    string? VariantGroup = null,
    string? VariantLabel = null);