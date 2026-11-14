using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Appointments;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// Bookings made at the desk (ADR 0029): the client gets the same confirmation as after an online booking, by text and
/// by email, with their own link that opens the cancel and change pages. The salon can switch desk confirmations off;
/// online bookings are confirmed either way.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task A_booking_made_at_the_desk_is_confirmed_by_text_and_email_with_the_clients_own_link()
    {
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        await SetEmailAsync(sophie, "sophie@example.com");

        var desk = await BookAtTheDeskAsync(sophie, _annaId, At(10, 0));
        await DispatchAsync();

        var appointment = (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == desk);
        appointment.Source.ShouldBe(AppointmentSource.Admin);
        var token = appointment.PublicToken.ShouldNotBeNull();
        var sms = factory.Sms.Messages.ShouldHaveSingleItem();
        sms.ToE164.ShouldBe("+447700900456");
        sms.Body.ShouldStartWith("Hi Sophie, your Cut at Studio 20 is booked for ");
        sms.Body.ShouldEndWith($"/v/{token}");
        var email = factory.Emails.Messages.ShouldHaveSingleItem();
        email.To.ShouldBe("sophie@example.com");
        email.Subject.ShouldBe("Your Cut at Studio 20 is booked");
        email.TextBody.ShouldNotBeNull().ShouldContain($"/v/{token}");

        // Dispatching again sends nothing more.
        await DispatchAsync();
        factory.Sms.Messages.ShouldHaveSingleItem();
        factory.Emails.Messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_link_of_a_desk_booking_opens_the_clients_cancel_and_change_pages()
    {
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        var desk = await BookAtTheDeskAsync(sophie, _annaId, At(10, 0));
        var token = (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == desk).PublicToken.ShouldNotBeNull();
        using var client = factory.ClientFor(HostA);

        var cancel = await GetAsync(client, $"/book/cancel/{token}");
        cancel.ShouldContain("Cut");
        cancel.ShouldContain("Anna");
        var change = await GetAsync(client, $"/book/change/{token}");
        change.ShouldContain("Cut");

        // The link belongs to this salon only.
        using var other = factory.ClientFor(HostB);
        (await GetAsync(other, $"/book/cancel/{token}")).ShouldNotContain("Anna");
    }

    [Fact]
    public async Task With_desk_confirmations_switched_off_only_online_bookings_are_confirmed()
    {
        await SaveNotificationSettingsAsync(s => s.ConfirmDeskBookings = false);
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        await SetEmailAsync(sophie, "sophie@example.com");

        var desk = await BookAtTheDeskAsync(sophie, _annaId, At(10, 0));
        await DispatchAsync();

        factory.Sms.Messages.ShouldBeEmpty();
        factory.Emails.Messages.ShouldBeEmpty();
        (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == desk).PublicToken.ShouldNotBeNull("the link exists either way, for reminders and for the desk to read out");

        using var client = factory.ClientFor(HostA);
        await BookThroughTheFlowAsync(client, At(12, 0), _tomId);
        await DispatchAsync();
        ConfirmationTexts().ShouldHaveSingleItem("an online booking is always confirmed");
    }

    [Fact]
    public async Task A_desk_booking_for_a_client_nobody_can_reach_sends_nothing_and_does_not_fail()
    {
        Guid nobody;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
            var client = new Core.Clients.Client { FirstName = "Walk", LastName = "In", CreatedAt = now, UpdatedAt = now };
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            nobody = client.Id;
        }

        await BookAtTheDeskAsync(nobody, _annaId, At(10, 0));
        await DispatchAsync();

        factory.Sms.Messages.ShouldBeEmpty();
        factory.Emails.Messages.ShouldBeEmpty();
        (await PendingOutboxCountAsync()).ShouldBe(0, "nothing is left to retry");
    }

    private async Task SetEmailAsync(Guid clientId, string email)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients
            .Where(c => c.Id == clientId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Email, email));
    }
}