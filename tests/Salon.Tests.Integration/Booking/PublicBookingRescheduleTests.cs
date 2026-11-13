using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Clients;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The client's "change the time" link and the two messages that follow a change of plan (ADR 0021): the moved SMS and
/// the cancellation SMS, whoever caused them.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task The_change_page_moves_the_visit_frees_the_old_time_and_texts_the_new_one_once()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, donePath) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var changePath = $"/book/change/{appointment.PublicToken}";
        await DispatchAsync();
        factory.Sms.Clear();

        (await GetAsync(client, donePath)).ShouldContain($"href=\"{changePath}\" data-testid=\"change-link\"");
        var page = await GetAsync(client, $"{changePath}?date={Iso(_day)}");
        page.ShouldContain("Change the time");
        page.ShouldContain("data-testid=\"change-form\"");
        page.ShouldContain("Now booked for");
        page.ShouldNotContain(SlotButton(At(10, 0)), Case.Sensitive, "the time it already has is not offered back");
        page.ShouldContain(SlotButton(At(11, 0)));
        page.ShouldContain(SlotButton(At(10, 30)), Case.Sensitive, "times overlapping its own current slot are free for it");

        var moved = await PostChangeAsync(client, changePath, At(11, 0), _annaId, page);

        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = WebUtility.HtmlDecode(await moved.Content.ReadAsStringAsync());
        result.ShouldContain("Your appointment has been moved.");
        result.ShouldContain("11:00");
        var row = (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem();
        row.Start.ShouldBe(At(11, 0));
        row.StaffMemberId.ShouldBe(_annaId);
        row.Status.ShouldBe(AppointmentStatus.Booked);
        row.PriceAmount.ShouldBe(appointment.PriceAmount);
        (await SlotStartsAsync(_studio.Id, _annaId)).ShouldContain(At(10, 0), "the old time is free again");
        (await OutboxRowsAsync()).Select(m => m.EventType).ShouldContain(typeof(AppointmentMoved).FullName);

        // The same form posted again (a double tap): still "moved", nothing changes, no second text.
        var again = await PostChangeAsync(client, changePath, At(11, 0), _annaId, page);
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()).ShouldContain("Your appointment has been moved.");
        await DispatchAsync();

        var sms = factory.Sms.Messages.ShouldHaveSingleItem();
        sms.ToE164.ShouldBe(PhoneE164);
        sms.Body.ShouldStartWith("Studio 20: your Cut is now on ");
        sms.Body.ShouldContain("11:00");
        sms.Body.ShouldContain($"/book/cancel/{appointment.PublicToken}");
    }

    [Fact]
    public async Task Anyone_picks_whoever_is_free_and_a_time_taken_meanwhile_asks_to_pick_again()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var changePath = $"/book/change/{appointment.PublicToken}";
        var page = await GetAsync(client, $"{changePath}?date={Iso(_day)}&staff=any");
        page.ShouldContain("data-testid=\"staff-any\"");

        // Somebody else takes Anna's 13:00 while the page is open.
        var other = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        await BookAtTheDeskAsync(other, _annaId, At(13, 0));
        var taken = await PostChangeAsync(client, changePath, At(13, 0), _annaId, page);

        taken.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = WebUtility.HtmlDecode(await taken.Content.ReadAsStringAsync());
        again.ShouldContain("data-testid=\"change-message\"");
        again.ShouldContain("data-testid=\"change-form\"", Case.Sensitive, "fresh times are offered straight away");
        (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == appointment.Id).Start.ShouldBe(At(10, 0));

        // "Anyone" at 13:00: Anna is busy now, Tom (10:00 to 14:00) is free.
        var moved = await PostChangeAsync(client, changePath, At(13, 0), null, page);

        WebUtility.HtmlDecode(await moved.Content.ReadAsStringAsync()).ShouldContain("Your appointment has been moved.");
        var row = (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == appointment.Id);
        row.Start.ShouldBe(At(13, 0));
        row.StaffMemberId.ShouldBe(_tomId);
    }

    [Fact]
    public async Task A_cancelled_or_started_visit_and_an_unknown_or_foreign_link_cannot_be_changed()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var changePath = $"/book/change/{appointment.PublicToken}";
        var page = await GetAsync(client, changePath);

        (await GetAsync(client, "/book/change/" + new string('A', 43))).ShouldContain("This link is not valid or has already been used.");
        using var polish = factory.ClientFor(HostB);
        var foreign = await GetAsync(polish, changePath);
        foreign.ShouldNotContain("data-testid=\"change-form\"");
        foreign.ShouldNotContain("Cut", Case.Sensitive, "another salon learns nothing about the booking");

        // Started: the page says so and a post changes nothing.
        await SetStartAsync(appointment.Id, SystemClock.Instance.GetCurrentInstant() - Duration.FromMinutes(5));
        (await GetAsync(client, changePath)).ShouldContain("cannot be changed online");
        var late = await PostChangeAsync(client, changePath, At(11, 0), _annaId, page);
        WebUtility.HtmlDecode(await late.Content.ReadAsStringAsync()).ShouldContain("cannot be changed online");

        // Cancelled: nothing to change.
        await SetStartAsync(appointment.Id, At(10, 0));
        await CancelBySalonAsync(appointment.Id);
        var gone = await GetAsync(client, changePath);
        gone.ShouldContain("already been cancelled");
        gone.ShouldNotContain("data-testid=\"change-form\"");
        var refused = await PostChangeAsync(client, changePath, At(11, 0), _annaId, page);
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("already been cancelled");
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().Start.ShouldBe(At(10, 0));

        // No anti-forgery token, no move.
        var bare = await client.PostAsync(new Uri(changePath, UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string> { ["Start"] = InstantPattern.ExtendedIso.Format(At(11, 0)) }));
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelling_texts_the_client_whoever_cancels_and_the_cancel_page_offers_a_change_first()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await DispatchAsync();
        factory.Sms.Clear();
        var cancelPath = $"/book/cancel/{appointment.PublicToken}";

        var page = await GetAsync(client, cancelPath);
        page.ShouldContain($"href=\"/book/change/{appointment.PublicToken}\" data-testid=\"change-instead\"");
        await PostAsync(client, cancelPath, [], page);
        await DispatchAsync();

        var byClient = factory.Sms.Messages.ShouldHaveSingleItem();
        byClient.ToE164.ShouldBe(PhoneE164);
        byClient.Body.ShouldStartWith("Studio 20: your Cut on ");
        byClient.Body.ShouldContain("has been cancelled. Rebook any time: ");
        byClient.Body.ShouldEndWith("/book");

        // The salon cancels a visit booked at the desk: the client hears about it the same way.
        factory.Sms.Clear();
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        var desk = await BookAtTheDeskAsync(sophie, _tomId, At(11, 0));
        await CancelBySalonAsync(desk);
        await DispatchAsync();

        factory.Sms.Messages.ShouldHaveSingleItem().ToE164.ShouldBe("+447700900456");
    }

    [Fact]
    public async Task No_cancellation_text_for_a_visit_that_had_started_or_a_card_without_a_phone()
    {
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        var started = await BookAtTheDeskAsync(sophie, _annaId, At(10, 0));
        await SetStartAsync(started, SystemClock.Instance.GetCurrentInstant() - Duration.FromHours(1));
        var phoneless = await ClientWithoutPhoneAsync();
        var noPhone = await BookAtTheDeskAsync(phoneless, _tomId, At(11, 0));
        await DispatchAsync();
        factory.Sms.Clear();

        await CancelBySalonAsync(started);
        await CancelBySalonAsync(noPhone);
        await DispatchAsync();

        factory.Sms.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_visit_the_salon_moves_in_the_calendar_texts_the_client_the_new_time()
    {
        var sophie = await CreateClientAsync(_studio.Id, "Sophie", "Smith", "07700 900456");
        var desk = await BookAtTheDeskAsync(sophie, _annaId, At(10, 0));
        await DispatchAsync();
        factory.Sms.Clear();

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<IBookingService>().MoveAsync(desk, _tomId, At(12, 0));
        }

        await DispatchAsync();

        var sms = factory.Sms.Messages.ShouldHaveSingleItem();
        sms.ToE164.ShouldBe("+447700900456");
        sms.Body.ShouldContain("is now on ");
        sms.Body.ShouldContain("12:00");
        var token = (await AppointmentsAsync(_studio.Id)).Single(a => a.Id == desk).PublicToken.ShouldNotBeNull();
        sms.Body.ShouldEndWith($"/book/cancel/{token}", Case.Sensitive, "a booking made at the desk has the client's own link too (ADR 0029)");
    }

    private static string SlotButton(Instant start) => $"name=\"Start\" value=\"{InstantPattern.ExtendedIso.Format(start)}\"";

    private static async Task<HttpResponseMessage> PostChangeAsync(HttpClient client, string changePath, Instant start, Guid? staffId, string pageWithToken)
    {
        var fields = new Dictionary<string, string> { ["Start"] = InstantPattern.ExtendedIso.Format(start) };
        if (staffId is { } id)
        {
            fields["StaffMemberId"] = id.ToString();
        }

        return await PostAsync(client, changePath, fields, pageWithToken);
    }

    private async Task<Guid> BookAtTheDeskAsync(Guid clientId, Guid staffId, Instant start)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var appointment = await scope.ServiceProvider.GetRequiredService<IBookingService>()
            .BookAsync(new BookAppointmentRequest(clientId, staffId, _serviceId, start, AppointmentSource.Admin));
        return appointment.Id;
    }

    private async Task CancelBySalonAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<IBookingService>().CancelAsync(appointmentId, byClient: false, reason: "Salon closed");
    }

    private async Task SetStartAsync(Guid appointmentId, Instant start)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Start, start).SetProperty(a => a.End, start + Duration.FromMinutes(30)));
    }

    private async Task<Guid> ClientWithoutPhoneAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var client = new Client { FirstName = "No", LastName = "Phone", CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }
}