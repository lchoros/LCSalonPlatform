using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Salon.Core.Appointments;
using Salon.Services.Clients.Import;
using Salon.Services.Payments;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Ui;

namespace Salon.Tests.Unit.Mvc;

/// <summary>What the views ask instead of deciding it themselves (ADR 0043).</summary>
public class ViewHelpersTests
{
    [Fact]
    public void Every_visit_status_has_a_badge_on_its_card_and_only_what_the_desk_acts_on_stands_out_in_a_list()
    {
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            Badges.Appointment(status).ShouldNotBeNullOrWhiteSpace(status.ToString());
            Badges.AppointmentInHistory(status).ShouldNotBeNullOrWhiteSpace(status.ToString());
        }

        Badges.Appointment(AppointmentStatus.NoShow).ShouldBe(Badges.Appointment(AppointmentStatus.CancelledBySalon));
        Badges.Appointment(AppointmentStatus.Completed).ShouldNotBe(Badges.Appointment(AppointmentStatus.Booked));

        var loud = Enum.GetValues<AppointmentStatus>().Where(s => Badges.AppointmentInList(s) != Badges.AppointmentInList(AppointmentStatus.Booked)).ToArray();
        loud.ShouldBe([AppointmentStatus.Confirmed, AppointmentStatus.Arrived]);
        Badges.AppointmentInList(AppointmentStatus.Arrived).ShouldBe(Badges.Appointment(AppointmentStatus.Arrived), "the same status wears the same colour where both show it");
    }

    [Fact]
    public void A_fee_and_an_import_row_each_have_three_looks()
    {
        Enum.GetValues<FeeStatus>().Select(Badges.Fee).Distinct().Count().ShouldBe(3);
        Enum.GetValues<ImportRowStatus>().Select(Badges.ImportRow).Distinct().Count().ShouldBe(3);
        Badges.Fee(FeeStatus.Paid).ShouldBe(Badges.ImportRow(ImportRowStatus.New), "good news is one green");
    }

    [Theory]
    [InlineData(true, true, "is-on")]
    [InlineData(true, false, "is-on")]
    [InlineData(false, true, "is-off")]
    [InlineData(false, false, "is-dim")]
    public void A_day_tile_is_chosen_free_or_muted(bool chosen, bool hasFreeTimes, string expected)
    {
        Badges.DayTile(chosen, hasFreeTimes).ShouldBe(expected);
    }

    [Fact]
    public void The_answer_given_most_often_is_the_primary_button()
    {
        Badges.OutcomeButton(VisitOutcome.Came).ShouldContain("btn-primary");
        Badges.OutcomeButton(VisitOutcome.DidNotCome).ShouldContain("btn-secondary");
    }

    [Fact]
    public void A_fields_messages_come_in_order_and_a_field_without_any_is_fine()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("Name", "Enter the name.");
        state.AddModelError("Name", "The name is too long.");

        state.ErrorsFor("Name").ShouldBe(["Enter the name.", "The name is too long."]);
        state.ErrorsFor("Price").ShouldBeEmpty();
        state.InvalidClass("Name").ShouldBe("input-validation-error");
        state.InvalidClass("Price").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/admin/staff", "/admin/staff", true)]
    [InlineData("/admin/staff/new", "/admin/staff", true)]
    [InlineData("/Admin/Staff", "/admin/staff", true)]
    [InlineData("/admin/staffing", "/admin/staff", false)]
    [InlineData("/admin/appointments/calendar", "/admin/appointments", true)]
    [InlineData("/", "/admin/staff", false)]
    public void A_menu_link_is_current_for_its_area_and_what_lies_under_it(string path, string area, bool current)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        context.Request.Current(area).ShouldBe(current ? "true" : null);
    }
}