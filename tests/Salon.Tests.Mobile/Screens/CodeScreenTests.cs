using Salon.Api.Contracts.Booking;
using Salon.Mobile.Core.Api;
using Salon.Mobile.Core.Localization;
using Salon.Mobile.Core.Screens.Code;
using Salon.Tests.Mobile.Fakes;

namespace Salon.Tests.Mobile.Screens;

public class CodeScreenTests
{
    [Fact]
    public async Task It_says_where_the_code_went()
    {
        var (_, screen) = await LoadedAsync();

        screen.SentText.ShouldBe("We've texted a 6-digit code to +44 •••• ••• 789. It is valid for 10 minutes.");
        screen.Code.ShouldBeEmpty();
        screen.CanVerify.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_a_code_sent_it_goes_back_to_the_time_screen()
    {
        var app = new Harness();
        var screen = app.CodeScreen();

        await screen.LoadAsync();

        app.Navigator.Shown.ShouldBe([FakeNavigator.Time]);
    }

    [Theory]
    [InlineData("12 34 56", "123456")]
    [InlineData("1234567", "123456")]
    [InlineData("12a3", "123")]
    [InlineData("١٢٣", "")]
    public void The_field_keeps_six_plain_digits_at_most(string typed, string kept)
    {
        var screen = new Harness().CodeScreen();

        screen.Code = typed;

        screen.Code.ShouldBe(kept);
        screen.CanVerify.ShouldBe(kept.Length == 6);
    }

    [Fact]
    public async Task Fewer_than_six_digits_are_not_sent()
    {
        var (app, screen) = await LoadedAsync();
        screen.Code = "12345";

        await screen.VerifyAsync();

        screen.CodeError.ShouldBe("Enter the 6-digit code.");
        app.Api.VerifyRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Booked_goes_to_done()
    {
        var (app, screen) = await LoadedAsync();
        screen.Code = "123456";

        await screen.VerifyAsync();

        app.Api.VerifyRequests.ShouldHaveSingleItem().ShouldBe((Sample.VerificationId, new VerifyRequest("123456")));
        app.Session.AppointmentId.ShouldBe(Sample.AppointmentId);
        app.Session.VerificationId.ShouldBeNull();
        app.Navigator.Last.ShouldBe(FakeNavigator.Done);
    }

    [Theory]
    [InlineData(2, "That code isn't right. You have 2 attempts left.")]
    [InlineData(1, "That code isn't right. You have 1 attempt left.")]
    public async Task A_wrong_code_shows_the_attempts_left_and_stays(int attemptsLeft, string expected)
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.WrongCode, null, attemptsLeft));
        screen.Code = "000000";

        await screen.VerifyAsync();

        screen.CodeError.ShouldBe(expected);
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);
        app.Session.VerificationId.ShouldBe(Sample.VerificationId);

        screen.Code = "1";
        screen.CodeError.ShouldBeNull();
    }

    [Fact]
    public async Task A_wrong_code_with_no_attempts_left_is_too_many()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.WrongCode, null, 0));
        screen.Code = "000000";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "Too many wrong codes. Choose your time again and we will text you a new one.", timeKept: true);
    }

    [Fact]
    public async Task Too_many_attempts_goes_back_to_the_time_with_the_time_kept()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.TooManyAttempts, null, 0));
        screen.Code = "000000";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "Too many wrong codes. Choose your time again and we will text you a new one.", timeKept: true);
    }

    [Fact]
    public async Task An_expired_code_goes_back_to_the_time_with_the_time_kept()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.Expired, null, 0));
        screen.Code = "123456";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "This code has expired. Choose your time again and we will text you a new one.", timeKept: true);
    }

    [Fact]
    public async Task A_time_taken_meanwhile_goes_back_to_the_time_without_it()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.SlotTaken, null, 0));
        screen.Code = "123456";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "Sorry, that time has just gone. Please pick another.", timeKept: false);
    }

    [Fact]
    public async Task A_verification_the_server_no_longer_knows_goes_back_to_the_time()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse(VerifyOutcomes.NotFound, null, 0));
        screen.Code = "123456";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "We could not find this booking any more. Please choose your time again.", timeKept: true);
    }

    [Fact]
    public async Task A_404_refusal_is_the_same_as_not_found()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Refused<VerifyResponse>(404, new ApiProblem("notFound", string.Empty));
        screen.Code = "123456";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "We could not find this booking any more. Please choose your time again.", timeKept: true);
    }

    [Fact]
    public async Task A_409_refusal_is_the_same_as_slot_taken()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Refused<VerifyResponse>(409, new ApiProblem("slotTaken", "Gone."));
        screen.Code = "123456";

        await screen.VerifyAsync();

        await ShouldBeBackAtTimeAsync(app, "Sorry, that time has just gone. Please pick another.", timeKept: false);
    }

    [Fact]
    public async Task Too_many_requests_shows_the_servers_sentence_and_stays()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Refused<VerifyResponse>(429, new ApiProblem("tooManyCodes", "Slow down."));
        screen.Code = "123456";

        await screen.VerifyAsync();

        screen.Message.ShouldBe("Slow down.");
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);
    }

    [Fact]
    public async Task A_validation_refusal_lands_under_the_field()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Refused<VerifyResponse>(400, new ApiProblem("validation", string.Empty));
        screen.Code = "123456";

        await screen.VerifyAsync();

        screen.CodeError.ShouldBe("Enter the 6-digit code.");
    }

    [Fact]
    public async Task Offline_says_so_and_keeps_the_code()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Offline<VerifyResponse>();
        screen.Code = "123456";

        await screen.VerifyAsync();

        screen.Message.ShouldBe(app.Text[TextKeys.Offline]);
        screen.Code.ShouldBe("123456");
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);
    }

    [Fact]
    public async Task An_outcome_the_app_does_not_know_is_unexpected()
    {
        var (app, screen) = await LoadedAsync();
        app.Api.OnVerify = (_, _) => ApiResult.Ok(new VerifyResponse("somethingNew", null, 0));
        screen.Code = "123456";

        await screen.VerifyAsync();

        screen.Message.ShouldBe(app.Text[TextKeys.Unexpected]);
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);
    }

    [Fact]
    public async Task A_new_code_starts_with_an_empty_field()
    {
        var (app, screen) = await LoadedAsync();
        screen.Code = "123";

        await screen.LoadAsync();
        screen.Code.ShouldBe("123");

        app.Session.CodeSent(Guid.CreateVersion7(), "+44 ••• 111");
        await screen.LoadAsync();
        screen.Code.ShouldBeEmpty();
    }

    private static async Task<(Harness App, CodeScreen Screen)> LoadedAsync()
    {
        var app = new Harness();
        await app.ReadyForDetailsAsync();
        var details = app.DetailsScreen();
        await details.LoadAsync();
        details.FirstName = "Ada";
        details.LastName = "Lovelace";
        details.Phone = "07700900789";
        await details.SubmitAsync();
        app.Navigator.Last.ShouldBe(FakeNavigator.Code);

        var screen = app.CodeScreen();
        await screen.LoadAsync();
        return (app, screen);
    }

    private static async Task ShouldBeBackAtTimeAsync(Harness app, string message, bool timeKept)
    {
        app.Navigator.Last.ShouldBe(FakeNavigator.Time);
        app.Session.VerificationId.ShouldBeNull();
        app.Session.Parts.Count.ShouldBe(2);
        app.Session.Start.ShouldBe(timeKept ? "2026-11-26T09:45:00Z" : null);

        var time = app.TimeScreen();
        await time.LoadAsync();
        time.Message.ShouldBe(message);
    }
}