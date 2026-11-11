namespace Salon.Core.Identity;

/// <summary>
/// Role → permissions, fixed in code for M0 (ADR 0009): Owner has everything; Manager everything but users and settings;
/// Receptionist books, sees clients and records payments; Staff only books.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyList<string> ManagerPermissions =
        Permissions.All.Where(p => p is not (Permissions.UsersManage or Permissions.SettingsManage)).ToArray();

    private static readonly IReadOnlyList<string> ReceptionistPermissions =
    [
        Permissions.BookingsManage,
        Permissions.ClientsView,
        Permissions.PaymentsRecord,
    ];

    private static readonly IReadOnlyList<string> StaffPermissions = [Permissions.BookingsManage];

    public static IReadOnlyList<string> For(Role role) => role switch
    {
        Role.Owner => Permissions.All,
        Role.Manager => ManagerPermissions,
        Role.Receptionist => ReceptionistPermissions,
        Role.Staff => StaffPermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}