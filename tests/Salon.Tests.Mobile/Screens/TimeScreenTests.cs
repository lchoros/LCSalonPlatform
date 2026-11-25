using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Screens.Time;
using Salon.Tests.Mobile.Fakes;

namespace Salon.Tests.Mobile.Screens;

public class TimeScreenTests
{
    [Fact]
    public async Task The_first_call_holds_one_part_and_no_staff_and_leaves_the_window_to_the_server()
    {
        var app = new Harness();

        await app.LoadedTimeAsync(Sample.Cut);

        app.Api.AvailabilityRequests.ShouldHaveSingleItem();
        app.Api.LastAvailability.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, null)]);
        app.Api.LastAvailability.From.ShouldBeNull();
        app.Api.LastAvailability.Date.ShouldBeNull();
        app.Api.LastAvailability.Start.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_visit_the_screen_goes_back_to_the_services()
    {
        var app = new Harness();
        var screen = app.TimeScreen();

        await screen.LoadAsync();

        app.Navigator.Shown.ShouldBe([FakeNavigator.Services]);
        app.Api.AvailabilityRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_month_and_the_days_come_from_the_answer()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Cut);

        screen.HasAnswer.ShouldBeTrue();
        screen.MonthTitle.ShouldBe("November 2026");
        screen.Days.Count.ShouldBe(7);
        screen.Days[0].ShouldBe(new DayModel("2026-11-26", "Thu", "26", true, true));
        screen.Days[2].HasTimes.ShouldBeFalse();
        screen.Days.Count(d => d.IsSelected).ShouldBe(1);
        screen.CanGoEarlier.ShouldBeFalse();
        screen.CanGoLater.ShouldBeTrue();
    }

    [Fact]
    public async Task The_month_is_in_the_salons_language()
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon("pl-PL", "PLN");

        var screen = await app.LoadedTimeAsync(Sample.Cut);

        screen.MonthTitle.ShouldBe("Listopad 2026");
        screen.Days[0].WeekdayText.ShouldBe("czw.");
        screen.PartsOfDay.Select(p => p.Label).ShouldBe(["Rano", "Po południu", "Wieczorem"]);
        screen.Cards[0].StaffText.ShouldBe("Jeszcze nie wybrano");
        screen.TotalText.ShouldBe("45,00 zł");
    }

    [Fact]
    public async Task Parts_of_the_day_are_the_ones_with_times_and_the_first_is_selected()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Cut);

        screen.PartsOfDay.Select(p => p.Key).ShouldBe(["morning", "afternoon", "evening"]);
        screen.PartsOfDay.Select(p => p.Label).ShouldBe(["Morning", "Afternoon", "Evening"]);
        screen.PartsOfDay[0].IsSelected.ShouldBeTrue();
        screen.PartsOfDay[0].Times.Select(t => t.LocalTime).ShouldBe(["09:00", "09:45"]);
        screen.Times.Select(t => t.LocalTime).ShouldBe(["09:00", "09:45"]);
        screen.NoTimesText.ShouldBeNull();
    }

    [Fact]
    public async Task A_part_of_the_day_without_times_has_no_tab()
    {
        var app = new Harness();
        app.Api.OnAvailability = (_, _) => Task.FromResult(ApiResult.Ok(Sample.Answer([Sample.Part(Sample.Cut, "Cut")])));

        var screen = await app.LoadedTimeAsync(Sample.Cut);

        screen.PartsOfDay.Select(p => p.Key).ShouldBe(["morning", "afternoon"]);
    }

    [Fact]
    public async Task Choosing_a_tab_shows_its_times_without_a_call()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        screen.SelectPartOfDay(screen.PartsOfDay[2]);

        screen.Times.Select(t => t.LocalTime).ShouldBe(["18:00"]);
        screen.PartsOfDay.Single(p => p.IsSelected).Key.ShouldBe("evening");
        app.Api.AvailabilityRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_day_without_times_says_so()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.PickDayAsync(screen.Days[2]);

        screen.Times.ShouldBeEmpty();
        screen.PartsOfDay.ShouldBeEmpty();
        screen.NoTimesText.ShouldBe("No free times on this day. Try another day.");
    }

    [Fact]
    public async Task A_window_without_any_times_says_so()
    {
        var app = new Harness();
        app.Api.OnAvailability = (_, _) => Task.FromResult(ApiResult.Ok(Sample.Answer([Sample.Part(Sample.Cut, "Cut")], times: []) with
        {
            Days = [new DayItem("2026-11-26", false)],
        }));

        var screen = await app.LoadedTimeAsync(Sample.Cut);

        screen.NoTimesText.ShouldBe("No free times on these days. Try later days.");
    }

    [Fact]
    public async Task The_card_shows_name_price_length_and_that_who_with_is_owed()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Colour);

        var card = screen.Cards.ShouldHaveSingleItem();
        card.Name.ShouldBe("Colour");
        card.PriceText.ShouldBe("from £60.00");
        card.TimeText.ShouldBeNull();
        card.TimeOrDurationText.ShouldBe("60 min");
        card.StaffText.ShouldBe("Not chosen yet");
        card.StaffRequired.ShouldBeTrue();
        card.CanChooseStaff.ShouldBeTrue();
        card.StaffActionText.ShouldBe("Choose");
        screen.TotalText.ShouldBe("from £60.00");
        screen.TotalDurationText.ShouldBe("60 min");
        screen.CanContinue.ShouldBeFalse();
        screen.CanAddService.ShouldBeFalse();
        screen.ChosenTimeText.ShouldBeNull();
    }

    [Fact]
    public async Task Where_one_person_does_it_the_card_names_them_and_nothing_is_owed()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Beard);

        var card = screen.Cards.ShouldHaveSingleItem();
        card.StaffText.ShouldBe("Ben");
        card.StaffRequired.ShouldBeFalse();
        card.StaffActionText.ShouldBe("Change");
    }

    [Fact]
    public async Task The_staff_sheet_offers_anyone_first_then_the_people_with_their_photos()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Cut);

        screen.OpenStaffChoice(screen.Cards[0]);

        screen.IsStaffSheetOpen.ShouldBeTrue();
        var sheet = screen.StaffSheet!;
        sheet.Title.ShouldBe("Who would you like for Cut?");
        sheet.Note.ShouldBeNull();
        sheet.Options.Select(o => o.Name).ShouldBe(["Anyone available", "Anna", "Ben"]);
        sheet.Options[0].IsAnyone.ShouldBeTrue();
        sheet.Options[0].Value.ShouldBe(BookingApi.AnyStaff);
        sheet.Options[1].Value.ShouldBe(Sample.Anna.ToString());
        sheet.Options[1].PhotoUrl.ShouldBe(new Uri("https://studio.example.com/media/staff/anna.jpg"));
        sheet.Options[2].PhotoUrl.ShouldBeNull();
        sheet.Options.ShouldAllBe(o => !o.IsChosen);

        screen.CloseStaffChoice();
        screen.IsStaffSheetOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Choosing_a_person_asks_again_with_that_answer_and_the_card_names_them()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, Sample.Anna.ToString()));

        screen.IsStaffSheetOpen.ShouldBeFalse();
        app.Api.LastAvailability.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, Sample.Anna.ToString())]);
        screen.Cards[0].StaffText.ShouldBe("Anna");
        screen.Cards[0].StaffPhotoUrl.ShouldBe(new Uri("https://studio.example.com/media/staff/anna.jpg"));
        screen.Cards[0].StaffRequired.ShouldBeFalse();
        screen.Cards[0].StaffActionText.ShouldBe("Change");

        screen.OpenStaffChoice(screen.Cards[0]);
        screen.StaffSheet!.Options.Single(o => o.IsChosen).Name.ShouldBe("Anna");
    }

    [Fact]
    public async Task Anyone_is_an_answer_shown_as_no_preference()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, BookingApi.AnyStaff));

        app.Api.LastAvailability.Parts[0].Staff.ShouldBe("any");
        screen.Cards[0].StaffText.ShouldBe("No preference");
        screen.Cards[0].StaffRequired.ShouldBeFalse();
        screen.OpenStaffChoice(screen.Cards[0]);
        screen.StaffSheet!.Options.Single(o => o.IsChosen).IsAnyone.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_part_has_its_own_answer()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut, Sample.Colour);

        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Colour, Sample.Ben.ToString()));
        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, BookingApi.AnyStaff));

        app.Api.LastAvailability.Parts.ShouldBe([new VisitPartRequest(Sample.Cut, "any"), new VisitPartRequest(Sample.Colour, Sample.Ben.ToString())]);
        screen.Cards.Select(c => c.StaffText).ShouldBe(["No preference", "Ben"]);
    }

    [Fact]
    public async Task Tapping_a_time_sends_it_and_the_cards_get_their_own_times()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut, Sample.Beard);

        await screen.PickTimeAsync(screen.Times[1]);

        app.Api.LastAvailability.Start.ShouldBe("2026-11-26T09:45:00Z");
        app.Api.LastAvailability.Date.ShouldBe("2026-11-26");
        app.Session.Start.ShouldBe("2026-11-26T09:45:00Z");
        screen.Times.Single(t => t.IsChosen).LocalTime.ShouldBe("09:45");
        screen.Cards.Select(c => c.TimeText).ShouldBe(["09:45 – 10:30", "10:30 – 10:50"]);
        screen.Cards[0].TimeOrDurationText.ShouldBe("09:45 – 10:30");
        screen.ChosenTimeText.ShouldBe("09:45 – 10:50");
        screen.TotalText.ShouldBe("from £61.00");
        screen.TotalDurationText.ShouldBe("65 min");
    }

    [Fact]
    public async Task With_a_time_tapped_the_sheet_says_whose_list_it_is()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Cut);
        await screen.PickTimeAsync(screen.Times[1]);

        screen.OpenStaffChoice(screen.Cards[0]);

        screen.StaffSheet!.Note.ShouldBe("These are the people free at 09:45. For somebody else, choose another time first.");
    }

    [Fact]
    public async Task The_tab_of_a_chosen_evening_time_stays_selected()
    {
        var screen = await new Harness().LoadedTimeAsync(Sample.Cut);
        screen.SelectPartOfDay(screen.PartsOfDay[2]);

        await screen.PickTimeAsync(screen.Times[0]);

        screen.PartsOfDay.Single(p => p.IsSelected).Key.ShouldBe("evening");
        screen.Times.ShouldHaveSingleItem().IsChosen.ShouldBeTrue();
    }

    [Fact]
    public async Task A_tapped_time_that_comes_back_not_chosen_clears_quietly()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        var slot = screen.Times[0];
        app.Server.Taken.Add(slot.Start);

        await screen.PickTimeAsync(slot);

        app.Api.LastAvailability.Start.ShouldBe(slot.Start);
        app.Session.Start.ShouldBeNull();
        screen.Times.ShouldAllBe(t => !t.IsChosen);
        screen.Message.ShouldBeNull();
        screen.ChosenTimeText.ShouldBeNull();

        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, BookingApi.AnyStaff));
        app.Api.LastAvailability.Start.ShouldBeNull();
    }

    [Fact]
    public async Task A_new_day_drops_the_tapped_time()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        await screen.PickTimeAsync(screen.Times[0]);

        await screen.PickDayAsync(screen.Days[1]);

        app.Api.LastAvailability.Date.ShouldBe("2026-11-27");
        app.Api.LastAvailability.Start.ShouldBeNull();
        screen.Days.Single(d => d.IsSelected).Date.ShouldBe("2026-11-27");
    }

    [Fact]
    public async Task Later_and_earlier_move_the_window_and_let_the_server_pick_the_day()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        await screen.PickTimeAsync(screen.Times[0]);

        await screen.LaterAsync();

        app.Api.LastAvailability.From.ShouldBe("2026-12-03");
        app.Api.LastAvailability.Date.ShouldBeNull();
        app.Api.LastAvailability.Start.ShouldBeNull();
        screen.MonthTitle.ShouldBe("December 2026");
        screen.CanGoEarlier.ShouldBeTrue();

        await screen.EarlierAsync();

        app.Api.LastAvailability.From.ShouldBe("2026-11-26");
        screen.CanGoEarlier.ShouldBeFalse();
    }

    [Fact]
    public async Task Earlier_at_today_does_nothing()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.EarlierAsync();

        app.Api.AvailabilityRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_last_tap_wins_and_the_earlier_call_is_cancelled()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        var gate = new GatedAvailability(app);
        var slow = screen.PickDayAsync(screen.Days[1]);
        var fast = screen.PickDayAsync(screen.Days[3]);

        gate.Calls.Count.ShouldBe(2);
        gate.Calls[0].Token.IsCancellationRequested.ShouldBeTrue();
        gate.Calls[1].Token.IsCancellationRequested.ShouldBeFalse();
        screen.IsBusy.ShouldBeTrue();

        gate.Answer(1);
        await fast;
        gate.Answer(0);
        await slow;

        screen.Days.Single(d => d.IsSelected).Date.ShouldBe("2026-11-29");
        app.Session.Date.ShouldBe("2026-11-29");
        screen.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_superseded_call_that_ends_in_cancellation_is_dropped_quietly()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        var gate = new GatedAvailability(app, throwWhenCancelled: true);
        var slow = screen.PickTimeAsync(screen.Times[0]);
        var fast = screen.PickTimeAsync(screen.Times[1]);

        gate.Answer(1);
        await fast;
        await slow;

        screen.Times.Single(t => t.IsChosen).LocalTime.ShouldBe("09:45");
        screen.Message.ShouldBeNull();
        screen.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Continue_before_a_time_says_choose_a_time_and_stays()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Beard);

        await screen.ContinueAsync();

        screen.Message.ShouldBe("Choose a time first.");
        screen.HighlightedCard.ShouldBeNull();
        screen.Cards.ShouldAllBe(c => !c.IsHighlighted);
        app.Navigator.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Continue_with_a_time_but_who_with_owed_says_so_and_lights_that_part_up()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Beard, Sample.Cut);
        await screen.PickTimeAsync(screen.Times[0]);
        screen.CanContinue.ShouldBeFalse();

        await screen.ContinueAsync();

        screen.Message.ShouldBe("Choose who with first.");
        screen.Cards.Select(c => c.IsHighlighted).ShouldBe([false, true]);
        screen.HighlightedCard!.ServiceId.ShouldBe(Sample.Cut);
        app.Navigator.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Continue_with_neither_says_both()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.ContinueAsync();

        screen.Message.ShouldBe("Choose a time and who with first.");
        screen.Cards[0].IsHighlighted.ShouldBeTrue();
    }

    [Fact]
    public async Task The_next_tap_clears_the_message_and_the_highlight()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        await screen.ContinueAsync();

        await screen.PickTimeAsync(screen.Times[0]);

        screen.Message.ShouldBeNull();
        screen.HighlightedCard.ShouldBeNull();
        screen.Cards[0].IsHighlighted.ShouldBeFalse();
    }

    [Fact]
    public async Task Continue_when_allowed_keeps_the_answer_for_the_details_and_goes_on()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Beard);
        await screen.PickTimeAsync(screen.Times[0]);
        screen.CanContinue.ShouldBeTrue();

        await screen.ContinueAsync();

        app.Navigator.Shown.ShouldBe([FakeNavigator.Details]);
        app.Session.Availability!.ChosenStart.ShouldBe("2026-11-26T09:00:00Z");
        screen.Message.ShouldBeNull();
    }

    [Fact]
    public async Task Continue_while_a_tap_is_on_its_way_waits_for_the_answer()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Beard);
        await screen.PickTimeAsync(screen.Times[0]);
        var gate = new GatedAvailability(app);
        var tap = screen.PickTimeAsync(screen.Times[1]);

        await screen.ContinueAsync();

        app.Navigator.Shown.ShouldBeEmpty();
        screen.Message.ShouldBeNull();
        gate.Answer(0);
        await tap;
    }

    [Fact]
    public async Task Add_another_service_before_a_time_says_what_is_missing()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        screen.ShowAddService.ShouldBeTrue();
        screen.CanAddService.ShouldBeFalse();

        await screen.AddServiceAsync();

        screen.Message.ShouldBe("Choose a time and who with first.");
        screen.Cards[0].IsHighlighted.ShouldBeTrue();
        app.Session.IsAdding.ShouldBeFalse();
        app.Navigator.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_another_service_with_who_with_owed_says_so()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        await screen.PickTimeAsync(screen.Times[0]);

        await screen.AddServiceAsync();

        screen.Message.ShouldBe("Choose who with first.");
        screen.HighlightedCard!.ServiceId.ShouldBe(Sample.Cut);
    }

    [Fact]
    public async Task Add_another_service_when_allowed_opens_the_list_in_adding_mode()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Beard);
        await screen.PickTimeAsync(screen.Times[0]);
        screen.CanAddService.ShouldBeTrue();

        await screen.AddServiceAsync();

        app.Session.IsAdding.ShouldBeTrue();
        app.Navigator.Shown.ShouldBe([FakeNavigator.Services]);
        app.Session.Start.ShouldBe("2026-11-26T09:00:00Z");
    }

    [Fact]
    public async Task A_salon_of_one_service_per_booking_offers_no_add()
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon(severalServices: false);
        var screen = await app.LoadedTimeAsync(Sample.Beard);
        await screen.PickTimeAsync(screen.Times[0]);

        screen.ShowAddService.ShouldBeFalse();
        screen.CanAddService.ShouldBeFalse();

        await screen.AddServiceAsync();
        screen.Message.ShouldBe("This booking cannot hold another service.");
        app.Navigator.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_full_visit_offers_no_add()
    {
        var app = new Harness();
        app.Server.Salon = Sample.Salon(maxServices: 2);

        var screen = await app.LoadedTimeAsync(Sample.Cut, Sample.Beard);

        screen.ShowAddService.ShouldBeFalse();
    }

    [Fact]
    public async Task Removing_a_part_moves_the_next_one_up_and_keeps_the_day_and_time()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut, Sample.Beard);
        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, Sample.Anna.ToString()));
        await screen.PickTimeAsync(screen.Times[0]);

        await screen.RemovePartAsync(screen.Cards[0]);

        app.Api.LastAvailability.Parts.ShouldBe([new VisitPartRequest(Sample.Beard, null)]);
        app.Api.LastAvailability.Start.ShouldBe("2026-11-26T09:00:00Z");
        screen.Cards.ShouldHaveSingleItem().Name.ShouldBe("Beard trim");
        screen.Cards[0].Index.ShouldBe(0);
        screen.Cards[0].TimeText.ShouldBe("09:00 – 09:20");
    }

    [Fact]
    public async Task Removing_the_last_part_goes_back_to_the_services_with_nothing_kept()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        await screen.PickTimeAsync(screen.Times[0]);
        var calls = app.Api.AvailabilityRequests.Count;

        await screen.RemovePartAsync(screen.Cards[0]);

        app.Navigator.Shown.ShouldBe([FakeNavigator.Services]);
        app.Session.Parts.ShouldBeEmpty();
        app.Session.Start.ShouldBeNull();
        app.Session.Date.ShouldBeNull();
        app.Api.AvailabilityRequests.Count.ShouldBe(calls);
        screen.HasAnswer.ShouldBeFalse();
        screen.Cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_left_for_the_screen_shows_when_it_appears()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        app.Session.PostNotice("Sorry, that time has just gone. Please pick another.");
        var screen = app.TimeScreen();

        await screen.LoadAsync();

        screen.Message.ShouldBe("Sorry, that time has just gone. Please pick another.");
        app.Session.TakeNotice().ShouldBeNull();

        screen.DismissMessageCommand.Execute(null);
        screen.Message.ShouldBeNull();
    }

    [Fact]
    public async Task Offline_on_the_first_call_fills_the_screen_with_an_error_and_retry_asks_again()
    {
        var app = new Harness();
        app.Api.OnAvailability = (_, _) => Task.FromResult(ApiResult.Offline<Availability>());

        var screen = await app.LoadedTimeAsync(Sample.Cut);

        screen.HasError.ShouldBeTrue();
        screen.ErrorMessage.ShouldBe(app.Text[TextKeys.Offline]);
        screen.HasAnswer.ShouldBeFalse();
        screen.IsBusy.ShouldBeFalse();

        app.Api.OnAvailability = (request, _) => Task.FromResult(ApiResult.Ok(app.Server.Availability(request)));
        await screen.RefreshAsync();

        screen.HasError.ShouldBeFalse();
        screen.HasAnswer.ShouldBeTrue();
    }

    [Fact]
    public async Task Offline_after_a_tap_keeps_the_screen_and_says_so()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        app.Api.OnAvailability = (_, _) => Task.FromResult(ApiResult.Offline<Availability>());

        await screen.PickDayAsync(screen.Days[1]);

        screen.HasError.ShouldBeFalse();
        screen.Message.ShouldBe(app.Text[TextKeys.Offline]);
        screen.Days.Single(d => d.IsSelected).Date.ShouldBe("2026-11-26");
    }

    [Fact]
    public async Task A_refused_call_shows_the_servers_sentence()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);
        app.Api.OnAvailability = (_, _) => Task.FromResult(ApiResult.Refused<Availability>(400, new ApiProblem("validation", "That person does not do this service.")));

        await screen.ChooseStaffAsync(Harness.StaffOf(screen, Sample.Cut, Sample.Ben.ToString()));

        screen.Message.ShouldBe("That person does not do this service.");
    }

    [Fact]
    public async Task Leaving_drops_the_visit_and_goes_back_to_the_services()
    {
        var app = new Harness();
        var screen = await app.LoadedTimeAsync(Sample.Cut);

        await screen.LeaveAsync();

        app.Session.Parts.ShouldBeEmpty();
        app.Navigator.Shown.ShouldBe([FakeNavigator.Services]);
        screen.HasAnswer.ShouldBeFalse();
    }

    [Fact]
    public async Task Bindings_hear_about_every_change()
    {
        var app = new Harness();
        app.Visit(Sample.Cut);
        var screen = app.TimeScreen();
        var changed = new List<string?>();
        screen.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await screen.LoadAsync();

        changed.ShouldContain(nameof(TimeScreen.MonthTitle));
        changed.ShouldContain(nameof(TimeScreen.Days));
        changed.ShouldContain(nameof(TimeScreen.Times));
        changed.ShouldContain(nameof(TimeScreen.Cards));
        changed.ShouldContain(nameof(TimeScreen.TotalText));
        changed.ShouldContain(nameof(TimeScreen.HasAnswer));
        changed.ShouldContain(nameof(TimeScreen.IsBusy));
    }

    /// <summary>Holds every availability call until the test answers it, from the sample server.</summary>
    private sealed class GatedAvailability
    {
        private readonly Harness _app;
        private readonly bool _throwWhenCancelled;

        public GatedAvailability(Harness app, bool throwWhenCancelled = false)
        {
            _app = app;
            _throwWhenCancelled = throwWhenCancelled;
            app.Api.OnAvailability = Hold;
        }

        public List<(AvailabilityRequest Request, CancellationToken Token, TaskCompletionSource<ApiResult<Availability>> Answer)> Calls { get; } = [];

        public void Answer(int call)
        {
            var (request, _, answer) = Calls[call];
            answer.SetResult(ApiResult.Ok(_app.Server.Availability(request)));
        }

        private Task<ApiResult<Availability>> Hold(AvailabilityRequest request, CancellationToken token)
        {
            var answer = new TaskCompletionSource<ApiResult<Availability>>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_throwWhenCancelled)
            {
                token.Register(() => answer.TrySetCanceled(token));
            }

            Calls.Add((request, token, answer));
            return answer.Task;
        }
    }
}