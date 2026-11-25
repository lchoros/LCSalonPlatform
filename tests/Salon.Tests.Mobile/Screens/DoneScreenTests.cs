using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;
using Salon.Tests.Mobile.Fakes;

namespace Salon.Tests.Mobile.Screens;

public class DoneScreenTests
{
    [Fact]
    public async Task It_shows_what_when_who_with_and_the_price_as_the_server_sent_them()
    {
        var app = new Harness();
        app.Session.AppointmentId = Sample.AppointmentId;
        var screen = app.DoneScreen();

        await screen.LoadAsync();

        app.Api.VisitRequests.ShouldBe([Sample.AppointmentId]);
        screen.HasVisit.ShouldBeTrue();
        screen.ServiceName.ShouldBe("Cut + Colour");
        screen.StaffName.ShouldBe("Anna");
        screen.WhenText.ShouldBe("Thursday 26 November 2026, 09:45");
        screen.PriceText.ShouldBe("£105.00");
        screen.Status.ShouldBe("booked");
        screen.ManageUrl.ShouldBe(new Uri("https://studio.example.com/book/manage/abc123"));
    }

    [Fact]
    public async Task In_polish_the_day_reads_in_polish()
    {
        var app = new Harness();
        app.Text.UseCulture("pl-PL");
        app.Api.OnVisit = _ => ApiResult.Ok(Sample.Visit() with { Price = 120m, Currency = "PLN" });
        app.Session.AppointmentId = Sample.AppointmentId;
        var screen = app.DoneScreen();

        await screen.LoadAsync();

        screen.WhenText.ShouldBe("czwartek 26 listopada 2026, godz. 09:45");
        screen.PriceText.ShouldBe("120,00 zł");
    }

    [Fact]
    public async Task An_absolute_manage_address_is_kept_as_it_is()
    {
        var app = new Harness();
        app.Api.OnVisit = _ => ApiResult.Ok(Sample.Visit("https://studio.example.com/book/manage/xyz"));
        app.Session.AppointmentId = Sample.AppointmentId;
        var screen = app.DoneScreen();

        await screen.LoadAsync();

        screen.ManageUrl.ShouldBe(new Uri("https://studio.example.com/book/manage/xyz"));
    }

    [Fact]
    public async Task Manage_opens_the_visits_page_in_the_browser()
    {
        var app = new Harness();
        app.Session.AppointmentId = Sample.AppointmentId;
        var screen = app.DoneScreen();
        await screen.LoadAsync();

        await screen.ManageAsync();

        app.Navigator.Opened.ShouldBe([new Uri("https://studio.example.com/book/manage/abc123")]);
    }

    [Fact]
    public async Task Book_another_starts_again_from_the_services_with_the_salon_kept()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        app.Session.AppointmentId = Sample.AppointmentId;
        var screen = app.DoneScreen();
        await screen.LoadAsync();

        await screen.BookAnotherAsync();

        app.Session.Parts.ShouldBeEmpty();
        app.Session.AppointmentId.ShouldBeNull();
        app.Session.HasServices.ShouldBeTrue();
        app.Navigator.Last.ShouldBe(FakeNavigator.Services);
    }

    [Fact]
    public async Task Offline_says_so_and_retry_loads()
    {
        var app = new Harness();
        app.Session.AppointmentId = Sample.AppointmentId;
        app.Api.OnVisit = _ => ApiResult.Offline<BookedVisit>();
        var screen = app.DoneScreen();

        await screen.LoadAsync();

        screen.HasError.ShouldBeTrue();
        screen.ErrorMessage.ShouldBe(app.Text[TextKeys.Offline]);
        screen.HasVisit.ShouldBeFalse();

        app.Api.OnVisit = _ => ApiResult.Ok(Sample.Visit());
        await screen.LoadAsync();

        screen.HasError.ShouldBeFalse();
        screen.HasVisit.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_an_appointment_there_is_nothing_to_show()
    {
        var app = new Harness();
        var screen = app.DoneScreen();

        await screen.LoadAsync();

        screen.HasVisit.ShouldBeFalse();
        screen.HasError.ShouldBeTrue();
        app.Api.VisitRequests.ShouldBeEmpty();
    }
}