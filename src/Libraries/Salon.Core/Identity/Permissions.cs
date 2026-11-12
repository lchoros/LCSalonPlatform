namespace Salon.Core.Identity;

/// <summary>
/// The catalogue of permission names (ADR 0009). A permission is carried as one <see cref="UserClaimTypes.Permission"/>
/// claim per name and checked by <c>[RequirePermission]</c>; the names are also what a per-tenant role map will store in M2.
/// </summary>
public static class Permissions
{
    public const string CatalogManage = "Catalog.Manage";
    public const string StaffManage = "Staff.Manage";
    public const string BookingsManage = "Bookings.Manage";
    public const string ClientsView = "Clients.View";

    /// <summary>Bulk changes to the client list, such as importing a CSV (ADR 0019); Owner and Manager only.</summary>
    public const string ClientsManage = "Clients.Manage";
    public const string PaymentsRecord = "Payments.Record";
    public const string SettingsManage = "Settings.Manage";
    public const string TemplatesManage = "Templates.Manage";
    public const string UsersManage = "Users.Manage";

    /// <summary>Every known permission; <c>[RequirePermission]</c> refuses a name outside this list at construction.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CatalogManage,
        StaffManage,
        BookingsManage,
        ClientsView,
        ClientsManage,
        PaymentsRecord,
        SettingsManage,
        TemplatesManage,
        UsersManage,
    ];
}