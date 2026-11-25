using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Booking;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Screens.Code;
using Salon.Mobile.Core.Screens.Details;
using Salon.Mobile.Core.Screens.Done;
using Salon.Mobile.Core.Screens.Services;
using Salon.Mobile.Core.Screens.Time;

namespace Salon.Tests.Mobile.Fakes;

/// <summary>One app's worth of shared objects, wired as the MAUI app would wire them.</summary>
internal sealed class Harness
{
    public static readonly Uri Host = new("https://studio.example.com/");

    public Harness(SampleServer? server = null)
    {
        Api = new FakeBookingApi(server);
        Session = new BookingSession(new SalonAppOptions { BaseAddress = Host });
    }

    public FakeBookingApi Api { get; }

    public FakeNavigator Navigator { get; } = new();

    public BookingSession Session { get; }

    public AppText Text { get; } = new();

    public SampleServer Server => Api.Server;

    public ServicesScreen ServicesScreen()
    {
        return new ServicesScreen(Api, Navigator, Session, Text);
    }

    public TimeScreen TimeScreen()
    {
        return new TimeScreen(Api, Navigator, Session, Text);
    }

    public DetailsScreen DetailsScreen()
    {
        return new DetailsScreen(Api, Navigator, Session, Text);
    }

    public CodeScreen CodeScreen()
    {
        return new CodeScreen(Api, Navigator, Session, Text);
    }

    public DoneScreen DoneScreen()
    {
        return new DoneScreen(Api, Navigator, Session, Text);
    }

    /// <summary>The services loaded and a visit of these services started, nobody chosen.</summary>
    public void Visit(params Guid[] services)
    {
        Session.SetServices(Server.ServiceList());
        Text.UseCulture(Server.Salon.Culture);
        Session.StartVisit(services[0]);
        foreach (var service in services.Skip(1))
        {
            Session.AddService(service).ShouldBeTrue();
        }
    }

    /// <summary>A time screen for the visit, loaded.</summary>
    public async Task<TimeScreen> LoadedTimeAsync(params Guid[] services)
    {
        Visit(services);
        var screen = TimeScreen();
        await screen.LoadAsync();
        return screen;
    }

    /// <summary>A visit with a time and every answer, Continue pressed: the details screen's starting point.</summary>
    public async Task ReadyForDetailsAsync()
    {
        var time = await LoadedTimeAsync(Sample.Cut, Sample.Beard);
        await time.ChooseStaffAsync(time.Cards.Count == 0 ? throw new InvalidOperationException() : StaffOf(time, Sample.Cut, Sample.Anna.ToString()));
        await time.PickTimeAsync(time.Times[1]);
        time.CanContinue.ShouldBeTrue();
        await time.ContinueAsync();
        Navigator.Last.ShouldBe(FakeNavigator.Details);
    }

    public static StaffOption StaffOf(TimeScreen screen, Guid serviceId, string value)
    {
        screen.OpenStaffChoice(screen.Cards.First(c => c.ServiceId == serviceId));
        return screen.StaffSheet!.Options.First(o => o.Value == value);
    }
}