using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Screens.Details;
using Salon.Tests.Mobile.Fakes;

namespace Salon.Tests.Mobile.Screens;

public class DetailsScreenTests
{
    [Fact]
    public async Task It_loads_the_terms_once_and_says_what_is_being_booked()
    {
        var app = new Harness();
        await app.ReadyForDetailsAsync();
        var screen = app.DetailsScreen();

        await screen.LoadAsync();
        await screen.LoadAsync();

        app.Api.TermsCalls.ShouldBe(1);
        screen.ConsentWording.ShouldBe("I agree to Studio Anna sending me news and offers.");
        screen.PrivacyUrl.ShouldBe(new Uri("https://studio.example.com/privacy"));
        screen.RequiresSmsCode.ShouldBeTrue();
        screen.SubmitText.ShouldBe("Send code");
        screen.PhoneHint.ShouldNotBeNull();
        screen.TermsLines.ShouldBe(
        [
            "Free up to 24 hours before the visit.",
            "Cancelling later than that may cost 50% of the price.",
            "Missing the visit without cancelling may cost 100% of the price.",
        ]);
        screen.ServicesText.ShouldBe("Cut + Beard trim");
        screen.WhenText.ShouldBe("Thursday 26 November 2026, 09:45 – 10:50");
        screen.TotalText.ShouldBe("from £61.00");
        screen.MarketingConsent.ShouldBeFalse();
    }

    [Fact]
    public async Task Where_no_code_is_asked_the_button_books_and_the_terms_follow_the_numbers()
    {
        var app = new Harness();
        app.Server.Terms = new BookingTerms(false, "Yes please.", "/privacy", 1, 0m, 0m);
        await app.ReadyForDetailsAsync();
        var screen = app.DetailsScreen();

        await screen.LoadAsync();

        screen.SubmitText.ShouldBe("Book");
        screen.PhoneHint.ShouldBeNull();
        screen.TermsLines.ShouldBe(["Free up to 1 hour before the visit."]);
    }

    [Fact]
    public async Task Without_a_chosen_time_it_goes_back_to_the_time_screen()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        var screen = app.DetailsScreen();

        await screen.LoadAsync();

        app.Navigator.Shown.ShouldBe([FakeNavigator.Time]);
        app.Api.TermsCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Terms_offline_wait_for_a_retry()
    {
        var app = new Harness();
        await app.ReadyForDetailsAsync();
        app.Api.OnTerms = ApiResult.Offline<BookingTerms>;
        var screen = app.DetailsScreen();

        await screen.LoadAsync();

        screen.HasError.ShouldBeTrue();
        screen.ErrorMessage.ShouldBe(app.Text[TextKeys.Offline]);

        app.Api.OnTerms = () => ApiResult.Ok(app.Server.Terms);
        screen.RetryCommand.Execute(null);
        await Task.Yield();

        screen.HasError.ShouldBeFalse();
        screen.ConsentWording.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Empty_required_fields_are_flagged_here_and_nothing_is_sent()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);
        screen.Email = "not checked here";

        await screen.SubmitAsync();

