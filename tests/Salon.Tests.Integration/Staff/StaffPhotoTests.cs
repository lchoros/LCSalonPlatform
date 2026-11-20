using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Staff;

/// <summary>
/// Staff profile photos over HTTP (ADR 0088): the owner uploads a picture on the staff edit page and the face shows
/// on the staff list and in the public "who with" picker; a file is judged by its bytes and its size; removing falls
/// back to the letter; the picture is served to visitors for active people of this salon only; the gates (Staff.Manage,
/// sign-in, anti-forgery, tenant); and the table is guarded by row-level security like every tenant table.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class StaffPhotoTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    /// <summary>A real one-pixel PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>The start of a JPEG; enough for the rule, which reads the signature only.</summary>
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _ritaId;
    private Guid _ewaId;
    private Guid _serviceId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var now = SystemClock.Instance.GetCurrentInstant();
            var anna = new StaffMember { DisplayName = "Anna Kowalska", SortOrder = 1, CreatedAt = now };
            var tom = new StaffMember { DisplayName = "Tom Reed", SortOrder = 2, CreatedAt = now };
            var rita = new StaffMember { DisplayName = "Rita Retired", SortOrder = 3, CreatedAt = now, IsActive = false, IsBookable = false };
            db.StaffMembers.AddRange(anna, tom, rita);
            foreach (var day in WorkingHoursRules.Week)
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = tom.Id, Day = day, Start = new LocalTime(10, 0), End = new LocalTime(14, 0) });
            }

            var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP", SortOrder = 1 };
            db.Services.Add(cut);
            await db.SaveChangesAsync();
            (_annaId, _tomId, _ritaId, _serviceId) = (anna.Id, tom.Id, rita.Id, cut.Id);
        }

        await using var polishScope = factory.CreateTenantScope(_polish.Id);
        var polishDb = polishScope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var ewa = new StaffMember { DisplayName = "Ewa Nowak", SortOrder = 1, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        polishDb.StaffMembers.Add(ewa);
        await polishDb.SaveChangesAsync();
        _ewaId = ewa.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_edit_page_carries_the_photo_form_with_the_letter_until_a_picture_is_uploaded()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetOkAsync(owner, EditPath(_annaId));

        html.ShouldContain("data-testid=\"staff-photo-form\"");
        html.ShouldContain($"action=\"/admin/staff/{_annaId}/photo\"");
        html.ShouldContain("enctype=\"multipart/form-data\"");
        html.ShouldContain("accept=\"image/jpeg,image/png,image/webp\"");
        html.ShouldContain("at most 512 KB");
        html.ShouldContain("data-avatar=\"letter\"");
        html.ShouldNotContain("data-avatar=\"photo\"");
        html.ShouldNotContain("data-testid=\"staff-photo-remove\"", customMessage: "there is nothing to remove yet");
        html.ShouldContain("data-testid=\"edit-staff-form\"", customMessage: "the details form is still there");
    }

    [Fact]
    public async Task An_uploaded_picture_shows_on_the_staff_list_and_in_the_public_picker()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await UploadAsync(owner, _annaId, Png, "anna.png", "image/png");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(EditPath(_annaId));
        var stored = (await PhotoAsync(_studio.Id, _annaId)).ShouldNotBeNull();
        stored.ContentType.ShouldBe("image/png");
        stored.Bytes.ShouldBe(Png);
        var address = $"/staff-photo/{_annaId}?v={StaffPhotoRules.VersionOf(stored.UpdatedAt)}";

        var edit = await GetOkAsync(owner, EditPath(_annaId));
        edit.ShouldContain("Photo saved.");
        edit.ShouldContain($"src=\"{address}\"");
        edit.ShouldContain("data-testid=\"staff-photo-remove\"");

        var list = await GetOkAsync(owner, "/admin/staff");
        Avatars(list).ShouldBe([$"photo:{address}", "letter:T", "letter:R"], "Anna's face, and the first letter of the two without a picture");
        list.ShouldContain($"<img src=\"{address}\" alt=\"\"", customMessage: "decorative next to the visible name");

        using var visitor = factory.ClientFor(HostA);
        var picker = await GetOkAsync(visitor, $"/book/{_serviceId}");
        picker.ShouldContain("data-testid=\"staff-picker\"");
        Avatars(picker).ShouldBe([$"photo:{address}", "letter:T"], "the two people a client may choose; 'anyone' has no face");
        picker.ShouldContain("data-testid=\"staff-any\"");
        StaffOptionCount().Count(picker).ShouldBe(2);
        picker.ShouldContain($"href=\"/book/{_serviceId}?date=");
    }

    [Fact]
    public async Task The_picture_is_served_to_a_visitor_as_what_its_bytes_are_and_may_be_kept_for_good()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        // The browser's word and the file name both say JPEG; the bytes say PNG.
        (await UploadAsync(owner, _annaId, Png, "holiday in Rochdale.jpg", "image/jpeg")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var version = await VersionAsync(_studio.Id, _annaId);
        using var visitor = factory.ClientFor(HostA);

        var response = await visitor.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("image/png");
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl.ShouldNotBeNull().ToString().ShouldBe("public, max-age=31536000, immutable");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse("a picture anybody may cache sets no cookie");
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'none'; style-src 'unsafe-inline'; sandbox"]);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe($"\"{version}\"");

        // Asked again with what it already holds, a browser is told to keep it and is sent nothing.
        using var again = new HttpRequestMessage(HttpMethod.Get, new Uri($"/staff-photo/{_annaId}", UriKind.Relative));
        again.Headers.IfNoneMatch.ParseAdd($"\"{version}\"");
        (await visitor.SendAsync(again)).StatusCode.ShouldBe(HttpStatusCode.NotModified);

        // The owner's own request carried the sign-in cookie: the same picture, never for a cache others share. (This
        // host renews the cookie on every request, which makes the answer no-store; the unit test has the header we choose.)
        var forOwner = await owner.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative));
        forOwner.StatusCode.ShouldBe(HttpStatusCode.OK);
        forOwner.Headers.CacheControl.ShouldNotBeNull().Public.ShouldBeFalse();
    }

    [Fact]
    public async Task An_address_without_the_current_version_gets_the_current_picture_but_must_ask_again()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _annaId, Png, "a.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var first = await VersionAsync(_studio.Id, _annaId);
        (await UploadAsync(owner, _annaId, Jpeg, "b.jpg", "image/jpeg")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var second = await VersionAsync(_studio.Id, _annaId);
        using var visitor = factory.ClientFor(HostA);

        second.ShouldNotBe(first, "a new picture has a new address");
        (await PhotoCountAsync(_studio.Id)).ShouldBe(1, "the second upload replaced the first");
        foreach (var address in new[] { $"/staff-photo/{_annaId}?v={first}", $"/staff-photo/{_annaId}", $"/staff-photo/{_annaId}?v=nonsense" })
        {
            var stale = await visitor.GetAsync(new Uri(address, UriKind.Relative));
            stale.StatusCode.ShouldBe(HttpStatusCode.OK, address);
            stale.Content.Headers.ContentType.ShouldNotBeNull().ToString().ShouldBe("image/jpeg", address);
            (await stale.Content.ReadAsByteArrayAsync()).ShouldBe(Jpeg, address);
            stale.Headers.CacheControl.ShouldNotBeNull().ToString().ShouldBe("no-cache", address);
        }

        var current = await visitor.GetAsync(new Uri($"/staff-photo/{_annaId}?v={second}", UriKind.Relative));
        current.Headers.CacheControl.ShouldNotBeNull().ToString().ShouldBe("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task A_text_file_renamed_to_jpg_is_refused_with_a_sentence()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await UploadAsync(owner, _annaId, Encoding.UTF8.GetBytes("name,phone\nAnna,07700 900123\n"), "photo.jpg", "image/jpeg");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(EditPath(_annaId));
        var edit = await GetOkAsync(owner, EditPath(_annaId));
        edit.ShouldContain("data-testid=\"staff-photo-problem\"");
        edit.ShouldContain("That file is not a JPEG, PNG or WebP picture.");
        edit.ShouldNotContain("Photo saved.");
        (await PhotoAsync(_studio.Id, _annaId)).ShouldBeNull();
    }

    [Fact]
    public async Task An_svg_and_a_gif_are_refused_too()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        await UploadAsync(owner, _annaId, Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "face.svg", "image/svg+xml");
        (await GetOkAsync(owner, EditPath(_annaId))).ShouldContain("That file is not a JPEG, PNG or WebP picture.");
        await UploadAsync(owner, _annaId, Encoding.ASCII.GetBytes("GIF89a\u0001\u0000\u0001\u0000\u0000\u0000\u0000"), "face.gif", "image/gif");
        (await GetOkAsync(owner, EditPath(_annaId))).ShouldContain("That file is not a JPEG, PNG or WebP picture.");

        (await PhotoCountAsync(_studio.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_picture_over_512_KB_is_refused_and_one_of_exactly_512_KB_is_taken()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var tooLarge = await UploadAsync(owner, _annaId, Padded(Png, StaffPhotoRules.MaxBytes + 1), "large.png", "image/png");

        tooLarge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var edit = await GetOkAsync(owner, EditPath(_annaId));
        edit.ShouldContain("data-testid=\"staff-photo-problem\"");
        edit.ShouldContain("That picture is larger than 512 KB.");
        (await PhotoAsync(_studio.Id, _annaId)).ShouldBeNull();

        (await UploadAsync(owner, _annaId, Padded(Png, StaffPhotoRules.MaxBytes), "just.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PhotoAsync(_studio.Id, _annaId)).ShouldNotBeNull().Bytes.Length.ShouldBe(StaffPhotoRules.MaxBytes);
    }

    [Fact]
    public async Task A_photo_the_size_of_a_phone_camera_picture_still_gets_the_sentence_not_a_bare_error()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await UploadAsync(owner, _annaId, Padded(Jpeg, 6 * 1024 * 1024), "IMG_0042.jpg", "image/jpeg");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(owner, EditPath(_annaId))).ShouldContain("That picture is larger than 512 KB.");
        (await PhotoCountAsync(_studio.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Posting_no_file_asks_for_one()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var form = new MultipartFormDataContent { { new StringContent(await TokenAsync(owner, _annaId)), "__RequestVerificationToken" } };

        var response = await owner.PostAsync(new Uri(PhotoPath(_annaId), UriKind.Relative), form);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOkAsync(owner, EditPath(_annaId))).ShouldContain("Choose a picture first.");
    }

    [Fact]
    public async Task The_sentences_are_in_the_salons_language()
    {
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var form = await GetOkAsync(owner, EditPath(_ewaId));
        form.ShouldContain("Zdjęcie");
        form.ShouldContain("najwyżej 512 KB");

        await UploadAsync(owner, _ewaId, Encoding.UTF8.GetBytes("to nie jest zdjęcie"), "zdjecie.png", "image/png");
        (await GetOkAsync(owner, EditPath(_ewaId))).ShouldContain("Ten plik nie jest zdjęciem JPEG, PNG ani WebP.");

        await UploadAsync(owner, _ewaId, Padded(Png, StaffPhotoRules.MaxBytes + 1), "duze.png", "image/png");
        (await GetOkAsync(owner, EditPath(_ewaId))).ShouldContain("To zdjęcie ma więcej niż 512 KB.");

        await UploadAsync(owner, _ewaId, Png, "ewa.png", "image/png");
        (await GetOkAsync(owner, EditPath(_ewaId))).ShouldContain("Zdjęcie zapisane.");
    }

    [Fact]
    public async Task Removing_the_photo_deletes_it_and_falls_back_to_the_letter()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _annaId, Png, "anna.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var version = await VersionAsync(_studio.Id, _annaId);

        var response = await PostAsync(owner, _annaId, PhotoPath(_annaId) + "/remove");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(EditPath(_annaId));
        (await PhotoCountAsync(_studio.Id)).ShouldBe(0, "removed means deleted: the picture is the person's own data");
        var edit = await GetOkAsync(owner, EditPath(_annaId));
        edit.ShouldContain("Photo removed.");
        edit.ShouldNotContain("data-avatar=\"photo\"");
        Avatars(await GetOkAsync(owner, "/admin/staff")).ShouldBe(["letter:A", "letter:T", "letter:R"]);
        using var visitor = factory.ClientFor(HostA);
        Avatars(await GetOkAsync(visitor, $"/book/{_serviceId}")).ShouldBe(["letter:A", "letter:T"]);
        (await visitor.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_the_staff_member_deletes_the_photo_with_them()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _ritaId, Png, "rita.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PhotoCountAsync(_studio.Id)).ShouldBe(1);

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            (await db.StaffMembers.Where(s => s.Id == _ritaId).ExecuteDeleteAsync()).ShouldBe(1);
        }

        (await PhotoCountAsync(_studio.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Another_salons_photo_is_not_found_and_cannot_be_changed()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _annaId, Png, "anna.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var version = await VersionAsync(_studio.Id, _annaId);
        using var polishVisitor = factory.ClientFor(HostB);
        using var polishOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        using var platform = factory.ClientFor("localhost");

        (await polishVisitor.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await polishOwner.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await platform.GetAsync(new Uri($"/staff-photo/{_annaId}?v={version}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The Polish owner's own form gives a valid token; Anna is still not theirs to touch.
        (await UploadWithTokenOfAsync(polishOwner, _ewaId, _annaId, Jpeg)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostWithTokenOfAsync(polishOwner, _ewaId, PhotoPath(_annaId) + "/remove")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PhotoAsync(_studio.Id, _annaId)).ShouldNotBeNull().Bytes.ShouldBe(Png);
        (await PhotoCountAsync(_polish.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task An_inactive_staff_members_photo_is_not_served_to_a_visitor()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _ritaId, Png, "rita.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await UploadAsync(owner, _tomId, Png, "tom.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var rita = $"/staff-photo/{_ritaId}?v={await VersionAsync(_studio.Id, _ritaId)}";
        var tom = $"/staff-photo/{_tomId}?v={await VersionAsync(_studio.Id, _tomId)}";
        using var visitor = factory.ClientFor(HostA);

        (await visitor.GetAsync(new Uri(rita, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner still sees her on the staff list, face and all; that copy is theirs alone to keep.
        var forOwner = await owner.GetAsync(new Uri(rita, UriKind.Relative));
        forOwner.StatusCode.ShouldBe(HttpStatusCode.OK);
        forOwner.Headers.CacheControl.ShouldNotBeNull().Public.ShouldBeFalse();
        (await forOwner.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        Avatars(await GetOkAsync(owner, "/admin/staff")).ShouldContain($"photo:{rita}");

        // Somebody who stops working here stops being shown at once.
        (await visitor.GetAsync(new Uri(tom, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            await db.StaffMembers.Where(s => s.Id == _tomId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, false));
        }

        (await visitor.GetAsync(new Uri(tom, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_receptionist_cannot_upload_or_remove_and_a_manager_can()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await UploadAsync(owner, _tomId, Png, "tom.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var token = await TokenFromAsync(receptionist, "/admin/appointments");

        var upload = await UploadWithTokenAsync(receptionist, token, _annaId, Png);
        var remove = await receptionist.PostAsync(
            new Uri(PhotoPath(_tomId) + "/remove", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));

        upload.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        upload.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        remove.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        remove.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await PhotoAsync(_studio.Id, _annaId)).ShouldBeNull();
        (await PhotoAsync(_studio.Id, _tomId)).ShouldNotBeNull();

        (await UploadAsync(manager, _annaId, Png, "anna.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PhotoAsync(_studio.Id, _annaId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Without_a_sign_in_or_the_anti_forgery_token_nothing_is_stored()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        using var anonymous = factory.ClientFor(HostA);

        using var unsigned = FileForm(Png, "anna.png", "image/png");
        var noToken = await owner.PostAsync(new Uri(PhotoPath(_annaId), UriKind.Relative), unsigned);
        using var again = FileForm(Png, "anna.png", "image/png");
        var noSignIn = await anonymous.PostAsync(new Uri(PhotoPath(_annaId), UriKind.Relative), again);

        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noSignIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        noSignIn.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await PhotoCountAsync(_studio.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_person_the_salon_does_not_have_is_not_found()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var nobody = Guid.NewGuid();

        (await UploadWithTokenOfAsync(owner, _annaId, nobody, Png)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostWithTokenOfAsync(owner, _annaId, PhotoPath(nobody) + "/remove")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var visitor = factory.ClientFor(HostA);
        (await visitor.GetAsync(new Uri($"/staff-photo/{nobody}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync(new Uri($"/staff-photo/{_annaId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound, "Anna has no picture yet");
        (await visitor.GetAsync(new Uri("/staff-photo/not-an-id", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_database_itself_keeps_one_salons_photos_from_another()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await UploadAsync(owner, _annaId, Png, "anna.png", "image/png")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await AppRole.EnsureAsync();

        (await CountAsAppRoleAsync(_studio.Id)).ShouldBe(1);
        (await CountAsAppRoleAsync(_polish.Id)).ShouldBe(0);
        (await CountAsAppRoleAsync(null)).ShouldBe(0);

        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        await using var applied = new NpgsqlCommand("SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id LIKE '%\\_StaffPhotos'", connection);
        ((long)(await applied.ExecuteScalarAsync())!).ShouldBe(1, "the StaffPhotos migration is applied");
        await using var policy = new NpgsqlCommand(
            """
            SELECT c.relrowsecurity AND c.relforcerowsecurity, (SELECT string_agg(p.polname, ',') FROM pg_policy p WHERE p.polrelid = c.oid)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'salon' AND c.relname = 'staff_photos'
            """, connection);
        await using var reader = await policy.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue("table salon.staff_photos not found");
        reader.GetBoolean(0).ShouldBeTrue("row level security is enabled and forced");
        reader.GetString(1).ShouldBe("tenant_isolation");
    }

    private static string EditPath(Guid staffMemberId) => $"/admin/staff/{staffMemberId}/edit";

    private static string PhotoPath(Guid staffMemberId) => $"/admin/staff/{staffMemberId}/photo";

    /// <summary>The picture with zero bytes after it up to <paramref name="length"/>: still that kind of file by its first bytes.</summary>
    private static byte[] Padded(byte[] start, int length)
    {
        var bytes = new byte[length];
        start.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Every face on the page in order: "photo:" with its address or "letter:" with the letter shown.</summary>
    private static List<string> Avatars(string html)
        => AvatarPattern().Matches(html)
            .Select(m => m.Groups["src"].Success ? "photo:" + WebUtility.HtmlDecode(m.Groups["src"].Value) : "letter:" + m.Groups["letter"].Value)
            .ToList();

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> TokenFromAsync(HttpClient client, string path)
        => IdentityTestClient.AntiForgeryToken(await GetOkAsync(client, path));

    private static Task<string> TokenAsync(HttpClient client, Guid staffMemberId) => TokenFromAsync(client, EditPath(staffMemberId));

    private static MultipartFormDataContent FileForm(byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "photo", fileName } };
    }

    private static async Task<HttpResponseMessage> UploadWithTokenAsync(HttpClient client, string token, Guid staffMemberId, byte[] content, string fileName = "photo.png", string contentType = "image/png")
    {
        using var form = FileForm(content, fileName, contentType);
        form.Add(new StringContent(token), "__RequestVerificationToken");
        return await client.PostAsync(new Uri(PhotoPath(staffMemberId), UriKind.Relative), form);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid staffMemberId, byte[] content, string fileName, string contentType)
        => await UploadWithTokenAsync(client, await TokenAsync(client, staffMemberId), staffMemberId, content, fileName, contentType);

    /// <summary>Posts the file for <paramref name="targetId"/> with a token taken from the form of <paramref name="ownId"/>, a person the client may open.</summary>
    private static async Task<HttpResponseMessage> UploadWithTokenOfAsync(HttpClient client, Guid ownId, Guid targetId, byte[] content)
        => await UploadWithTokenAsync(client, await TokenAsync(client, ownId), targetId, content);

    private static async Task<HttpResponseMessage> PostWithTokenOfAsync(HttpClient client, Guid ownId, string path)
        => await client.PostAsync(
            new Uri(path, UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", await TokenAsync(client, ownId))]));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid staffMemberId, string path) => PostWithTokenOfAsync(client, staffMemberId, path);

    private async Task<StaffPhoto?> PhotoAsync(Guid tenantId, Guid staffMemberId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.StaffMemberId == staffMemberId);
    }

    private async Task<string> VersionAsync(Guid tenantId, Guid staffMemberId)
        => StaffPhotoRules.VersionOf((await PhotoAsync(tenantId, staffMemberId)).ShouldNotBeNull().UpdatedAt);

    private async Task<int> PhotoCountAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffPhotos.CountAsync();
    }

    private static async Task<long> CountAsAppRoleAsync(Guid? tenantId)
    {
        await using var connection = new NpgsqlConnection(AppRole.ConnectionString);
        await connection.OpenAsync();
        await using var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, false)", connection);
        set.Parameters.AddWithValue("tenant", tenantId?.ToString() ?? string.Empty);
        await set.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM salon.staff_photos", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [GeneratedRegex("<img src=\"(?<src>[^\"]+)\" alt=\"\"[^>]*data-avatar=\"photo\"|data-avatar=\"letter\"[^>]*>(?<letter>[^<]*)<")]
    private static partial Regex AvatarPattern();

    [GeneratedRegex("data-testid=\"staff-option\"")]
    private static partial Regex StaffOptionCount();
}