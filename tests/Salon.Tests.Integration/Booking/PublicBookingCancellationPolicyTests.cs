using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Appointments;
using Salon.Core.Settings;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Booking;

/// <summary>
/// The salon's cancellation policy as the client meets it (ADR 0022): said before they book, a change refused inside the
/// notice period, and a late cancellation still possible with the fee said first. The visit in these tests is tomorrow
/// at 10:00: always inside a 48-hour notice period and always outside a 2-hour one.
/// </summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task The_details_page_states_the_cancellation_policy_before_the_client_commits()
    {
        using var client = factory.ClientFor(HostA);
        (await GetAsync(client, DetailsPath(At(10, 0), _annaId))).ShouldNotContain("data-testid=\"cancellation-terms\"", Case.Sensitive, "no policy, nothing to say");

        await SavePolicyAsync(_studio.Id, hours: 24, latePercent: 50, noShowPercent: 100);
        var page = await GetAsync(client, DetailsPath(At(10, 0), _annaId));

        page.ShouldContain("data-testid=\"cancellation-terms\"");
        page.ShouldContain("Free up to 24 hours before the visit.");
        page.ShouldContain("Cancelling later than that may cost 50% of the price.");
        page.ShouldContain("Missing the visit without cancelling may cost 100% of the price.");

        // A no-show fee alone is a policy too; without a notice period nothing is said about late cancellation.
        await SavePolicyAsync(_studio.Id, hours: 0, latePercent: 0, noShowPercent: 50);
        var noShowOnly = await GetAsync(client, DetailsPath(At(10, 0), _annaId));
        noShowOnly.ShouldContain("Missing the visit without cancelling may cost 50% of the price.");
        noShowOnly.ShouldNotContain("data-testid=\"terms-free\"");
    }

    [Fact]
    public async Task Outside_the_notice_period_the_link_changes_and_cancels_as_before()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await SavePolicyAsync(_studio.Id, hours: 2, latePercent: 50, noShowPercent: 100);

        var change = await GetAsync(client, $"/book/change/{appointment.PublicToken}?date={Iso(_day)}");
        var cancel = await GetAsync(client, $"/book/cancel/{appointment.PublicToken}");

        change.ShouldContain("data-testid=\"change-form\"");
        cancel.ShouldContain("data-testid=\"change-instead\"");
        cancel.ShouldNotContain("data-testid=\"late-cancel-warning\"");
        var moved = await PostChangeAsync(client, $"/book/change/{appointment.PublicToken}", At(11, 0), _annaId, change);
        WebUtility.HtmlDecode(await moved.Content.ReadAsStringAsync()).ShouldContain("Your appointment has been moved.");
    }

    [Fact]
    public async Task Inside_the_notice_period_the_time_cannot_be_changed_online_and_cancelling_says_the_fee_first()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        var changePath = $"/book/change/{appointment.PublicToken}";
        var cancelPath = $"/book/cancel/{appointment.PublicToken}";
        var stale = await GetAsync(client, $"{changePath}?date={Iso(_day)}");
        await SavePolicyAsync(_studio.Id, hours: 48, latePercent: 50, noShowPercent: 100);

        // The change page explains and offers no times; a form left open from before is refused all the same.
        var change = await GetAsync(client, changePath);
        change.ShouldContain("It is less than 48 hours before your visit, so the time can no longer be changed online. Please contact the salon.");
        change.ShouldNotContain("data-testid=\"change-form\"");
        change.ShouldContain($"href=\"{cancelPath}\" data-testid=\"cancel-instead\"");
        var refused = await PostChangeAsync(client, changePath, At(11, 0), _annaId, stale);
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).ShouldContain("can no longer be changed online");
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().Start.ShouldBe(At(10, 0));

        // Cancelling is still possible (an empty chair the salon knows about beats a no-show), with the fee said first.
        var cancel = await GetAsync(client, cancelPath);
        cancel.ShouldContain("this is a late cancellation. The salon may charge a fee of £10.00.");
        cancel.ShouldNotContain("data-testid=\"change-instead\"");
        var done = await PostAsync(client, cancelPath, new(), cancel);
        var result = WebUtility.HtmlDecode(await done.Content.ReadAsStringAsync());
        result.ShouldContain("Your appointment has been cancelled.");
        result.ShouldContain("This was a late cancellation. The salon may charge a fee of £10.00.");
        (await AppointmentsAsync(_studio.Id)).ShouldHaveSingleItem().Status.ShouldBe(AppointmentStatus.CancelledByClient);

        // The policy only proposes: nothing is on the client's account until a person at the salon adds it.
        await using var scope = factory.CreateTenantScope(_studio.Id);
        (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_notice_period_without_a_fee_only_stops_online_changes()
    {
        using var client = factory.ClientFor(HostA);
        var (appointment, _) = await BookThroughTheFlowAsync(client, At(10, 0), _annaId);
        await SavePolicyAsync(_studio.Id, hours: 48, latePercent: 0, noShowPercent: 0);

        var cancel = await GetAsync(client, $"/book/cancel/{appointment.PublicToken}");

        cancel.ShouldContain("data-testid=\"confirm-cancel\"");
        cancel.ShouldNotContain("data-testid=\"late-cancel-warning\"");
        cancel.ShouldNotContain("data-testid=\"change-instead\"");
    }

    private async Task SavePolicyAsync(Guid tenantId, int hours, int latePercent, int noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings
        {
            FreeChangeHours = hours,
            LateCancellationFeePercent = latePercent,
            NoShowFeePercent = noShowPercent,
        });
    }
}