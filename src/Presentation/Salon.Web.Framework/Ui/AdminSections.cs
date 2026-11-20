using Salon.Core.Identity;

namespace Salon.Web.Framework.Ui;

/// <summary>One page of a module, as a tab: where it is, what it is called, and what a role needs to open it.</summary>
/// <param name="Key">Stable within its module; also the tab's test id suffix.</param>
/// <param name="Permissions">Every one is needed — the same permissions the page's own controller asks for.</param>
public sealed record SectionTab(string Key, string LabelKey, string Url, params string[] Permissions);

/// <param name="Name">The module, for test ids: <c>section-tabs-{Name}</c>.</param>
/// <param name="LabelKey">What the tab row is called for assistive technology.</param>
/// <param name="CurrentKey">The tab of the page being shown.</param>
public sealed record SectionTabsModel(string Name, string LabelKey, IReadOnlyList<SectionTab> Tabs, string CurrentKey);

/// <summary>
/// The pages inside each admin module, as one row of tabs under the module's place in the menu (ADR 0089) — the
/// Versum pattern the owner pointed at: the menu names the module, the tabs name its pages. Kept here, not in the
/// views, so a page asks for its row by name and the list of pages lives in one place.
/// </summary>
public static class AdminSections
{
    private static readonly SectionTab[] AppointmentTabs =
    [
        new("day", "Sections.Appointments.Day", "/admin/appointments", Permissions.BookingsManage, Permissions.ClientsView),
        new("calendar", "Sections.Appointments.Calendar", "/admin/appointments/calendar", Permissions.BookingsManage, Permissions.ClientsView),
        new("waitlist", "Sections.Appointments.Waitlist", "/admin/appointments/waitlist", Permissions.BookingsManage, Permissions.ClientsView),
        new("import", "Sections.Import", "/admin/appointments/import", Permissions.BookingsManage, Permissions.ClientsView, Permissions.ClientsManage),
    ];

    private static readonly SectionTab[] ClientTabs =
    [
        new("list", "Sections.Clients.List", "/admin/clients", Permissions.ClientsView),
        new("import", "Sections.Import", "/admin/clients/import", Permissions.ClientsManage),
    ];

    private static readonly SectionTab[] TillTabs =
    [
        new("cash-up", "Sections.Till.CashUp", "/admin/payments/cash-up", Permissions.PaymentsRecord, Permissions.ClientsView),
        new("sale", "Sections.Till.Sale", "/admin/payments/sale", Permissions.PaymentsRecord),
        new("vouchers", "Sections.Till.Vouchers", "/admin/payments/vouchers", Permissions.PaymentsRecord),
    ];

    private static readonly SectionTab[] ServiceTabs =
    [
        new("list", "Sections.Services.List", "/admin/services", Permissions.CatalogManage),
        new("categories", "Sections.Services.Categories", "/admin/services/categories", Permissions.CatalogManage),
        new("import", "Sections.Import", "/admin/services/import", Permissions.CatalogManage),
    ];

    private static readonly SectionTab[] ProductTabs =
    [
        new("list", "Sections.Products.List", "/admin/products", Permissions.BookingsManage),
        new("import", "Sections.Import", "/admin/products/import", Permissions.CatalogManage),
    ];

    private static readonly SectionTab[] StaffTabs =
    [
        new("list", "Sections.Staff.List", "/admin/staff", Permissions.StaffManage),
        new("commission", "Sections.Staff.Commission", "/admin/staff/commission", Permissions.SettingsManage),
    ];

    public static SectionTabsModel Appointments(string current) => new("appointments", "Layout.Nav.Appointments", AppointmentTabs, current);

    public static SectionTabsModel Clients(string current) => new("clients", "Layout.Nav.Clients", ClientTabs, current);

    public static SectionTabsModel Till(string current) => new("till", "Layout.Nav.CashUp", TillTabs, current);

    public static SectionTabsModel Services(string current) => new("services", "Layout.Nav.Services", ServiceTabs, current);

    public static SectionTabsModel Products(string current) => new("products", "Layout.Nav.Products", ProductTabs, current);

    public static SectionTabsModel Staff(string current) => new("staff", "Layout.Nav.Staff", StaffTabs, current);

    /// <summary>Every row, for the tests that hold each tab to its page's own permissions and texts.</summary>
    public static IReadOnlyList<SectionTabsModel> All { get; } =
    [
        Appointments("day"), Clients("list"), Till("cash-up"), Services("list"), Products("list"), Staff("list"),
    ];
}