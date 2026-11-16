using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Clients;

/// <summary>
/// Consultation forms (ADR 0058): what a client said about their health before a treatment. The answers are in the
/// database as cipher text only and are read on the form's own page, by the logins that may open the client's card;
/// the card lists when and for what, never what was said. A form is complete and confirmed or it is not kept; the
/// same form twice is one record; a merged card's forms follow it; another salon has neither the client nor the form.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ConsultationTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Consent = "I confirm that these answers are true.";
    private const string Said = "Came up in a rash after a box dye in 2024";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_answers_are_kept_as_cipher_text_and_read_back_whole()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);
        var answers = Answers(ConsultationQuestions.HadReaction, ConsultationQuestions.Medication);

        var result = await ConsultationsAsync(s => s.RecordAsync(Request(answers, details: $"  {Said}  ", patchTestOn: Today.PlusDays(-2), takenFor: "  Full head colour "), user.Id));

        result.Outcome.ShouldBe(ConsultationOutcome.Recorded);
        await InScopeAsync(async db =>
        {
            var row = await db.ConsultationRecords.AsNoTracking().SingleAsync();
            (row.Id, row.ClientId, row.TakenByUserId, row.For, row.ConsentWording, row.FormVersion)
                .ShouldBe((result.Id!.Value, _clientId, user.Id, "Full head colour", Consent, ConsultationQuestions.Version));
            // Nothing a person could read: not what was said, not the name typed, not which question it was.
            row.AnswersProtected.ShouldNotContain("rash");
            row.AnswersProtected.ShouldNotContain("Olivia");
            row.AnswersProtected.ShouldNotContain(ConsultationQuestions.HadReaction);
            row.AnswersProtected.ShouldNotContain("true");
        });

        var read = (await ConsultationsAsync(s => s.GetAsync(_clientId, result.Id!.Value))).ShouldNotBeNull();
        read.Unreadable.ShouldBeFalse();
        (read.ClientName, read.ConsentWording, read.Summary.For, read.Summary.TakenOn, read.Summary.TakenByName).ShouldBe(("Olivia Brown", Consent, "Full head colour", Today, user.DisplayName));
        var kept = read.Answers.ShouldNotBeNull();
        (kept.Details, kept.PatchTestOn, kept.ConfirmedBy).ShouldBe((Said, Today.PlusDays(-2), "Olivia Brown"));
        kept.Concerns.ShouldBe([ConsultationQuestions.HadReaction, ConsultationQuestions.Medication]);
        kept.Answers.Count.ShouldBe(ConsultationQuestions.All.Count);

        var list = await ConsultationsAsync(s => s.ListAsync(_clientId));
        list.ShouldHaveSingleItem().ShouldBe(new ConsultationSummary(result.Id!.Value, Today, user.DisplayName, "Full head colour"));
    }

    [Fact]
    public async Task A_patch_test_on_the_form_moves_the_date_on_the_card_forward_only()
    {
        await ConsultationsAsync(s => s.RecordAsync(Request(Answers(), patchTestOn: Today.PlusDays(-3)), null));
        (await PatchTestOnCardAsync()).ShouldBe(Today.PlusDays(-3));

        // An older form typed in later does not take the card back.
        await ConsultationsAsync(s => s.RecordAsync(Request(Answers(), patchTestOn: Today.PlusDays(-30)), null));
        (await PatchTestOnCardAsync()).ShouldBe(Today.PlusDays(-3));

        await ConsultationsAsync(s => s.RecordAsync(Request(Answers(), patchTestOn: Today), null));
        (await PatchTestOnCardAsync()).ShouldBe(Today);

        // A form without a test leaves the card as it is.
        await ConsultationsAsync(s => s.RecordAsync(Request(Answers()), null));
        (await PatchTestOnCardAsync()).ShouldBe(Today);
        (await ConsultationsAsync(s => s.ListAsync(_clientId))).Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_form_that_is_not_finished_or_not_confirmed_is_not_kept()
    {
        var skipped = Answers();
        skipped.Remove(ConsultationQuestions.Pregnant);
        (await OutcomeAsync(Request(skipped))).ShouldBe(ConsultationOutcome.Incomplete);
        (await OutcomeAsync(Request(Answers()) with { Answers = null })).ShouldBe(ConsultationOutcome.Incomplete);
        (await OutcomeAsync(Request(Answers()) with { Agreed = false })).ShouldBe(ConsultationOutcome.NotConfirmed);
        (await OutcomeAsync(Request(Answers()) with { ConfirmedBy = " " })).ShouldBe(ConsultationOutcome.NotConfirmed);
        (await OutcomeAsync(Request(Answers()) with { ConsentWording = " " })).ShouldBe(ConsultationOutcome.NotConfirmed);
        (await OutcomeAsync(Request(Answers(), details: new string('a', ConsultationRecord.DetailsMaxLength + 1)))).ShouldBe(ConsultationOutcome.TooLong);
        (await OutcomeAsync(Request(Answers(), takenFor: new string('a', ConsultationRecord.ForMaxLength + 1)))).ShouldBe(ConsultationOutcome.TooLong);
        (await OutcomeAsync(Request(Answers(), patchTestOn: Today.PlusDays(1)))).ShouldBe(ConsultationOutcome.PatchTestDayInvalid);
        (await OutcomeAsync(Request(Answers()) with { ClientId = Guid.NewGuid() })).ShouldBe(ConsultationOutcome.ClientUnknown);

        await InScopeAsync(async db => (await db.ConsultationRecords.CountAsync()).ShouldBe(0));
        (await PatchTestOnCardAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task The_longest_form_fits_the_column()
    {
        var result = await ConsultationsAsync(s => s.RecordAsync(
            Request(Answers([.. ConsultationQuestions.All]), details: new string('ł', ConsultationRecord.DetailsMaxLength), patchTestOn: Today, takenFor: new string('a', ConsultationRecord.ForMaxLength))
                with
            { ConfirmedBy = new string('ż', Client.NameMaxLength * 2) },
            null));

        result.Outcome.ShouldBe(ConsultationOutcome.Recorded);
        var read = (await ConsultationsAsync(s => s.GetAsync(_clientId, result.Id!.Value))).ShouldNotBeNull();
        read.Answers.ShouldNotBeNull().Details.ShouldNotBeNull().Length.ShouldBe(ConsultationRecord.DetailsMaxLength);
    }

    [Fact]
    public async Task The_same_form_sent_twice_is_one_record()
    {
        var formId = Guid.CreateVersion7();

        var first = await ConsultationsAsync(s => s.RecordAsync(Request(Answers()) with { RecordId = formId }, null));
        var second = await ConsultationsAsync(s => s.RecordAsync(Request(Answers(ConsultationQuestions.Allergies)) with { RecordId = formId }, null));

        (first.Outcome, first.Id).ShouldBe((ConsultationOutcome.Recorded, formId));
        (second.Outcome, second.Id).ShouldBe((ConsultationOutcome.Recorded, formId));
        (await ConsultationsAsync(s => s.ListAsync(_clientId))).ShouldHaveSingleItem();
        // What was confirmed the first time is what is kept.
        (await ConsultationsAsync(s => s.GetAsync(_clientId, formId))).ShouldNotBeNull().Answers.ShouldNotBeNull().Concerns.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_form_is_taken_for_a_card_that_is_inactive_or_merged_away_and_a_merged_cards_forms_follow_it()
    {
        var duplicateId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Browne", "+447700900124");
        var taken = await ConsultationsAsync(s => s.RecordAsync(Request(Answers(ConsultationQuestions.Allergies), details: Said) with { ClientId = duplicateId }, null));
        taken.Outcome.ShouldBe(ConsultationOutcome.Recorded);

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<IClientService>().MergeAsync(_clientId, duplicateId, Guid.NewGuid());
        }

        (await ConsultationsAsync(s => s.ListAsync(duplicateId))).ShouldBeEmpty();
        (await ConsultationsAsync(s => s.ListAsync(_clientId))).ShouldHaveSingleItem().Id.ShouldBe(taken.Id!.Value);
        (await ConsultationsAsync(s => s.GetAsync(_clientId, taken.Id!.Value))).ShouldNotBeNull().Answers.ShouldNotBeNull().Details.ShouldBe(Said);
        (await ConsultationsAsync(s => s.GetAsync(duplicateId, taken.Id!.Value))).ShouldBeNull();

        (await OutcomeAsync(Request(Answers()) with { ClientId = duplicateId })).ShouldBe(ConsultationOutcome.ClientUnknown);
        (await ConsultationsAsync(s => s.FindClientAsync(duplicateId))).ShouldNotBeNull().CanRecord.ShouldBeFalse();
        (await ConsultationsAsync(s => s.FindClientAsync(_clientId))).ShouldNotBeNull().CanRecord.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_salon_has_neither_the_client_nor_the_form()
    {
        var taken = await ConsultationsAsync(s => s.RecordAsync(Request(Answers(ConsultationQuestions.Allergies), details: Said), null));

        (await ConsultationsAsync(s => s.GetAsync(_clientId, taken.Id!.Value), _polish.Id)).ShouldBeNull();
        (await ConsultationsAsync(s => s.ListAsync(_clientId), _polish.Id)).ShouldBeEmpty();
        (await ConsultationsAsync(s => s.FindClientAsync(_clientId), _polish.Id)).ShouldBeNull();
        (await ConsultationsAsync(s => s.RecordAsync(Request(Answers()), null), _polish.Id)).Outcome.ShouldBe(ConsultationOutcome.ClientUnknown);

        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await theirOwner.GetAsync(new Uri(FormPath(_clientId), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await theirOwner.GetAsync(new Uri(RecordPath(_clientId, taken.Id!.Value), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A form of this salon asked for under another client of this salon does not open either.
        var otherClient = await VisitSeed.ClientAsync(factory, _studio.Id, "Amelia", "Jones");
        (await ConsultationsAsync(s => s.GetAsync(otherClient, taken.Id!.Value))).ShouldBeNull();
    }

    [Fact]
    public async Task Answers_that_can_no_longer_be_opened_are_said_to_be_unreadable_not_shown_as_empty()
    {
        var taken = await ConsultationsAsync(s => s.RecordAsync(Request(Answers(), details: Said), null));
        await InScopeAsync(db => db.ConsultationRecords.ExecuteUpdateAsync(s => s.SetProperty(r => r.AnswersProtected, "not-cipher-text")));

        var read = (await ConsultationsAsync(s => s.GetAsync(_clientId, taken.Id!.Value))).ShouldNotBeNull();
        (read.Unreadable, read.Answers).ShouldBe((true, null));

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, RecordPath(_clientId, taken.Id!.Value));
        page.ShouldContain("data-testid=\"consultation-unreadable\"");
        page.ShouldNotContain("data-testid=\"consultation-answers\"");

        // Cipher text that opens but is not the form's document is unreadable too, not an error page.
        foreach (var damaged in new[] { "not json", "{}", "null" })
        {
            var cipher = factory.Services.GetRequiredService<Salon.Services.Security.IFieldEncryptor>().Protect(damaged);
            await InScopeAsync(db => db.ConsultationRecords.ExecuteUpdateAsync(s => s.SetProperty(r => r.AnswersProtected, cipher)));
            (await ConsultationsAsync(s => s.GetAsync(_clientId, taken.Id!.Value))).ShouldNotBeNull().Unreadable.ShouldBeTrue(damaged);
        }
    }

    [Fact]
    public async Task The_desk_fills_the_form_and_the_card_lists_it_without_what_was_said()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var card = await GetAsync(desk, CardPath(_clientId));
        card.ShouldContain("data-testid=\"no-consultations\"");
        card.ShouldContain($"href=\"{FormPath(_clientId)}\"");

        var form = await GetAsync(desk, FormPath(_clientId));
        form.ShouldContain("Olivia Brown");
        Regex.Count(form, "data-testid=\"question\"").ShouldBe(ConsultationQuestions.All.Count);

        var fields = Fields(form, Answers(ConsultationQuestions.HadReaction));
        fields["For"] = "Full head colour";
        fields["Details"] = Said;
        fields["PatchTestOn"] = LocalDatePattern.Iso.Format(Today.PlusDays(-2));
        // Whatever the post says the client agreed to, the record keeps the sentence the page showed.
        fields["ConsentWording"] = "I agree to anything";
        var saved = await desk.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(fields));

        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect, await saved.Content.ReadAsStringAsync());
        var recordPath = saved.Headers.Location.ShouldNotBeNull().OriginalString;
        recordPath.ShouldBe(RecordPath(_clientId, Guid.Parse(fields["RecordId"])));

        using var shown = await desk.GetAsync(new Uri(recordPath, UriKind.Relative));
        shown.StatusCode.ShouldBe(HttpStatusCode.OK);
        shown.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var record = WebUtility.HtmlDecode(await shown.Content.ReadAsStringAsync());
        record.ShouldContain("The consultation form is saved.");
        record.ShouldContain(Said);
        record.ShouldContain("For: Full head colour");
        record.ShouldContain("Confirmed by Olivia Brown");
        record.ShouldContain("I confirm that these answers are true and complete.");
        record.ShouldNotContain("I agree to anything");
        Regex.Count(record, "data-concern=\"true\"").ShouldBe(1);
        Regex.Count(record, "data-concern=\"false\"").ShouldBe(ConsultationQuestions.All.Count - 1);
        record.ShouldContain($"data-question=\"{ConsultationQuestions.HadReaction}\" data-concern=\"true\"");

        // The same post again (a reload) lands on the same record.
        var again = await desk.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(fields));
        again.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(recordPath);

        var cardAfter = await GetAsync(desk, CardPath(_clientId));
        Regex.Count(cardAfter, "data-testid=\"consultation-row\"").ShouldBe(1);
        cardAfter.ShouldContain($"href=\"{recordPath}\"");
        cardAfter.ShouldContain("Full head colour");
        cardAfter.ShouldNotContain("rash");
        (await PatchTestOnCardAsync()).ShouldBe(Today.PlusDays(-2));
    }

    [Fact]
    public async Task A_form_with_a_question_skipped_or_not_confirmed_comes_back_with_what_was_typed()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var form = await GetAsync(owner, FormPath(_clientId));

        var skipped = Answers(ConsultationQuestions.Allergies);
        skipped.Remove(ConsultationQuestions.Medication);
        var fields = Fields(form, skipped);
        fields["Details"] = Said;
        fields.Remove("Agreed");
        fields["ConfirmedBy"] = string.Empty;
        fields["PatchTestOn"] = "last week";
        var refused = await owner.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(fields));

        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        page.ShouldContain("Answer every question with Yes or No.");
        page.ShouldContain("The client ticks this box to confirm the answers.");
        page.ShouldContain("The client types their full name here.");
        page.ShouldContain("Enter the date of the patch test, or leave it empty.");
        page.ShouldContain(Said);
        // The form keeps its id, so the corrected post is still one record.
        page.ShouldContain($"name=\"RecordId\" value=\"{fields["RecordId"]}\"");
        Regex.Count(page, "checked=\"checked\"").ShouldBe(ConsultationQuestions.All.Count - 1);

        var future = Fields(form, Answers());
        future["PatchTestOn"] = LocalDatePattern.Iso.Format(Today.PlusDays(2));
        var tomorrow = await owner.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(future));
        WebUtility.HtmlDecode(await tomorrow.Content.ReadAsStringAsync()).ShouldContain("The patch test cannot be dated in the future.");

        var nonsense = Fields(form, Answers());
        nonsense[$"Answers[{ConsultationQuestions.Pregnant}]"] = "maybe";
        var notYesOrNo = await owner.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(nonsense));
        notYesOrNo.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await notYesOrNo.Content.ReadAsStringAsync()).ShouldContain("Answer every question with Yes or No.");

        await InScopeAsync(async db => (await db.ConsultationRecords.CountAsync()).ShouldBe(0));
    }

    [Fact]
    public async Task Only_the_logins_that_open_the_card_reach_the_forms_and_a_post_needs_the_token()
    {
        var taken = await ConsultationsAsync(s => s.RecordAsync(Request(Answers(), details: Said), null));
        string[] paths = [FormPath(_clientId), RecordPath(_clientId, taken.Id!.Value)];

        foreach (var role in new[] { Role.Owner, Role.Manager, Role.Receptionist })
        {
            using var allowed = await factory.SignedInClientAsync(HostA, _studio.Id, role);
            foreach (var path in paths)
            {
                (await allowed.GetAsync(new Uri(path, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, $"{role} {path}");
            }
        }

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var anonymous = factory.ClientFor(HostA);
        foreach (var path in paths)
        {
            var denied = await staff.GetAsync(new Uri(path, UriKind.Relative));
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
            var challenge = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
            challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        }

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner, "second-owner@studio-20.test");
        var fields = Fields(await GetAsync(owner, FormPath(_clientId)), Answers());
        fields.Remove("__RequestVerificationToken");
        (await owner.PostAsync(new Uri(FormPath(_clientId), UriKind.Relative), new FormUrlEncodedContent(fields))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync(new Uri(RecordPath(_clientId, Guid.NewGuid()), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync(new Uri(FormPath(Guid.NewGuid()), UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ConsultationsAsync(s => s.ListAsync(_clientId))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_form_speaks_the_salon_language()
    {
        var clientId = await VisitSeed.ClientAsync(factory, _polish.Id, "Zofia", "Kowalska");
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var form = await GetAsync(owner, FormPath(clientId));

        form.ShouldContain("Nowy formularz konsultacji");
        form.ShouldContain("Czy jesteś w ciąży lub karmisz piersią?");
        form.ShouldNotContain("Are you pregnant");
    }

    private static string CardPath(Guid clientId) => $"/admin/clients/{clientId}";

    private static string FormPath(Guid clientId) => $"/admin/clients/{clientId}/consultations/new";

    private static string RecordPath(Guid clientId, Guid id) => $"/admin/clients/{clientId}/consultations/{id}";

    /// <summary>Every question answered no, except the ones named.</summary>
    private static Dictionary<string, bool> Answers(params string[] yes) => ConsultationQuestions.All.ToDictionary(key => key, key => yes.Contains(key));

    private ConsultationRequest Request(Dictionary<string, bool> answers, string? details = null, LocalDate? patchTestOn = null, string? takenFor = null)
        => new(_clientId, takenFor, answers, details, patchTestOn, "Olivia Brown", true, Consent);

    /// <summary>The posted form: the page's token and record id, the answers, the tick and the name.</summary>
    private static Dictionary<string, string> Fields(string page, Dictionary<string, bool> answers)
    {
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(page),
            ["RecordId"] = RecordId().Match(page).Groups["id"].Value,
            ["Agreed"] = "true",
            ["ConfirmedBy"] = "Olivia Brown",
        };
        foreach (var (key, yes) in answers)
        {
            fields[$"Answers[{key}]"] = yes ? "true" : "false";
        }

        return fields;
    }

    private async Task<ConsultationOutcome> OutcomeAsync(ConsultationRequest request) => (await ConsultationsAsync(s => s.RecordAsync(request, null))).Outcome;

    private async Task<LocalDate?> PatchTestOnCardAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Clients.AsNoTracking().Where(c => c.Id == _clientId).Select(c => c.LastPatchTestOn).SingleAsync();
    }

    private async Task<T> ConsultationsAsync<T>(Func<IConsultationService, Task<T>> act, Guid? tenantId = null)
    {
        await using var scope = factory.CreateTenantScope(tenantId ?? _studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<IConsultationService>());
    }

    private async Task InScopeAsync(Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("name=\"RecordId\" value=\"(?<id>[0-9a-f-]{36})\"")]
    private static partial Regex RecordId();
}