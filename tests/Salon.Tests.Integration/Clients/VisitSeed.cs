using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Staff;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// Seeds a salon with one stylist, one service and clients, and writes appointments straight to the tables (past visits
/// cannot go through the booking service: the past has no slots). Each appointment gets its first status-change row and,
/// when it ended in another state, the transition row, as the state machine would have written them.
/// </summary>
internal static class VisitSeed
{
    public const string StaffName = "Anna";
    public const string ServiceName = "Cut";

    public static async Task<(Guid StaffId, Guid ServiceId)> SalonAsync(SalonWebApplicationFactory factory, Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = StaffName, CreatedAt = now };
        var cut = new Service { Name = ServiceName, Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        db.StaffMembers.Add(anna);
        db.Services.Add(cut);
        await db.SaveChangesAsync();
        return (anna.Id, cut.Id);
    }

    public static async Task<Guid> ClientAsync(SalonWebApplicationFactory factory, Guid tenantId, string firstName, string lastName, string? phone = null, string? email = null, string? notes = null)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var client = new Client { FirstName = firstName, LastName = lastName, Phone = phone, Email = email, Notes = notes, CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    /// <summary>One appointment of <paramref name="minutes"/> starting at <paramref name="start"/>, left in <paramref name="status"/>; returns its id.</summary>
    public static async Task<Guid> AppointmentAsync(
        SalonWebApplicationFactory factory,
        Guid tenantId,
        Guid clientId,
        Guid staffId,
        Guid serviceId,
        Instant start,
        AppointmentStatus status,
        int minutes = 30,
        decimal price = 20m)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var createdAt = start.Minus(Duration.FromDays(1));
        var appointment = new Appointment
        {
            TenantId = tenantId,
            ClientId = clientId,
            StaffMemberId = staffId,
            ServiceId = serviceId,
            Start = start,
            End = start.Plus(Duration.FromMinutes(minutes)),
            Status = AppointmentStatus.Booked,
            Source = AppointmentSource.Admin,
            PriceAmount = price,
            Currency = "GBP",
            DurationMinutes = minutes,
            CreatedAt = createdAt,
        };
        db.Appointments.Add(appointment);
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange { TenantId = tenantId, AppointmentId = appointment.Id, To = AppointmentStatus.Booked, At = createdAt });
        if (status != AppointmentStatus.Booked)
        {
            appointment.Status = status;
            db.AppointmentStatusChanges.Add(new AppointmentStatusChange { TenantId = tenantId, AppointmentId = appointment.Id, From = AppointmentStatus.Booked, To = status, At = start });
        }

        await db.SaveChangesAsync();
        return appointment.Id;
    }

    public static async Task<List<Appointment>> AppointmentsOfAsync(SalonWebApplicationFactory factory, Guid tenantId, Guid clientId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.ClientId == clientId).OrderBy(a => a.Start).ToListAsync();
    }
}
