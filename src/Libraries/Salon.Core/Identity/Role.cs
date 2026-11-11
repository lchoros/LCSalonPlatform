namespace Salon.Core.Identity;

/// <summary>
/// A salon user's role; the permission set per role lives in <see cref="RolePermissions"/> (ADR 0009). Stored as an int,
/// so values are explicit and never reordered.
/// </summary>
public enum Role
{
    Owner = 1,
    Manager = 2,
    Receptionist = 3,
    Staff = 4,
}