        screen.FirstNameError.ShouldBe("Enter your first name.");
        screen.LastNameError.ShouldBe("Enter your last name.");
        screen.PhoneError.ShouldBe("Enter your mobile number.");
        screen.EmailError.ShouldBeNull();
        app.Api.StartRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Typing_in_a_field_clears_its_error()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);
        await screen.SubmitAsync();

        screen.FirstName = "A";

        screen.FirstNameError.ShouldBeNull();
        screen.LastNameError.ShouldNotBeNull();
    }

    [Fact]
    public async Task Submit_sends_the_visit_with_every_answer_said_and_the_details_trimmed()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);
        Fill(screen);
        screen.Email = "   ";
        screen.MarketingConsent = true;

        await screen.SubmitAsync();

        var request = app.Api.StartRequests.ShouldHaveSingleItem();
        request.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, Sample.Anna.ToString()), new VisitPartRequest(Sample.Beard, Sample.Ben.ToString())]);
        request.Start.ShouldBe("2026-11-26T09:45:00Z");
        request.FirstName.ShouldBe("Ada");
        request.LastName.ShouldBe("Lovelace");
        request.Phone.ShouldBe("07700 900789");
        request.Email.ShouldBeNull();
        request.MarketingConsent.ShouldBeTrue();
    }

    [Fact]
    public async Task A_part_with_nothing_said_and_nobody_named_books_with_anyone()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        app.Session.Availability = Sample.Answer([Sample.Part(Sample.Cut, "Cut", needsStaffChoice: false, anyone: true, localStart: "09:00", localEnd: "09:45")], chosenStart: "2026-11-26T09:00:00Z", canContinue: true);
        var screen = app.DetailsScreen();
        await screen.LoadAsync();
        Fill(screen);

        await screen.SubmitAsync();

        app.Api.StartRequests.ShouldHaveSingleItem().Parts.ShouldBe([new VisitPartRequest(Sample.Cut, BookingApi.AnyStaff)]);
    }

    [Fact]
    public async Task A_code_sent_goes_to_the_code_screen()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        app.Session.VerificationId.ShouldBe(Sample.VerificationId);
        app.Session.MaskedPhone.ShouldBe("+44 •••• ••• 789");
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);
        screen.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Booked_without_a_code_goes_straight_to_done()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Ok(new StartBookingResponse(null, Sample.AppointmentId, null));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        app.Session.AppointmentId.ShouldBe(Sample.AppointmentId);
        app.Session.VerificationId.ShouldBeNull();
        app.Navigator.Last.ShouldBe(FakeNavigator.Done);
    }

    [Fact]
    public async Task An_answer_with_neither_id_is_unexpected()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Ok(new StartBookingResponse(null, null, null));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        screen.Message.ShouldBe(app.Text[TextKeys.Unexpected]);
        app.Navigator.Last.ShouldBe(FakeNavigator.Details);
    }

    [Fact]
    public async Task The_servers_field_messages_land_under_their_fields_whatever_the_case_of_the_name()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(400, new ApiProblem(
            "validation",
            "Check the form.",
            new Dictionary<string, string[]>
            {
                ["phone"] = ["Enter a valid mobile number; use the country code for numbers from abroad."],
                ["Email"] = ["Enter a valid email address."],
                ["FirstName"] = ["A name can have at most 100 characters."],
                ["LASTNAME"] = ["Too long."],
            }));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        screen.PhoneError.ShouldBe("Enter a valid mobile number; use the country code for numbers from abroad.");
        screen.EmailError.ShouldBe("Enter a valid email address.");
        screen.FirstNameError.ShouldBe("A name can have at most 100 characters.");
        screen.LastNameError.ShouldBe("Too long.");
        screen.Message.ShouldBeNull();
        app.Navigator.Last.ShouldBe(FakeNavigator.Details);
    }

    [Fact]
    public async Task A_field_the_form_does_not_have_shows_over_the_form()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(400, new ApiProblem(
            "validation",
            "Check the form.",
            new Dictionary<string, string[]> { ["Start"] = ["Choose a time first."] }));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        screen.Message.ShouldBe("Choose a time first.");
    }

    [Fact]
    public async Task A_validation_refusal_without_fields_shows_its_sentence()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(400, new ApiProblem("validation", "Something in the form is not right."));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        screen.Message.ShouldBe("Something in the form is not right.");
    }

    [Fact]
    public async Task A_time_taken_meanwhile_goes_back_to_the_time_screen_with_a_message()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(409, new ApiProblem("slotTaken", "Taken."));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        app.Navigator.Last.ShouldBe(FakeNavigator.Time);
        app.Session.Start.ShouldBeNull();
        app.Session.Parts.Count.ShouldBe(2);
        app.Session.Date.ShouldBe("2026-11-26");

        var time = app.TimeScreen();
        await time.LoadAsync();
        time.Message.ShouldBe("Sorry, that time has just gone. Please pick another.");
        app.Api.LastAvailability.Start.ShouldBeNull();
        app.Api.LastAvailability.Parts[0].Staff.ShouldBe(Sample.Anna.ToString());
    }

    [Fact]
    public async Task Too_many_codes_shows_the_servers_sentence_or_ours()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(429, new ApiProblem("tooManyCodes", "Wait an hour."));
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();
        screen.Message.ShouldBe("Wait an hour.");

        app.Api.OnStart = _ => ApiResult.Refused<StartBookingResponse>(429, new ApiProblem("tooManyCodes", string.Empty));
        await screen.SubmitAsync();
        screen.Message.ShouldBe("We have already sent several codes to this number. Please wait an hour and try again.");
        app.Navigator.Last.ShouldBe(FakeNavigator.Details);
    }

    [Fact]
    public async Task Offline_says_so_and_keeps_the_form()
    {
        var app = new Harness();
        app.Api.OnStart = _ => ApiResult.Offline<StartBookingResponse>();
        var screen = await LoadedAsync(app);
        Fill(screen);

        await screen.SubmitAsync();

        screen.Message.ShouldBe(app.Text[TextKeys.Offline]);
        screen.FirstName.ShouldBe(" Ada ");
        app.Navigator.Last.ShouldBe(FakeNavigator.Details);
    }

    [Fact]
    public async Task The_privacy_notice_opens_in_the_browser()
    {
        var app = new Harness();
        var screen = await LoadedAsync(app);

        await screen.OpenPrivacyAsync();

        app.Navigator.Opened.ShouldBe([new Uri("https://studio.example.com/privacy")]);
    }

    private static async Task<DetailsScreen> LoadedAsync(Harness app)
    {
        await app.ReadyForDetailsAsync();
        var screen = app.DetailsScreen();
        await screen.LoadAsync();
        return screen;
    }

    private static void Fill(DetailsScreen screen)
    {
        screen.FirstName = " Ada ";
        screen.LastName = "Lovelace";
        screen.Phone = "07700 900789 ";
    }
}